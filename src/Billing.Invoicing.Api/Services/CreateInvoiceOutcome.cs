using Billing.Invoicing.Api.Contracts;

namespace Billing.Invoicing.Api.Services;

/// <summary>Outcome of <see cref="InvoiceWorkflowService.Create"/>: the saved or replayed invoice, or the messages and open items that refused the draft.</summary>
public sealed record CreateInvoiceOutcome
{
    /// <summary>The 201 body of the saved or replayed invoice; null when the draft was refused.</summary>
    public CreateInvoiceResponse? Invoice { get; init; }

    /// <summary>The invoice's warnings when saved; the blocking and warning messages when refused.</summary>
    public IReadOnlyList<MessageDto> Messages { get; init; } = [];

    /// <summary>The invoice's open-item ids when saved; the gate ids when refused.</summary>
    public IReadOnlyList<string> OpenItems { get; init; } = [];

    /// <summary>Invoice number of the saved or replayed invoice; null when refused.</summary>
    public long? InvNo => Invoice?.InvNo;

    /// <summary>Package result message of the saved or replayed invoice; null when refused.</summary>
    public string? Message => Invoice?.Message;
}
