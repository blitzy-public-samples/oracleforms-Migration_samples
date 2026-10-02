using System.Collections.ObjectModel;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Domain.Rules;

/// <summary>DR-12 to DR-16: operator-input checks on one invoice line (T068, T077, T078, T074, T066, T052).</summary>
public static class LineEntryRules
{
    private const string QuantityLimitText = "Due to Quality system not allow more than 1 at qty for this service";

    private const string QuantityMinimumText = "Qty should be >=1";

    private const string ValueDiscountText = "You cant use value disocunt for credit invoices";

    private const string NeedApprovalSuffix = "Need Approval";

    private const string SelectServiceText = "You Must Select Value";

    private const string NotRequestedText = "This service not requested by doctor at this claim";

    private const string QtyField = "QTY";

    private const string DiscountTypeField = "LDISCT";

    private const string ApprovalReferenceField = "APPROV_REF_NO";

    private const string ServiceField = "SERVICEID";

    private const string QuantityRule = "DR-12";

    private const string DiscountTypeRule = "DR-13";

    private const string ApprovalRule = "DR-14";

    private const string ServiceRule = "DR-15";

    private const string NotRequestedRule = "DR-16";

    private const string ValueDiscount = "V";

    private const string RateDiscount = "R";

    private const int ShowQtyLimited = 1;

    private const int CreditPayType = 2;

    private const int ApprovalCheckEnforced = 1;

    private const int ApprovalCheckBypassed = 2;

    /// <summary>Checks the line quantity against the service's SHOW_QTY limit and the minimum of 1 (T068).</summary>
    /// <param name="service">Server-read profile of the line's service; null skips the SHOW_QTY check.</param>
    /// <param name="qty">Line quantity (QTY).</param>
    /// <returns>A blocking QTY message for the first failed check; otherwise <see cref="RuleResult.Empty"/>.</returns>
    public static RuleResult ValidateQuantity(ServiceProfile? service, decimal? qty)
    {
        if (service?.ShowQty == ShowQtyLimited && qty > 1)
        {
            return Block(QtyField, QuantityLimitText, QuantityRule);
        }

        if ((qty ?? 0) <= 0)
        {
            return Block(QtyField, QuantityMinimumText, QuantityRule);
        }

        return RuleResult.Empty;
    }

    /// <summary>Refuses a value discount on an invoice with a class code and resets the type to 'R' (T077, T078).</summary>
    /// <param name="classCode">Header class code (CLASS_CODE); null or empty for an invoice without a class.</param>
    /// <param name="discountType">Line discount type (LDISCT): 'N', 'R' or 'V'.</param>
    /// <returns>A blocking LDISCT message with LDISCT adjusted to 'R' when a class code is set and the type is 'V'; otherwise <see cref="RuleResult.Empty"/>.</returns>
    public static RuleResult ValidateDiscountType(string? classCode, string? discountType)
    {
        if (string.IsNullOrEmpty(classCode) || discountType != ValueDiscount)
        {
            return RuleResult.Empty;
        }

        var adjusted = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [DiscountTypeField] = RateDiscount,
        };

        return new RuleResult
        {
            Messages = new[] { new ValidationMessage(DiscountTypeField, ValueDiscountText, ValidationMessage.Blocking, DiscountTypeRule) },
            Adjusted = new ReadOnlyDictionary<string, object?>(adjusted),
        };
    }

    /// <summary>Requires an approval reference on a credit line whose service needs approval (T074).</summary>
    /// <param name="serviceId">Line service id (SERVICEID); null, empty or white space skips the check.</param>
    /// <param name="payType">Header pay type (PAYTYPE); only 2 (credit) is checked.</param>
    /// <param name="x422ApprovCheck">Approval-check setting X422_APPROV_CHECK: 1 enforces, 2 bypasses.</param>
    /// <param name="reqNeedA">Approval-needed flag (REQ_NEED_A); null or 0 never blocks.</param>
    /// <param name="approvRefNo">Approval reference number (APPROV_REF_NO); null, empty or white space is missing.</param>
    /// <returns>A blocking APPROV_REF_NO message of the service id followed by 'Need Approval'; otherwise <see cref="RuleResult.Empty"/>.</returns>
    public static RuleResult ValidateApproval(string? serviceId, int? payType, int? x422ApprovCheck, int? reqNeedA, string? approvRefNo)
    {
        if (string.IsNullOrWhiteSpace(serviceId) || payType != CreditPayType)
        {
            return RuleResult.Empty;
        }

        if (x422ApprovCheck == ApprovalCheckBypassed || reqNeedA == 0)
        {
            return RuleResult.Empty;
        }

        if (x422ApprovCheck == ApprovalCheckEnforced && reqNeedA is not null && reqNeedA != 0 && string.IsNullOrWhiteSpace(approvRefNo))
        {
            return Block(ApprovalReferenceField, serviceId + NeedApprovalSuffix, ApprovalRule);
        }

        return RuleResult.Empty;
    }

    /// <summary>Requires a service on the line (T066).</summary>
    /// <param name="serviceId">Line service id (SERVICEID).</param>
    /// <returns>A blocking SERVICEID message when the service id is null, empty or white space; otherwise <see cref="RuleResult.Empty"/>.</returns>
    public static RuleResult RequireService(string? serviceId) =>
        string.IsNullOrWhiteSpace(serviceId) ? Block(ServiceField, SelectServiceText, ServiceRule) : RuleResult.Empty;

    /// <summary>Warns when an insured new invoice carries a service the doctor did not request on the claim (T052).</summary>
    /// <param name="header">Draft header supplying SUB_COMP_CODE and INV_NO.</param>
    /// <param name="service">Server-read profile of the line's service; null raises no warning.</param>
    /// <param name="requested">True when PAT_SERV_REQ holds the service for the header's claim.</param>
    /// <returns>A SERVICEID warning when the header has a sub-company, no invoice number, the service's BEGIN_OF_CLAIM is 0 or null, and the service is not requested; otherwise <see cref="RuleResult.Empty"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="header"/> is null.</exception>
    public static RuleResult WarnNotRequested(InvoiceHeaderDraft header, ServiceProfile? service, bool requested)
    {
        ArgumentNullException.ThrowIfNull(header);

        if (string.IsNullOrEmpty(header.SubCompCode) || header.InvNo is not null || service is null)
        {
            return RuleResult.Empty;
        }

        if ((service.BeginOfClaim ?? 0) != 0 || requested)
        {
            return RuleResult.Empty;
        }

        return new RuleResult
        {
            Messages = new[] { new ValidationMessage(ServiceField, NotRequestedText, ValidationMessage.Warning, NotRequestedRule) },
        };
    }

    private static RuleResult Block(string field, string text, string rule) => new()
    {
        Messages = new[] { new ValidationMessage(field, text, ValidationMessage.Blocking, rule) },
    };
}
