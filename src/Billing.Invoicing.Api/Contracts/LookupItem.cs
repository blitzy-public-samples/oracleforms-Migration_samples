namespace Billing.Invoicing.Api.Contracts;

/// <summary>Code and label of one lookup list entry.</summary>
public sealed record LookupItem
{
    /// <summary>List value as text, such as an invoice type id or a currency code.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Display label of the entry.</summary>
    public string Name { get; init; } = string.Empty;
}
