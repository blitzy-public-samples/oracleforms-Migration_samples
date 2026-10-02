using System.Collections.ObjectModel;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Domain.Rules;

/// <summary>DR-01, DR-22: header record validation and the payment-type default of <c>T_INV</c> WHEN-VALIDATE-RECORD (T014, T038, T040).</summary>
public static class HeaderRecordRules
{
    private const string PaymentTypeEmpty = "Payment type is empty";
    private const string PatientNotInCompany = "this patient not belong to any company !!";
    private const string PatientRequired = "Select Patient No is required ";
    private const string DoctorRequired = "Doctor No is required ";

    private const string CompCodeField = "COMP_CODE";
    private const string PatientNoField = "PATIENTNO";
    private const string DoctorField = "DOCIDX";
    private const string SubPayTypeField = "SUB_PAYTYPE";

    private const string RecordRuleId = "DR-01";
    private const string PaymentTypeRuleId = "DR-22";

    private const int CreditPayType = 2;
    private const string CashCompanyCode = "0";
    private const int DefaultSubPayType = 1;

    /// <summary>Validates the header record in T014 order and stops at the first blocking message.</summary>
    /// <param name="header">Draft header supplying PAYTYPE, COMP_CODE, PATIENTNO and DOCIDX.</param>
    /// <returns>One blocking message on COMP_CODE (credit with company '0'), PATIENTNO (null, empty or white space) or DOCIDX (null), whichever fails first; otherwise <see cref="RuleResult.Empty"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="header"/> is null.</exception>
    public static RuleResult ValidateRecord(InvoiceHeaderDraft header)
    {
        ArgumentNullException.ThrowIfNull(header);

        if (header.PayType == CreditPayType && string.Equals(header.CompCode, CashCompanyCode, StringComparison.Ordinal))
        {
            return Block(CompCodeField, PatientNotInCompany);
        }

        if (string.IsNullOrWhiteSpace(header.PatientNo))
        {
            return Block(PatientNoField, PatientRequired);
        }

        if (header.DocId is null)
        {
            return Block(DoctorField, DoctorRequired);
        }

        return RuleResult.Empty;
    }

    /// <summary>Defaults payment method 1 when an amount is collected and neither payment method is set.</summary>
    /// <param name="header">Draft header supplying AMOUNT_1, AMOUNT_2, SUB_PAYTYPE and SUB_PAYTYPE2.</param>
    /// <returns>A SUB_PAYTYPE warning with SUB_PAYTYPE adjusted to 1 when the total collected is positive and both payment methods are null; otherwise <see cref="RuleResult.Empty"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="header"/> is null.</exception>
    public static RuleResult ApplyPaymentTypeDefault(InvoiceHeaderDraft header)
    {
        ArgumentNullException.ThrowIfNull(header);

        var collected = PaymentAllocationRules.TotalCollected(header.Amount1, header.Amount2);

        if (collected > 0m && header.SubPayType is null && header.SubPayType2 is null)
        {
            return new RuleResult
            {
                Messages = new[]
                {
                    new ValidationMessage(SubPayTypeField, PaymentTypeEmpty, ValidationMessage.Warning, PaymentTypeRuleId),
                },
                Adjusted = new ReadOnlyDictionary<string, object?>(
                    new Dictionary<string, object?>(StringComparer.Ordinal) { [SubPayTypeField] = DefaultSubPayType }),
            };
        }

        return RuleResult.Empty;
    }

    private static RuleResult Block(string field, string text) => new()
    {
        Messages = new[] { new ValidationMessage(field, text, ValidationMessage.Blocking, RecordRuleId) },
    };
}
