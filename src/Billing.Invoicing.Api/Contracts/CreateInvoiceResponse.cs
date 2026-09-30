using System.Collections.ObjectModel;

namespace Billing.Invoicing.Api.Contracts;

/// <summary>Result of saving an invoice.</summary>
public sealed record CreateInvoiceResponse
{
    /// <summary>Invoice number saved, or returned by a replay of the same request id (<c>INV_NO</c>).</summary>
    public required long InvNo { get; init; }

    /// <summary>Package result message (<c>t_full_invoice_result.message</c>).</summary>
    public string? Message { get; init; }

    /// <summary>Posting-stage flags <c>Y</c> or <c>N</c>, keyed <c>payment</c>, <c>queue</c>, <c>stock</c>, <c>printUrl</c> and <c>sms</c>.</summary>
    public IReadOnlyDictionary<string, string?> PostingFlags
    {
        get;
        init => field = new ReadOnlyDictionary<string, string?>(
            new Dictionary<string, string?>(value ?? throw new ArgumentNullException(nameof(value)), StringComparer.Ordinal));
    } = ReadOnlyDictionary<string, string?>.Empty;

    /// <summary>Warning messages returned with the result.</summary>
    public IReadOnlyList<MessageDto> Messages { get; init; } = [];

    /// <summary>Open-item ids listed with the result.</summary>
    public IReadOnlyList<string> OpenItems { get; init; } = [];
}
