using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Api.Contracts;

/// <summary>Calculated lines, totals and payment figures of a draft; nothing is saved.</summary>
public sealed record PreviewResponse
{
    /// <summary>Calculated lines as returned by the package, each with its price list id.</summary>
    public IReadOnlyList<EditablePreviewLine> Lines { get; init; } = [];

    /// <summary>Package totals and payment status.</summary>
    public PreviewTotals Totals { get; init; } = new();

    /// <summary>Refund to the patient (<c>REUND</c>).</summary>
    public decimal Refund { get; init; }

    /// <summary>Amount 1 plus amount 2 (<c>CASH_COLLECTED</c> item).</summary>
    public decimal TotalCollected { get; init; }

    /// <summary>Messages returned with the preview.</summary>
    public IReadOnlyList<MessageDto> Messages { get; init; } = [];

    /// <summary>Advisory open-item ids that apply to this preview.</summary>
    public IReadOnlyList<string> OpenItems { get; init; } = [];
}
