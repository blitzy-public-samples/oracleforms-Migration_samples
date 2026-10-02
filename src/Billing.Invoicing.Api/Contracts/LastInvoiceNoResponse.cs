namespace Billing.Invoicing.Api.Contracts;

/// <summary>Last invoice number of the operator's information centre.</summary>
public sealed record LastInvoiceNoResponse
{
    /// <summary>Highest invoice number of the operator's information centre (<c>INV_NO</c>).</summary>
    public required long InvNo { get; init; }
}
