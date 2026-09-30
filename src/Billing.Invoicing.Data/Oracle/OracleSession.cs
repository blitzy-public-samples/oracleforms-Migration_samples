using Billing.Invoicing.Data.Ports;
using Oracle.ManagedDataAccess.Client;

namespace Billing.Invoicing.Data.Oracle;

/// <summary>Unit of work over one Oracle connection and transaction; rolls back when disposed uncommitted. UNVERIFIED against Oracle.</summary>
public sealed class OracleSession : IOracleSession
{
    private bool _completed;
    private bool _disposed;

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
        await Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    /// <summary>Rolls back the whole transaction; a failure propagates unchanged.</summary>
    /// <param name="cancellationToken">Cancels the rollback request.</param>
    public async Task Rollback(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        await Transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    /// <summary>Rolls back to a named savepoint; the transaction stays open.</summary>
    /// <param name="savepointName">Name of a savepoint set earlier with <see cref="Save"/>.</param>
    public void Rollback(string savepointName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savepointName);
        EnsureActive();
        Transaction.Rollback(savepointName);
    }

    /// <summary>Sets a named savepoint in the transaction.</summary>
    /// <param name="savepointName">Name of the savepoint.</param>
    public void Save(string savepointName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savepointName);
        EnsureActive();
        Transaction.Save(savepointName);
    }

    /// <summary>Rolls back an uncompleted transaction, then releases the transaction and the connection; repeat calls do nothing.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (!_completed)
            {
                try
                {
                    await Transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // A failed rollback is ignored; disposal continues.
                }
            }

            try
            {
                await Transaction.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A failed transaction release is ignored; the connection is still released.
            }
        }
        finally
        {
            await Connection.DisposeAsync().ConfigureAwait(false);
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
