using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Data.Ports;
using Oracle.ManagedDataAccess.Client;

namespace Billing.Invoicing.Data.Oracle;

/// <summary>Opens Oracle connections and transactional sessions from <see cref="InvoicingDataOptions"/>. UNVERIFIED against Oracle.</summary>
public sealed class OracleSessionFactory : IOracleSessionFactory
{
    private const string BlankConnectionStringMessage = "The Oracle connection string is not configured.";
    private const string OpenOperation = "connection open";

    private static readonly ConcurrentDictionary<string, OpenGate> OpenGates = new(StringComparer.Ordinal);

    private readonly InvoicingDataOptions _options;

    /// <summary>Stores the data-layer settings; opens nothing.</summary>
    /// <param name="options">Connection string and command settings.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="InvoicingDataOptions.CommandTimeoutSeconds"/> is below 1 or above <see cref="InvoicingDataOptions.MaxCommandTimeoutSeconds"/>.</exception>
    public OracleSessionFactory(InvoicingDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.EnsureCommandTimeout(nameof(options));

        _options = options;
    }

    /// <summary>Opens a connection within <see cref="InvoicingDataOptions.CommandTimeoutSeconds"/> and begins a read-committed transaction on it; an open that exceeds it fails with a <see cref="TimeoutException"/>.</summary>
    /// <param name="cancellationToken">Cancels the connection open.</param>
    /// <returns>The open session; disposing it uncommitted attempts to roll back its local transaction, and the outcome can remain uncertain.</returns>
    public async Task<IOracleSession> Open(CancellationToken cancellationToken = default)
    {
        var connection = await OpenConnection(_options, cancellationToken).ConfigureAwait(false);

        OracleTransaction transaction;
        try
        {
            transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        }
        catch (Exception exception)
        {
            await DisposeAfterFailure(connection, exception).ConfigureAwait(false);
            throw;
        }

        return new OracleSession(connection, transaction, TimeSpan.FromSeconds(_options.CommandTimeoutSeconds));
    }

