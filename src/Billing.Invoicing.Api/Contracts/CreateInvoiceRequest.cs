namespace Billing.Invoicing.Api.Contracts;

/// <summary>Request to save a draft as an invoice.</summary>
public sealed record CreateInvoiceRequest
{
    /// <summary>The draft to save, carrying its request id and any discount-limit choice.</summary>
    public DraftDto Draft { get; init; } = new();
}
