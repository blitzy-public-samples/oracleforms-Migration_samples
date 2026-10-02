using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Api.Contracts;

/// <summary>Read-only view of a saved invoice.</summary>
public sealed record InvoiceViewResponse
{
    /// <summary>Saved invoice header.</summary>
    public InvoiceHeaderDraft Header { get; init; } = new();

    /// <summary>Saved invoice lines.</summary>
    public IReadOnlyList<InvoiceLineDraft> Lines { get; init; } = [];

    /// <summary>Lookup names and saved values by upper-case key, with line totals summed at read time; CASH_COLLECTED is the amount due, TOTAL_COLLECTED amount 1 plus amount 2.</summary>
    public IReadOnlyDictionary<string, object?> Display { get; init; } = new Dictionary<string, object?>();

    /// <summary>Whether the invoice is read-only.</summary>
    public bool ReadOnly { get; init; } = true;

    /// <summary>Open-item ids that apply to this view.</summary>
    public IReadOnlyList<string> OpenItems { get; init; } = [];
}
