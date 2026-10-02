using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Billing.Invoicing.Api.Contracts;

/// <summary>Request to load a bundled offer's lines into the draft.</summary>
public sealed record BundledOfferRequest : IValidatableObject
{
    private const string OfferIdItem = "OFERID";
    private const string InvalidOfferIdText = "Offer id must be a positive whole number.";

    /// <summary>The current invoice draft.</summary>
    public DraftDto Draft { get; init; } = new();

    /// <summary>Offer chosen in the offers list (<c>OFERID</c>), read from decimal text or a JSON number.</summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal OfferId { get; init; }

    /// <summary>Number of bundles to load (<c>p_bundle_qty</c>).</summary>
    public decimal BundleQty { get; init; } = 1m;

    /// <summary>Rejects an <c>OFERID</c> that is not a positive whole number, naming the legacy item.</summary>
    IEnumerable<ValidationResult> IValidatableObject.Validate(ValidationContext validationContext)
    {
        if (OfferId <= 0m || decimal.Truncate(OfferId) != OfferId)
        {
            yield return new ValidationResult(InvalidOfferIdText, [OfferIdItem]);
        }
    }
}
