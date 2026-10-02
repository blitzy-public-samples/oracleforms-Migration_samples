using System.Globalization;
using System.Runtime.ExceptionServices;
using Billing.Invoicing.Data.Ports;
using Oracle.ManagedDataAccess.Client;

namespace Billing.Invoicing.Data.Oracle;

/// <summary>Unit of work over one Oracle connection and transaction; attempts to roll back its local transaction when disposed uncommitted, and the outcome can remain uncertain. UNVERIFIED against Oracle.</summary>
public sealed class OracleSession : IOracleSession
{
    /// <summary>The <see cref="Exception.Data"/> key whose value is the <see cref="List{T}"/> of later failures attached to a first failure.</summary>
    internal const string SecondaryFailuresKey = "Billing.Invoicing.Data.SecondaryFailures";

    /// <summary>The longest deadline a call can be given.</summary>
    internal static readonly TimeSpan MaxDeadline = TimeSpan.FromMilliseconds(uint.MaxValue - 1d);

    private readonly TimeSpan _callDeadline;

    private bool _completed;
    private bool _disposed;
    private bool _lastCallSucceeded;
    private Exception? _failure;
    private Task? _abandonedCall;

    /// <summary>Wraps an open connection and the transaction begun on it; opens nothing itself.</summary>
    /// <param name="connection">The open connection the session owns.</param>
    /// <param name="transaction">The local transaction begun on <paramref name="connection"/>.</param>
    /// <param name="callDeadline">Deadline of each commit, rollback and savepoint call.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="callDeadline"/> is not above zero or exceeds <see cref="MaxDeadline"/>.</exception>
    internal OracleSession(OracleConnection connection, OracleTransaction transaction, TimeSpan callDeadline)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        EnsureDeadline(callDeadline, nameof(callDeadline));

