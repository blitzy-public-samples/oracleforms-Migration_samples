using System.Runtime.ExceptionServices;
using Billing.Invoicing.Data.Ports;
using Oracle.ManagedDataAccess.Client;

namespace Billing.Invoicing.Data.Oracle;

/// <summary>Unit of work over one Oracle connection and transaction; rolls back when disposed uncommitted. UNVERIFIED against Oracle.</summary>
public sealed class OracleSession : IOracleSession
{
    /// <summary>The <see cref="Exception.Data"/> key whose value is the <see cref="List{T}"/> of later failures attached to a first failure.</summary>
    internal const string SecondaryFailuresKey = "Billing.Invoicing.Data.SecondaryFailures";

    private bool _completed;
    private bool _disposed;
    private bool _lastCallSucceeded;
    private Exception? _failure;

    /// <summary>Wraps an open connection and the transaction begun on it; opens nothing itself.</summary>
    /// <param name="connection">The open connection the session owns.</param>
    /// <param name="transaction">The local transaction begun on <paramref name="connection"/>.</param>
    internal OracleSession(OracleConnection connection, OracleTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        Connection = connection;
        Transaction = transaction;
    }

    /// <summary>The connection that commands of this session run on.</summary>
    internal OracleConnection Connection { get; }

    /// <summary>The transaction that commands of this session enlist in.</summary>
    internal OracleTransaction Transaction { get; }

    /// <summary>Commits the transaction; a failure propagates unchanged and leaves the session uncompleted.</summary>
    /// <param name="cancellationToken">Cancels the commit request.</param>
    public async Task Commit(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        try
        {
            await Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }

        _completed = true;
    }

    /// <summary>Rolls back the whole transaction; a failure propagates unchanged.</summary>
    /// <param name="cancellationToken">Cancels the rollback request.</param>
    public async Task Rollback(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        try
        {
            await Transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }

        _completed = true;
    }

    /// <summary>Rolls back to a named savepoint; the transaction stays open.</summary>
    /// <param name="savepointName">Name of a savepoint set earlier with <see cref="Save"/>.</param>
    public void Rollback(string savepointName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savepointName);
        EnsureActive();
        try
        {
            Transaction.Rollback(savepointName);
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
    }

    /// <summary>Sets a named savepoint in the transaction.</summary>
    /// <param name="savepointName">Name of the savepoint.</param>
    public void Save(string savepointName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savepointName);
        EnsureActive();
        try
        {
            Transaction.Save(savepointName);
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
    }

    /// <summary>Rolls back an uncompleted transaction, then releases the transaction and the connection; cleanup failures attach to the first recorded failure, are thrown when none is recorded and an uncompleted session's last call succeeded, and are otherwise dropped; repeat calls do nothing.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        List<Exception>? cleanupFailures = null;

        if (!_completed)
        {
            try
            {
                await Transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (cleanupFailures ??= []).Add(exception);
            }
        }

        try
        {
            await Transaction.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (cleanupFailures ??= []).Add(exception);
        }

        try
        {
            await Connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (cleanupFailures ??= []).Add(exception);
        }

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
    /// <exception cref="InvalidOperationException">The session is already committed or rolled back.</exception>
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

    /// <summary>Throws when the session is disposed, committed or rolled back.</summary>
    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_completed)
        {
            throw new InvalidOperationException("The Oracle session is already committed or rolled back.");
        }
    }
}
