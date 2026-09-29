namespace Billing.Invoicing.Domain.Rules;

/// <summary>DR-21: decides whether a create clears the patient's reception-transfer fields (T011).</summary>
public static class ReceptionTransferRule
{
    /// <summary>Returns whether <c>PATIENT.NEW_INV_DOCID</c>, <c>NEW_INV_CLINICID</c>, <c>NEW_INV_CATID</c> and <c>NEW_INV_SERVICEID</c> are cleared after the invoice is created.</summary>
    /// <param name="isReplay">True when the create returned an invoice already created for the same request id.</param>
    /// <param name="hasNewInvDocId">True when the patient's <c>NEW_INV_DOCID</c> is set.</param>
    /// <returns>True for a new create whose patient has <c>NEW_INV_DOCID</c> set; otherwise false.</returns>
    public static bool ShouldClear(bool isReplay, bool hasNewInvDocId) => !isReplay && hasNewInvDocId;
}
