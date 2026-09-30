using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Domain.Rules;

/// <summary>DR-20: builds the header defaults of a new invoice draft (T015, T022, T003).</summary>
public static class InvoiceDefaultsRule
{
    private const string HomeCareFlag = "Y";
    private const int HomeCareInvoiceType = 7;
    private const int DefaultInvoiceType = 0;
    private const int DefaultSubPayType = 1;
    private const int CashOrCreditCash = 1;
    private const string CashCompanyCode = "0";
    private const string NoClaimParameter = "0";
    private const string NewConsultationClaimParameter = "1";
    private const string IndependentServiceClaimParameter = "2";

    /// <summary>Returns a new draft header carrying the INVTYPEID item initial value and the WHEN-CREATE-RECORD defaults and preloads.</summary>
    /// <param name="parameters">Entry parameters supplying IS_HOME_CARE, CLAIM_FLAG, CLAIM_NO, NEW_DOC, CASH_OR_CREDIT and VISIT_UNIQUE.</param>
    /// <param name="databaseTime">Database <c>SYSDATE</c> read when the draft is created; becomes the draft date and invoice date.</param>
    /// <param name="claimPreload">Header of the claim's first invoice, or null when the claim has no invoice.</param>
    /// <param name="visitDoctorId">Doctor of the visit in <c>PAT_VISIT_M</c> for VISIT_UNIQUE, or null when none was found.</param>
    /// <returns>The new draft header; fields the defaults do not set are left unset.</returns>
    public static InvoiceHeaderDraft Apply(
        InvoiceEntryParameters parameters,
        DateTime databaseTime,
        InvoiceHeaderDraft? claimPreload,
        int? visitDoctorId)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var draft = new InvoiceHeaderDraft
        {
            DraftDate = databaseTime,
            InvDate = databaseTime,
            InvTypeId = parameters.IsHomeCare == HomeCareFlag ? HomeCareInvoiceType : DefaultInvoiceType,
            SubPayType = DefaultSubPayType,
            ClaimFlag = parameters.ClaimFlag,
        };

        if (parameters.ClaimNo == NewConsultationClaimParameter)
        {
            draft = draft with { DocId = parameters.NewDoc };
        }

        if (claimPreload is not null && IsClaimNumberParameter(parameters.ClaimNo))
        {
            draft = ApplyClaimPreload(draft, parameters, claimPreload);
        }

        if (!string.IsNullOrEmpty(parameters.VisitUnique) && visitDoctorId is not null)
        {
            draft = draft with { DocId = visitDoctorId };
        }

        return draft;
    }

    private static bool IsClaimNumberParameter(string? claimNo) =>
        !string.IsNullOrEmpty(claimNo)
        && claimNo != NoClaimParameter
        && claimNo != NewConsultationClaimParameter
        && claimNo != IndependentServiceClaimParameter;

    private static InvoiceHeaderDraft ApplyClaimPreload(
        InvoiceHeaderDraft draft,
        InvoiceEntryParameters parameters,
        InvoiceHeaderDraft claimPreload)
    {
        // Any CASH_OR_CREDIT other than 1, null included, copies the preload's company, sub-company, class and card fields.
        var cashRequested = parameters.CashOrCredit == CashOrCreditCash;
        var compCode = cashRequested ? CashCompanyFor(claimPreload) : claimPreload.CompCode;

        return draft with
        {
            ClaimNo = claimPreload.ClaimNo,
            PatientNo = claimPreload.PatientNo,
            ClinicId = claimPreload.ClinicId,
            CompCode = compCode,
            SubCompCode = cashRequested ? null : claimPreload.SubCompCode,
            ClassCode = cashRequested ? null : claimPreload.ClassCode,
            PayType = PayTypeSelectionRule.Decide(compCode, null, parameters, claimPreload),
            InsNumber = cashRequested ? null : claimPreload.InsNumber,
            CardEnd = cashRequested ? null : claimPreload.CardEnd,
            PatPolicyNo = cashRequested ? null : claimPreload.PatPolicyNo,
        };
    }

    private static string? CashCompanyFor(InvoiceHeaderDraft claimPreload) =>
        claimPreload.CompCode == CashCompanyCode || !string.IsNullOrEmpty(claimPreload.SubCompCode)
            ? CashCompanyCode
            : claimPreload.CompCode;
}
