using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Domain.Workflow;

/// <summary>DR-17: decides whether an invoice can be edited or deleted (T010, T020, T021, T062).</summary>
public static class InvoiceStatePolicy
{
    private const string Rule = "DR-17";

    private const string DeleteRefusedText = "You Cant Delete Invoice From Here";

    /// <summary>Returns whether the invoice header and lines can be changed, lines added or lines removed.</summary>
    /// <param name="invNo">Invoice number; null for an unsaved draft.</param>
    /// <returns>True for an unsaved draft; false for any saved or queried invoice.</returns>
    public static bool CanEdit(long? invNo) => invNo is null;

    /// <summary>Returns the outcome of a request to delete the invoice.</summary>
    /// <param name="invNo">Invoice number; null for an unsaved draft.</param>
    /// <returns>A blocking form-level result carrying the legacy refusal text, for any invoice number.</returns>
    public static RuleResult CanDelete(long? invNo) => new()
    {
        Messages = [new ValidationMessage(null, DeleteRefusedText, ValidationMessage.Blocking, Rule)],
    };
}
