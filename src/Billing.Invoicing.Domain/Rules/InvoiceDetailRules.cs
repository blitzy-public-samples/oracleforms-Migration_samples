using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Domain.Rules;

/// <summary>DR-02: requires an invoice to have at least one detail line before it is saved (T009).</summary>
public static class InvoiceDetailRules
{
    private const string InvoiceWithoutDetails = "Invoice without Details";

    /// <summary>Checks that the draft has at least one line.</summary>
    /// <param name="lineCount">Number of lines in the draft.</param>
    /// <returns>A form-level blocking 'Invoice without Details' message when <paramref name="lineCount"/> is zero or less; otherwise <see cref="RuleResult.Empty"/>.</returns>
    public static RuleResult RequireDetails(int lineCount)
    {
        if (lineCount > 0)
        {
            return RuleResult.Empty;
        }

        return new RuleResult
        {
            Messages = new[] { new ValidationMessage(null, InvoiceWithoutDetails, ValidationMessage.Blocking, "DR-02") },
        };
    }
}
