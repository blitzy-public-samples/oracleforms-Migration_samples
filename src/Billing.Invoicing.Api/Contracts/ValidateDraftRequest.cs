namespace Billing.Invoicing.Api.Contracts;

/// <summary>Request to validate one item, line or record of a draft.</summary>
public sealed record ValidateDraftRequest
{
    /// <summary>The current draft.</summary>
    public DraftDto Draft { get; init; } = new();

    /// <summary>Upper-case legacy item name, such as <c>PATIENTNO</c> or <c>QTY</c>, or <c>LINE</c> or <c>RECORD</c>.</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>Zero-based index into <see cref="DraftDto.Lines"/> for a line target; <c>null</c> for a header target.</summary>
    public int? LineIndex { get; init; }
}