    /// <summary>Opens a non-transactional connection within the configured deadline through the connection string's open gate and tags connection failures (D-89, D-161).</summary>
    /// <param name="options">Settings holding the connection string and the open deadline.</param>
    /// <param name="cancellationToken">Cancels the connection open.</param>
    /// <returns>An open connection the caller disposes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="InvoicingDataOptions.CommandTimeoutSeconds"/> is below 1 or above <see cref="InvoicingDataOptions.MaxCommandTimeoutSeconds"/>.</exception>
    /// <exception cref="InvalidOperationException">The connection string is blank.</exception>
    /// <exception cref="ArgumentException">The connection string is malformed.</exception>
    /// <exception cref="TimeoutException">The open did not complete within <see cref="InvoicingDataOptions.CommandTimeoutSeconds"/>.</exception>
    /// <exception cref="OracleOpenReleasedException">Another open of the same connection string failed while this one waited; that failure is the inner exception.</exception>
    internal static async Task<OracleConnection> OpenConnection(InvoicingDataOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.EnsureCommandTimeout(nameof(options));

        if (!options.HasConnectionString)
        {
            var blank = new InvalidOperationException(BlankConnectionStringMessage);
            blank.Data[OracleFailureTranslator.ConfigurationFaultKey] = true;
            throw blank;
        }

        OracleConnection connection;
        try
        {
            connection = new OracleConnection(options.ConnectionString);
        }
        catch (ArgumentException exception)
        {
            exception.Data[OracleFailureTranslator.ConfigurationFaultKey] = true;
            throw;
        }

        await OpenThroughGate(
            options.ConnectionString,
            TimeSpan.FromSeconds(options.CommandTimeoutSeconds),
            connection.OpenAsync,
            (abandonedOpen, failure) => ReleaseFailedOpen(connection, abandonedOpen, failure),
            cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <summary>Opens through the gate of one connection string: while its opens fail, one open probes and the others await its outcome, and an outage failure releases every open waiting at that moment (D-161).</summary>
    /// <param name="key">The connection string that names the gate.</param>
    /// <param name="deadline">Time the whole open may take, waits for a probing open included.</param>
    /// <param name="open">Starts the open with a token that is cancelled at the deadline or by the caller; called at most once.</param>
    /// <param name="releaseResources">Releases the open's resources after it failed or stopped waiting, given the open's task when it is still running, else null, and the failure.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/>, <paramref name="open"/> or <paramref name="releaseResources"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="deadline"/> is not above zero or exceeds <see cref="OracleSession.MaxDeadline"/>.</exception>
    /// <exception cref="TimeoutException">The open, or the wait for a probing open, did not complete within <paramref name="deadline"/>.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled while the open or the wait was still running.</exception>
    /// <exception cref="OracleOpenReleasedException">Another open of the same connection string failed while this one waited; that failure is the inner exception.</exception>
    /// <exception cref="Exception">The open failed; its failure propagates unchanged.</exception>
    internal static async Task OpenThroughGate(
        string key,
        TimeSpan deadline,
        Func<CancellationToken, Task> open,
        Func<Task?, Exception, ValueTask> releaseResources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        OracleSession.EnsureDeadline(deadline, nameof(deadline));
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(releaseResources);

        OpenGate gate = OpenGates.GetOrAdd(key, static _ => new OpenGate());
        long started = Stopwatch.GetTimestamp();
        TimeSpan remaining = deadline;

        while (true)
        {
            TaskCompletionSource<Exception> cohort;
            TaskCompletionSource<Exception?>? probe = null;
            Task<Exception?>? probing = null;
            lock (gate.Sync)
            {
                cohort = gate.Cohort;
                if (gate.Failing)
                {
                    if (gate.Probe is { } current)
                    {
                        probing = current.Task;
                    }
                    else
                    {
                        probe = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
                        gate.Probe = probe;
                    }
                }
            }

            if (probing is not null)
            {
                try
                {
                    await OracleSession.RunWithinDeadline(OpenOperation, remaining, _ => probing, _ => { }, cancellationToken).ConfigureAwait(false);
                    if (probing.Result is { } probeFailure)
                    {
                        throw new OracleOpenReleasedException(probeFailure);
                    }

                    remaining = deadline - Stopwatch.GetElapsedTime(started);
                    if (remaining <= TimeSpan.Zero)
                    {
                        throw new TimeoutException(string.Create(
                            CultureInfo.InvariantCulture,
                            $"The Oracle {OpenOperation} did not complete within {deadline.TotalSeconds} seconds."));
                    }
                }
                catch (Exception failure)
                {
                    MarkDuringOpen(failure);
                    await ReleaseAfterFailure(releaseResources, null, failure).ConfigureAwait(false);
                    throw;
                }

                continue;
            }

            Task? abandoned = null;
            try
            {
                await OracleSession.RunWithinDeadline(
                    OpenOperation,
                    remaining,
                    open,
                    running => abandoned = running,
                    cancellationToken,
                    cohort.Task).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                MarkDuringOpen(failure);
                await ReleaseAfterFailure(releaseResources, abandoned, failure).ConfigureAwait(false);
                Settle(gate, probe, failure, abandoned, cancellationToken);
                throw;
            }

            lock (gate.Sync)
            {
                gate.Failing = false;
                ClearProbe(gate, probe);
            }

            probe?.TrySetResult(null);
            return;
        }
    }

    /// <summary>Releases a failed gated open's resources; a release failure is attached to the open failure, not thrown.</summary>
    /// <param name="releaseResources">Releases the open's resources.</param>
    /// <param name="abandoned">The open still running, or null.</param>
    /// <param name="failure">The open failure that keeps propagating.</param>
    private static async ValueTask ReleaseAfterFailure(Func<Task?, Exception, ValueTask> releaseResources, Task? abandoned, Exception failure)
    {
        try
        {
            await releaseResources(abandoned, failure).ConfigureAwait(false);
        }
        catch (Exception releaseFailure)
        {
            OracleSession.AttachSecondaryFailure(failure, releaseFailure);
        }
    }

    /// <summary>Releases the connection of a failed open: after its open settles when still running, else at once.</summary>
    /// <param name="connection">The connection whose open failed.</param>
    /// <param name="abandonedOpen">The open still running, or null.</param>
    /// <param name="failure">The open failure that keeps propagating.</param>
    private static ValueTask ReleaseFailedOpen(OracleConnection connection, Task? abandonedOpen, Exception failure)
    {
        if (abandonedOpen is null)
        {
            return DisposeAfterFailure(connection, failure);
        }

        OracleSession.ReleaseWhenSettled(abandonedOpen, connection);
        return ValueTask.CompletedTask;
    }

    /// <summary>Publishes a failed open to its gate: an outage releases the waiting opens, a release passes its inner failure to the opens awaiting this probe, and any other failure lets them open again.</summary>
    /// <param name="gate">The gate of the open's connection string.</param>
    /// <param name="probe">The outcome of this open when it is the probing open, else null.</param>
    /// <param name="failure">The open failure, whose resources are already released.</param>
    /// <param name="abandoned">The open still running, or null.</param>
    /// <param name="cancellationToken">The caller's token; a failure after it is cancelled is never published as an outage.</param>
    private static void Settle(OpenGate gate, TaskCompletionSource<Exception?>? probe, Exception failure, Task? abandoned, CancellationToken cancellationToken)
    {
        if (failure is OracleOpenReleasedException)
        {
            lock (gate.Sync)
            {
                ClearProbe(gate, probe);
            }

            probe?.TrySetResult(failure.InnerException);
            if (abandoned is not null)
            {
                PublishWhenFaulted(gate, abandoned);
            }

            return;
        }

        if (IsOutage(failure) && !cancellationToken.IsCancellationRequested)
        {
            PublishOutage(gate, probe, failure);
            return;
        }

        lock (gate.Sync)
        {
            ClearProbe(gate, probe);
        }

        probe?.TrySetResult(null);
    }

    /// <summary>Marks the gate failing and releases every open waiting on it with the outage failure.</summary>
    /// <param name="gate">The gate of the failed open's connection string.</param>
    /// <param name="probe">The outcome of the failed open when it is the probing open, else null.</param>
    /// <param name="failure">The outage failure.</param>
    private static void PublishOutage(OpenGate gate, TaskCompletionSource<Exception?>? probe, Exception failure)
    {
        TaskCompletionSource<Exception> released;
        lock (gate.Sync)
        {
            gate.Failing = true;
            released = gate.Cohort;
            gate.Cohort = NewCohort();
            ClearProbe(gate, probe);
        }

        released.TrySetResult(failure);
        probe?.TrySetResult(failure);
    }

    /// <summary>Publishes the outage failure of an open released while still running, once it settles with one.</summary>
    /// <param name="gate">The gate of the open's connection string.</param>
    /// <param name="abandoned">The released open still running.</param>
    private static void PublishWhenFaulted(OpenGate gate, Task abandoned) =>
        _ = abandoned.ContinueWith(
            static (settled, state) =>
            {
                if (settled.Exception?.InnerException is { } failure && IsOutage(failure))
                {
                    MarkDuringOpen(failure);
                    PublishOutage((OpenGate)state!, null, failure);
                }
            },
            gate,
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);

    /// <summary>Empties the gate's probe slot when it still holds the given probe; the caller holds the gate's lock.</summary>
    /// <param name="gate">The gate.</param>
    /// <param name="probe">The probe outcome to clear, or null.</param>
    private static void ClearProbe(OpenGate gate, TaskCompletionSource<Exception?>? probe)
    {
        if (probe is not null && ReferenceEquals(gate.Probe, probe))
        {
            gate.Probe = null;
        }
    }

    /// <summary>Returns whether an open failure is a driver, socket or timeout failure.</summary>
    /// <param name="failure">The open failure.</param>
    /// <returns>True for an <see cref="OracleException"/>, <see cref="SocketException"/> or <see cref="TimeoutException"/>.</returns>
    private static bool IsOutage(Exception failure) => failure is OracleException or SocketException or TimeoutException;

    /// <summary>Marks a driver, socket, timeout or release failure as raised while opening the connection.</summary>
    /// <param name="failure">The open failure.</param>
    private static void MarkDuringOpen(Exception failure)
    {
        if (IsOutage(failure) || failure is OracleOpenReleasedException)
        {
            failure.Data[OracleErrorParser.DuringOpenKey] = true;
        }
    }

    /// <summary>Creates the completion that releases the opens waiting on a gate.</summary>
    /// <returns>A completion whose continuations run asynchronously.</returns>
    private static TaskCompletionSource<Exception> NewCohort() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Disposes a connection whose open or transaction start failed; a disposal failure is attached to the original failure, not thrown.</summary>
    /// <param name="connection">The connection to release.</param>
    /// <param name="failure">The open or transaction-start failure that keeps propagating.</param>
    private static async ValueTask DisposeAfterFailure(OracleConnection connection, Exception failure)
    {
        try
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception disposalFailure)
        {
            OracleSession.AttachSecondaryFailure(failure, disposalFailure);
        }
    }

    /// <summary>Open state shared by the opens of one connection string (D-161).</summary>
    private sealed class OpenGate
    {
        /// <summary>Guards the other members.</summary>
        public Lock Sync { get; } = new();

        /// <summary>Completed with the next published outage failure, releasing the opens that captured it.</summary>
        public TaskCompletionSource<Exception> Cohort { get; set; } = NewCohort();

        /// <summary>Whether the last published open outcome is an outage.</summary>
        public bool Failing { get; set; }

        /// <summary>Outcome of the open probing a failing gate: null on success or when it gave up, else its failure; null when no open is probing.</summary>
        public TaskCompletionSource<Exception?>? Probe { get; set; }
    }
}
