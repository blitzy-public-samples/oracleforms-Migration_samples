using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Api.Contracts;

/// <summary>Unsaved invoice draft exchanged between client and server.</summary>
public sealed record DraftDto
{
    /// <summary>32-character upper-case hexadecimal request id, kept for the life of the draft.</summary>
    public string RequestId { get; init; } = string.Empty;

    /// <summary>Database time read when the draft was created.</summary>
    public DateTime DraftDate { get; init; }

    /// <summary>The <c>T_INV</c> header.</summary>
    public InvoiceHeaderDraft Header { get; init; } = new();

    /// <summary>The <c>D_INV</c> lines in grid order; a line's zero-based position is its line index.</summary>
    public IReadOnlyList<InvoiceLineDraft> Lines { get; init; } = [];

    /// <summary>The Form entry parameters set by the calling module.</summary>
    public InvoiceEntryParameters Parameters { get; init; } = new();

    /// <summary>The operator's answer to the maximum-discount prompt; null when none was given.</summary>
    public Billing.Invoicing.Domain.Model.DiscountLimitChoice? DiscountLimitChoice { get; init; }
}
