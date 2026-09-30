using System.Data;
using System.Net.Sockets;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Data.Ports;
using Oracle.ManagedDataAccess.Client;

namespace Billing.Invoicing.Data.Oracle;

/// <summary>Opens Oracle connections and transactional sessions from <see cref="InvoicingDataOptions"/>. UNVERIFIED against Oracle.</summary>
public sealed class OracleSessionFactory : IOracleSessionFactory
{
    private readonly InvoicingDataOptions _options;

    /// <summary>Stores the data-layer settings; opens nothing.</summary>
    /// <param name="options">Connection string and command settings.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    public OracleSessionFactory(InvoicingDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    /// <summary>Opens a connection and begins a read-committed transaction on it.</summary>
    /// <param name="cancellationToken">Cancels the connection open.</param>
    /// <returns>The open session; disposing it uncommitted rolls the transaction back.</returns>
    public async Task<IOracleSession> Open(CancellationToken cancellationToken = default)
    {
        var connection = await OpenConnection(_options, cancellationToken).ConfigureAwait(false);

        OracleTransaction transaction;
        try
        {
            transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new OracleSession(connection, transaction);
    }

    /// <summary>Opens a connection without a transaction; marks open failures in <see cref="Exception.Data"/> under <see cref="OracleErrorParser.DuringOpenKey"/> and rethrows.</summary>
    /// <param name="options">Settings holding the connection string.</param>
    /// <param name="cancellationToken">Cancels the connection open.</param>
    /// <returns>An open connection the caller disposes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    internal static async Task<OracleConnection> OpenConnection(InvoicingDataOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var connection = new OracleConnection(options.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch (Exception exception)
        {
            if (exception is OracleException or SocketException or TimeoutException)
            {
                exception.Data[OracleErrorParser.DuringOpenKey] = true;
            }

            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
