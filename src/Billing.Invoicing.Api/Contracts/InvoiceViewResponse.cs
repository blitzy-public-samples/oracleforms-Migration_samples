using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Api.Contracts;

/// <summary>Read-only view of a saved invoice.</summary>
public sealed record InvoiceViewResponse
{
    /// <summary>Saved invoice header.</summary>
    public InvoiceHeaderDraft Header { get; init; } = new();

    /// <summary>Saved invoice lines.</summary>
    public IReadOnlyList<InvoiceLineDraft> Lines { get; init; } = [];

    /// <summary>Display-only lookup names and persisted totals keyed by upper-case column or item name, with the saved-line totals TOTAL_GROSS, TOTAL_DISCOUNT and TOTAL_NET, CASH_COLLECTED as the amount due and TOTAL_COLLECTED as amount 1 plus amount 2.</summary>
    public IReadOnlyDictionary<string, object?> Display { get; init; } = new Dictionary<string, object?>();

    /// <summary>Whether the invoice is read-only.</summary>
    public bool ReadOnly { get; init; } = true;

    /// <summary>Open-item ids that apply to this view.</summary>
    public IReadOnlyList<string> OpenItems { get; init; } = [];
}
