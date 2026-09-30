using System.Data;
using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Ports;
using Dapper;

namespace Billing.Invoicing.Data.Commands;

/// <summary>Clears a patient's pending reception-transfer fields after an invoice is created. UNVERIFIED against Oracle.</summary>
public sealed class PatientTransferCommand : IPatientTransferCommand
{
    /// <summary>Sets the four NEW_INV_* columns of the patient to null when NEW_INV_DOCID is set.</summary>
    public const string ClearReceptionTransferSql =
        "UPDATE PATIENT SET NEW_INV_DOCID = NULL, NEW_INV_CLINICID = NULL, NEW_INV_CATID = NULL, NEW_INV_SERVICEID = NULL WHERE PATIENTNO = :patientNo AND NEW_INV_DOCID IS NOT NULL";

    private readonly InvoicingDataOptions _options;

    /// <summary>Stores the data-layer settings; opens nothing.</summary>
    /// <param name="options">Command timeout applied to the update.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="InvoicingDataOptions.CommandTimeoutSeconds"/> is below 1.</exception>
    public PatientTransferCommand(InvoicingDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.EnsureCommandTimeout(nameof(options));

        _options = options;
    }

    /// <summary>Clears the patient's NEW_INV_* reception-transfer fields inside the session's transaction.</summary>
    /// <param name="session">Open session whose connection and transaction the update runs on.</param>
    /// <param name="patientNo">Patient number of the invoice.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>Rows updated.</returns>
    public Task<int> ClearReceptionTransfer(IOracleSession session, string patientNo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(patientNo);

        if (session is not OracleSession oracleSession)
        {
            throw new ArgumentException("Session must be an OracleSession.", nameof(session));
        }

        return oracleSession.Connection.ExecuteAsync(
            ClearReceptionTransferCommand(patientNo, oracleSession.Transaction, cancellationToken));
    }

    /// <summary>DR-21 update command for the patient with the configured timeout.</summary>
    /// <param name="patientNo">Patient number bound as <c>:patientNo</c>.</param>
    /// <param name="transaction">Transaction the update runs in.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>The command definition.</returns>
    internal CommandDefinition ClearReceptionTransferCommand(string patientNo, IDbTransaction? transaction, CancellationToken cancellationToken) =>
        new(
            ClearReceptionTransferSql,
            new { patientNo },
            transaction: transaction,
            commandTimeout: _options.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);
}
