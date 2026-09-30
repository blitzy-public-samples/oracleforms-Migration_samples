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
            new CommandDefinition(
                ClearReceptionTransferSql,
                new { patientNo },
                transaction: oracleSession.Transaction,
                cancellationToken: cancellationToken));
    }
}
