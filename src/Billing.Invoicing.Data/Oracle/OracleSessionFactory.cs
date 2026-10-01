using System.Data;
using System.Net.Sockets;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Data.Ports;
using Oracle.ManagedDataAccess.Client;

namespace Billing.Invoicing.Data.Oracle;

/// <summary>Opens Oracle connections and transactional sessions from <see cref="InvoicingDataOptions"/>. UNVERIFIED against Oracle.</summary>
public sealed class OracleSessionFactory : IOracleSessionFactory
{
    private const string BlankConnectionStringMessage = "The Oracle connection string is not configured.";

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

    /// <summary>Opens a non-transactional connection within the configured deadline and tags connection failures (D-89).</summary>
    /// <param name="options">Settings holding the connection string and the open deadline.</param>
    /// <param name="cancellationToken">Cancels the connection open.</param>
    /// <returns>An open connection the caller disposes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="InvoicingDataOptions.CommandTimeoutSeconds"/> is below 1 or above <see cref="InvoicingDataOptions.MaxCommandTimeoutSeconds"/>.</exception>
    /// <exception cref="InvalidOperationException">The connection string is blank.</exception>
    /// <exception cref="ArgumentException">The connection string is malformed.</exception>
    /// <exception cref="TimeoutException">The open did not complete within <see cref="InvoicingDataOptions.CommandTimeoutSeconds"/>.</exception>
    internal static async Task<OracleConnection> OpenConnection(InvoicingDataOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.EnsureCommandTimeout(nameof(options));

        if (IsBlank(options.ConnectionString))
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

        Task? abandonedOpen = null;
        try
        {
            await OracleSession.RunWithinDeadline(
                "connection open",
                TimeSpan.FromSeconds(options.CommandTimeoutSeconds),
                connection.OpenAsync,
                opening => abandonedOpen = opening,
                cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch (Exception exception)
        {
            if (exception is OracleException or SocketException or TimeoutException)
            {
                exception.Data[OracleErrorParser.DuringOpenKey] = true;
            }

            if (abandonedOpen is not null)
            {
                OracleSession.ReleaseWhenSettled(abandonedOpen, connection);
            }
            else
            {
                await DisposeAfterFailure(connection, exception).ConfigureAwait(false);
            }

            throw;
        }
    }

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

    /// <summary>Returns whether a connection string holds no attribute: null, empty, or only white space and semicolons.</summary>
    /// <param name="connectionString">The configured connection string.</param>
    /// <returns>True when the connection string is blank.</returns>
    private static bool IsBlank(string? connectionString) =>
        connectionString is null || connectionString.All(character => char.IsWhiteSpace(character) || character == ';');
}