        Connection = connection;
        Transaction = transaction;
        _callDeadline = callDeadline;
    }

    /// <summary>The connection that commands of this session run on.</summary>
    internal OracleConnection Connection { get; }

    /// <summary>The transaction that commands of this session enlist in.</summary>
    internal OracleTransaction Transaction { get; }

    /// <summary>Commits the transaction within the session's call deadline.</summary>
    /// <param name="cancellationToken">Cancels the commit request.</param>
    /// <exception cref="ObjectDisposedException">The session is disposed.</exception>
    /// <exception cref="InvalidOperationException">The session is already committed or rolled back, or an earlier call on it did not complete within its deadline.</exception>
    /// <exception cref="TimeoutException">The commit did not complete within the call deadline; the session stays uncompleted.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled while the commit was still running; the session stays uncompleted.</exception>
    /// <exception cref="Exception">The commit failed; its failure propagates unchanged and the session stays uncompleted.</exception>
    public async Task Commit(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        try
        {
            await RunWithinDeadline(
                "commit",
                _callDeadline,
                token => Transaction.CommitAsync(token),
                abandoned => _abandonedCall = abandoned,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }

        _completed = true;
    }

    /// <summary>Rolls back the whole transaction within the session's call deadline.</summary>
    /// <param name="cancellationToken">Cancels the rollback request.</param>
    /// <exception cref="ObjectDisposedException">The session is disposed.</exception>
    /// <exception cref="InvalidOperationException">The session is already committed or rolled back, or an earlier call on it did not complete within its deadline.</exception>
    /// <exception cref="TimeoutException">The rollback did not complete within the call deadline.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled while the rollback was still running.</exception>
    /// <exception cref="Exception">The rollback failed; its failure propagates unchanged.</exception>
    public async Task Rollback(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        try
        {
            await RunWithinDeadline(
                "rollback",
                _callDeadline,
                token => Transaction.RollbackAsync(token),
                abandoned => _abandonedCall = abandoned,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }

        _completed = true;
    }

    /// <summary>Rolls back to a named savepoint within the session's call deadline; the transaction stays open.</summary>
    /// <param name="savepointName">Name of a savepoint set earlier with <see cref="Save(string, CancellationToken)"/>.</param>
    /// <param name="cancellationToken">Cancels the savepoint rollback.</param>
    /// <exception cref="ArgumentException"><paramref name="savepointName"/> is null, empty or white space.</exception>
    /// <exception cref="ObjectDisposedException">The session is disposed.</exception>
    /// <exception cref="InvalidOperationException">The session is already committed or rolled back, or an earlier call on it did not complete within its deadline.</exception>
    /// <exception cref="TimeoutException">The savepoint rollback did not complete within the call deadline; the session stays uncompleted.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled while the savepoint rollback was still running; the session stays uncompleted.</exception>
    /// <exception cref="Exception">The savepoint rollback failed; its failure propagates unchanged and the session stays uncompleted.</exception>
    public async Task Rollback(string savepointName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savepointName);
        EnsureActive();
        try
        {
            await RunWithinDeadline(
                "savepoint rollback",
                _callDeadline,
                token => Transaction.RollbackAsync(savepointName, token),
                abandoned => _abandonedCall = abandoned,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
    }

    /// <summary>Sets a named savepoint in the transaction within the session's call deadline.</summary>
    /// <param name="savepointName">Name of the savepoint.</param>
    /// <param name="cancellationToken">Cancels the savepoint request.</param>
    /// <exception cref="ArgumentException"><paramref name="savepointName"/> is null, empty or white space.</exception>
    /// <exception cref="ObjectDisposedException">The session is disposed.</exception>
    /// <exception cref="InvalidOperationException">The session is already committed or rolled back, or an earlier call on it did not complete within its deadline.</exception>
    /// <exception cref="TimeoutException">The savepoint did not complete within the call deadline; the session stays uncompleted.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled while the savepoint was still running; the session stays uncompleted.</exception>
    /// <exception cref="Exception">The savepoint failed; its failure propagates unchanged and the session stays uncompleted.</exception>
    public async Task Save(string savepointName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savepointName);
        EnsureActive();
        try
        {
            await RunWithinDeadline(
                "savepoint",
                _callDeadline,
                token => Transaction.SaveAsync(savepointName, token),
                abandoned => _abandonedCall = abandoned,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
    }

    /// <summary>Releases the session and attempts rollback of unfinished work, preserving the first failure; the rollback outcome can remain uncertain (D-89).</summary>
    /// <exception cref="Exception">A rollback or release failure of an uncompleted session whose last call succeeded and that recorded no earlier failure.</exception>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        List<Exception>? cleanupFailures = await ReleaseWithinDeadline(
            _callDeadline,
            _abandonedCall,
            _completed ? null : token => Transaction.RollbackAsync(token),
            Transaction,
            Connection).ConfigureAwait(false);

        if (cleanupFailures is null)
        {
            return;
        }

        if (_failure is not null)
        {
            foreach (var cleanupFailure in cleanupFailures)
            {
                AttachSecondaryFailure(_failure, cleanupFailure);
            }

            return;
        }

        if (!_completed && _lastCallSucceeded)
        {
            var first = cleanupFailures[0];
            foreach (var cleanupFailure in cleanupFailures.Skip(1))
            {
                AttachSecondaryFailure(first, cleanupFailure);
            }

            ExceptionDispatchInfo.Throw(first);
        }
    }

    /// <summary>Checks that the session is active and marks a gateway call on it as started.</summary>
    /// <exception cref="ObjectDisposedException">The session is disposed.</exception>
    /// <exception cref="InvalidOperationException">The session is already committed or rolled back, or an earlier call on it did not complete within its deadline.</exception>
    internal void BeginCall()
    {
        EnsureActive();
        _lastCallSucceeded = false;
    }

    /// <summary>Marks the gateway call started last as completed, with its outputs read.</summary>
    internal void EndCall() => _lastCallSucceeded = true;

    /// <summary>Records a failure raised in the session; the first is kept and later ones are attached to it.</summary>
    /// <param name="failure">The failure raised by a command or transaction call of this session.</param>
    /// <exception cref="ArgumentNullException"><paramref name="failure"/> is null.</exception>
    internal void RecordFailure(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        if (_failure is null)
        {
            _failure = failure;
            return;
        }

        AttachSecondaryFailure(_failure, failure);
    }

    /// <summary>Adds a later failure to the list held in <paramref name="primary"/>'s <see cref="Exception.Data"/> under <see cref="SecondaryFailuresKey"/>.</summary>
    /// <param name="primary">The first failure, which keeps propagating.</param>
    /// <param name="secondary">The later failure; ignored when it is <paramref name="primary"/> itself.</param>
    /// <exception cref="ArgumentNullException"><paramref name="primary"/> or <paramref name="secondary"/> is null.</exception>
    internal static void AttachSecondaryFailure(Exception primary, Exception secondary)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(secondary);

        if (ReferenceEquals(primary, secondary))
        {
            return;
        }

        if (primary.Data[SecondaryFailuresKey] is List<Exception> list)
        {
            list.Add(secondary);
        }
        else
        {
            primary.Data[SecondaryFailuresKey] = new List<Exception> { secondary };
        }
    }

    /// <summary>Runs an Oracle call under a deadline, distinguishing timeout from caller cancellation (D-89).</summary>
    /// <param name="operation">Name of the call, used in the timeout message.</param>
    /// <param name="deadline">Time the call may take.</param>
    /// <param name="call">Starts the call with a token that is cancelled when the deadline expires or the caller cancels.</param>
    /// <param name="onAbandoned">Receives the call's task when it is still running after its token was cancelled or <paramref name="released"/> completed.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <param name="released">Completes with another call's failure to stop waiting for this call, leaving its token uncancelled; null waits only for the call, the deadline and the caller.</param>
    /// <exception cref="ArgumentException"><paramref name="operation"/> is null, empty or white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="call"/> or <paramref name="onAbandoned"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="deadline"/> is not above zero or exceeds <see cref="MaxDeadline"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="call"/> returned no task.</exception>
    /// <exception cref="TimeoutException">The call did not complete within <paramref name="deadline"/>.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled while the call was still running.</exception>
    /// <exception cref="OracleOpenReleasedException"><paramref name="released"/> completed while the call was still running; its inner exception is the failure it completed with.</exception>
    internal static async Task RunWithinDeadline(
        string operation,
        TimeSpan deadline,
        Func<CancellationToken, Task> call,
        Action<Task> onAbandoned,
        CancellationToken cancellationToken,
        Task<Exception>? released = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        EnsureDeadline(deadline, nameof(deadline));
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(onAbandoned);

        using var callCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task pending = call(callCancellation.Token)
            ?? throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The Oracle {operation} returned no task."));

        try
        {
            if (released is null)
            {
                await pending.WaitAsync(deadline, cancellationToken).ConfigureAwait(false);
                return;
            }

            await Task.WhenAny(pending, released).WaitAsync(deadline, cancellationToken).ConfigureAwait(false);
            if (pending.IsCompleted)
            {
                await pending.ConfigureAwait(false);
                return;
            }
        }
        catch (Exception waitFailure)
        {
            AggregateException? cancelFailure = pending.IsCompleted ? null : CancelCall(callCancellation);
            if (pending.IsCompletedSuccessfully)
            {
                return;
            }

            Exception outcome;
            if (pending.IsCompleted)
            {
                Exception callFailure = FailureOf(pending);
                outcome = callCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested
                    ? Expired(operation, deadline, callFailure)
                    : callFailure;
            }
            else
            {
                onAbandoned(pending);
                outcome = cancellationToken.IsCancellationRequested
                    ? waitFailure as OperationCanceledException ?? new OperationCanceledException(cancellationToken)
                    : Expired(operation, deadline, waitFailure);
            }

            if (cancelFailure is not null)
            {
                AttachSecondaryFailure(outcome, cancelFailure);
            }

            ExceptionDispatchInfo.Throw(outcome);
        }

        onAbandoned(pending);
        throw new OracleOpenReleasedException(released.IsCompletedSuccessfully ? released.Result : FailureOf(released));
    }

    /// <summary>Attempts rollback and releases the transaction and connection within deadlines, deferring unfinished operations (D-89).</summary>
    /// <param name="deadline">Time each step may take.</param>
    /// <param name="stillRunning">A call on the connection that did not complete within its deadline, or null.</param>
    /// <param name="rollback">Rolls the transaction back with the given token, or null when the transaction is completed.</param>
    /// <param name="transaction">The transaction to release.</param>
    /// <param name="connection">The connection to release after the transaction.</param>
    /// <returns>The failures of the steps run before returning, in order, or null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="transaction"/> or <paramref name="connection"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="deadline"/> is not above zero or exceeds <see cref="MaxDeadline"/>.</exception>
    internal static async Task<List<Exception>?> ReleaseWithinDeadline(
        TimeSpan deadline,
        Task? stillRunning,
        Func<CancellationToken, Task>? rollback,
        IAsyncDisposable transaction,
        IAsyncDisposable connection)
    {
        EnsureDeadline(deadline, nameof(deadline));
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(connection);

        List<Exception>? failures = null;
        Task? unsettled = stillRunning;

        if (unsettled is null && rollback is not null)
        {
            try
            {
                await RunWithinDeadline("rollback", deadline, rollback, running => unsettled = running, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
                ReleaseWhenSettled(unsettled ?? Task.CompletedTask, transaction, connection);
                return failures;
            }
        }

        if (unsettled is not null)
        {
            ReleaseWhenSettled(unsettled, transaction, connection);
            return failures;
        }

        try
        {
            await RunWithinDeadline("transaction release", deadline, _ => transaction.DisposeAsync().AsTask(), running => unsettled = running, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            (failures ??= []).Add(failure);
        }

        if (unsettled is not null)
        {
            ReleaseWhenSettled(unsettled, connection);
            return failures;
        }

        try
        {
            await RunWithinDeadline("connection release", deadline, _ => connection.DisposeAsync().AsTask(), running => ReleaseWhenSettled(running), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            (failures ??= []).Add(failure);
        }

        return failures;
    }

    /// <summary>Disposes each resource in order, in the background, once a call still running on them has settled; a disposal failure does not stop the next.</summary>
    /// <param name="pending">The call still running on the resources.</param>
    /// <param name="resources">The resources to dispose, in order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pending"/>, <paramref name="resources"/> or one of its elements is null.</exception>
    internal static void ReleaseWhenSettled(Task pending, params IAsyncDisposable[] resources)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(resources);
        foreach (var resource in resources)
        {
            ArgumentNullException.ThrowIfNull(resource, nameof(resources));
        }

        IAsyncDisposable[] ordered = [.. resources];
        _ = pending.ContinueWith(
            static (settled, state) => DisposeInOrder(settled, (IAsyncDisposable[])state!),
            ordered,
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default).Unwrap();
    }

    /// <summary>Throws when the session is disposed, has a call that did not complete within its deadline, or is committed or rolled back.</summary>
    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_abandonedCall is not null)
        {
            throw new InvalidOperationException("An earlier call on this Oracle session did not complete within its deadline.");
        }

        if (_completed)
        {
            throw new InvalidOperationException("The Oracle session is already committed or rolled back.");
        }
    }

    /// <summary>Throws when a deadline is not above zero or exceeds <see cref="MaxDeadline"/>.</summary>
    /// <param name="deadline">The deadline to check.</param>
    /// <param name="paramName">Name of the caller's deadline parameter, reported in the exception.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="deadline"/> is not above zero or exceeds <see cref="MaxDeadline"/>.</exception>
    internal static void EnsureDeadline(TimeSpan deadline, string paramName)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(deadline, TimeSpan.Zero, paramName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(deadline, MaxDeadline, paramName);
    }

    /// <summary>Cancels a call's token, running the callbacks registered on it.</summary>
    /// <param name="callCancellation">The source of the call's token.</param>
    /// <returns>The failure raised by those callbacks, or null.</returns>
    private static AggregateException? CancelCall(CancellationTokenSource callCancellation)
    {
        try
        {
            callCancellation.Cancel();
            return null;
        }
        catch (AggregateException failure)
        {
            return failure;
        }
    }

    /// <summary>Returns the exception a faulted or cancelled task ends with.</summary>
    /// <param name="settled">A completed task that did not succeed.</param>
    /// <returns>The task's exception.</returns>
    /// <exception cref="ArgumentException"><paramref name="settled"/> is not completed or did not fail.</exception>
    private static Exception FailureOf(Task settled)
    {
        if (!settled.IsCompleted)
        {
            throw new ArgumentException("The task has not completed.", nameof(settled));
        }

        try
        {
            settled.GetAwaiter().GetResult();
        }
        catch (Exception failure)
        {
            return failure;
        }

        throw new ArgumentException("The task has not failed.", nameof(settled));
    }

    /// <summary>Builds the timeout of a call whose deadline expired.</summary>
    /// <param name="operation">Name of the call.</param>
    /// <param name="deadline">The expired deadline.</param>
    /// <param name="inner">The failure observed when the deadline expired.</param>
    /// <returns>The timeout naming the call and its deadline.</returns>
    private static TimeoutException Expired(string operation, TimeSpan deadline, Exception inner) =>
        new(string.Create(CultureInfo.InvariantCulture, $"The Oracle {operation} did not complete within {deadline.TotalSeconds} seconds."), inner);

    /// <summary>Observes a settled call's failure, then disposes each resource in order.</summary>
    /// <param name="settled">The settled call.</param>
    /// <param name="resources">The resources to dispose, in order.</param>
    private static async Task DisposeInOrder(Task settled, IAsyncDisposable[] resources)
    {
        _ = settled.Exception;

        foreach (var resource in resources)
        {
            try
            {
                await resource.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A disposal failure is dropped.
            }
        }
    }
}

/// <summary>Ends a connection open that stopped waiting because another open of the same connection string failed; that failure is the inner exception (D-161).</summary>
internal sealed class OracleOpenReleasedException : InvalidOperationException
{
    /// <summary>Wraps the failure of the other open.</summary>
    /// <param name="peerFailure">The failure that released this open.</param>
    internal OracleOpenReleasedException(Exception peerFailure)
        : base("The Oracle connection open stopped waiting because another open of the same connection string failed.", peerFailure)
    {
    }
}
