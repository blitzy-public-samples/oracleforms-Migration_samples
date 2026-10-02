using System.Globalization;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Domain.Rules;

/// <summary>DR-10: builds the invoice claim number from the entry parameters and the draft (T029).</summary>
public static class ClaimNumberRule
{
    /// <summary>Returns the claim number for the draft.</summary>
    /// <param name="header">Draft header supplying the patient number, clinic id and draft date.</param>
    /// <param name="parameters">Entry parameters supplying CLAIM_FLAG and CLAIM_NO.</param>
    /// <returns>CLAIM_NO when CLAIM_FLAG is 'R', or when CLAIM_NO is set and is neither '1' nor '2'; otherwise
    /// 'O-' + patient number + '-' + clinic id + '-' + the draft date as ddmmyy. An empty CLAIM_NO counts as null (D-110).</returns>
    public static string? Build(InvoiceHeaderDraft header, InvoiceEntryParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(parameters);

        var claimNo = string.IsNullOrEmpty(parameters.ClaimNo) ? null : parameters.ClaimNo;

        if (parameters.ClaimFlag == "R")
        {
            return claimNo;
        }

        if (claimNo is not null && claimNo != "1" && claimNo != "2")
        {
            return claimNo;
        }

        return "O-" + Text(header.PatientNo) + "-" + Text(header.ClinicId) + "-"
            + header.DraftDate.ToString("ddMMyy", CultureInfo.InvariantCulture);
    }

    private static string Text(string? value) => value ?? string.Empty;

    private static string Text(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
}
