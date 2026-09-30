namespace Billing.Invoicing.Api.Contracts;

/// <summary>Request to load a bundled offer's lines into the draft.</summary>
public sealed record BundledOfferRequest
{
    /// <summary>The current invoice draft.</summary>
    public DraftDto Draft { get; init; } = new();

    /// <summary>Offer chosen in the offers list (<c>OFERID</c>).</summary>
    public decimal OfferId { get; init; }

    /// <summary>Number of bundles to load (<c>p_bundle_qty</c>).</summary>
    public decimal BundleQty { get; init; } = 1m;
}
