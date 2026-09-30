using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Data.Blocked;

/// <summary>Blocked members for legacy database functions, reports and messaging absent from the ingestion; each throws <see cref="NotImplementedException"/> whose message starts with its open-item id.</summary>
public sealed class LegacyExternalCalls : ILegacyExternalCalls
{
    private const string ValidateTotalInvoiceMessage = $"{OpenItemIds.OI20}: VALIDATE_TOTAL_INV total invoice validation is not available.";

    private const string PreAuthorizationMessage = $"{OpenItemIds.OI21}: GET_ELLIGABILTY pre-authorisation derivation is not available.";

    private const string PatientAgeMessage = $"{OpenItemIds.OI22}: DAY_TO_DAYES patient age calculation is not available.";

    private const string PaidBeforeMessage = $"{OpenItemIds.OI23}: GET_PAYID_VALUE prior claim payments are not available.";

    private const string PricePlanMessage = $"{OpenItemIds.OI24}: GET_PRICE_PLAN price plan and price list resolution is not available.";

    private const string PatientCardMessage = $"{OpenItemIds.OI47}: PAT_CARD_INV.jsp patient card report is not available.";

    private const string BarcodeSmsMessage = $"{OpenItemIds.OI47}: PAT_CARD_INV.jsp barcode SMS is not available ({OpenItemIds.OI26} SEND_MESSAG).";

    private const string IqamaCheckMessage = $"{OpenItemIds.OI48}: iqama_check.jsp iqama check report is not available.";

    private const string InvoiceSmsMessage = $"{OpenItemIds.OI12}: BIL_MESSAGE invoice SMS is not available ({OpenItemIds.OI45} inv_small_cash.jsp).";

    private const string TransferStockMessage = $"{OpenItemIds.OI10}: BIL_STOCK_POSTING store transfer is not available ({OpenItemIds.OI44} SILENT_COMMET00).";

    private const string UnknownDocumentKindMessage = "Unknown legacy document kind.";

    /// <summary>Stands in for VALIDATE_TOTAL_INV; throws OI-20.</summary>
    /// <exception cref="NotImplementedException">Always, with a message starting OI-20.</exception>
    public Task ValidateTotalInvoice(IOracleSession session, long invNo, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException(ValidateTotalInvoiceMessage);

    /// <summary>Stands in for GET_ELLIGABILTY; throws OI-21.</summary>
    /// <exception cref="NotImplementedException">Always, with a message starting OI-21.</exception>
    public Task<string?> GetPreAuthorization(string patientNo, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException(PreAuthorizationMessage);

    /// <summary>Stands in for DAY_TO_DAYES; throws OI-22.</summary>
    /// <exception cref="NotImplementedException">Always, with a message starting OI-22.</exception>
    public Task<decimal> ComputePatientAgeYears(string patientNo, DateTime asOf, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException(PatientAgeMessage);

    /// <summary>Stands in for GET_PAYID_VALUE; throws OI-23.</summary>
    /// <exception cref="NotImplementedException">Always, with a message starting OI-23.</exception>
    public Task<decimal> GetPaidBefore(string? compCode, string? subCompCode, string? claimNo, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException(PaidBeforeMessage);

    /// <summary>Stands in for GET_PRICE_PLAN and its PRICE_PLAN_M price-list read; throws OI-24.</summary>
    /// <exception cref="NotImplementedException">Always, with a message starting OI-24.</exception>
    public Task<(decimal? PlanCode, decimal? ListId)> ResolvePricePlan(string infoCenterId, string? compCode, string? subCompCode, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException(PricePlanMessage);

    /// <summary>Stands in for the PAT_CARD_INV.jsp and iqama_check.jsp report URLs; throws OI-47 or OI-48.</summary>
    /// <param name="kind">patient-card or barcode-sms (OI-47), or iqama-check (OI-48); compared ordinally.</param>
    /// <exception cref="NotImplementedException">For a known kind, with a message starting with its open-item id.</exception>
    /// <exception cref="ArgumentException">For any other kind, including null.</exception>
    public Task<string> BuildLegacyDocument(long invNo, string kind, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException(kind switch
        {
            "patient-card" => PatientCardMessage,
            "barcode-sms" => BarcodeSmsMessage,
            "iqama-check" => IqamaCheckMessage,
            _ => throw new ArgumentException(UnknownDocumentKindMessage, nameof(kind)),
        });

    /// <summary>Stands in for the BIL_MESSAGE invoice SMS carrying the inv_small_cash.jsp link; throws OI-12.</summary>
    /// <exception cref="NotImplementedException">Always, with a message starting OI-12.</exception>
    public Task SendInvoiceSms(long invNo, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException(InvoiceSmsMessage);

    /// <summary>Stands in for the store transfer committed through SILENT_COMMET00; throws OI-10.</summary>
    /// <exception cref="NotImplementedException">Always, with a message starting OI-10.</exception>
    public Task TransferStock(long invNo, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException(TransferStockMessage);
}
