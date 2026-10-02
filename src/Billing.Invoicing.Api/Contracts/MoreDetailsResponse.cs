namespace Billing.Invoicing.Api.Contracts;

/// <summary>Read-only more-details fields of a saved invoice.</summary>
public sealed record MoreDetailsResponse
{
    /// <summary>Invoice number (<c>INV_NO</c>).</summary>
    public long InvNo { get; init; }

    /// <summary>Insurance number (<c>INS_NUMBER</c>).</summary>
    public string? InsNumber { get; init; }

    /// <summary>Insurance card expiry date (<c>CARD_END</c>).</summary>
    public DateTime? CardEnd { get; init; }

    /// <summary>Insurance policy number (<c>PAT_POLICY_NO</c>).</summary>
    public string? PatPolicyNo { get; init; }

    /// <summary>Per-line more-details fields, keyed by upper-case <c>D_INV</c> item name.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Lines { get; init; } = [];

    /// <summary>Store-transfer row ids of the invoice (<c>TRANS_M_ROW_ID</c>).</summary>
    public IReadOnlyList<string> TransMRowIds { get; init; } = [];
}
