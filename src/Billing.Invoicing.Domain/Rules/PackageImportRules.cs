using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Domain.Rules;

/// <summary>DR-19: reports a package import that added no lines (T089).</summary>
public static class PackageImportRules
{
    private const string RuleId = "DR-19";
    private const string NoServesAdded = "No Serves Added";

    /// <summary>Returns the form-level warning raised when a package import yields no lines.</summary>
    /// <param name="lineCount">Number of lines the package import returned.</param>
    /// <returns>One non-blocking warning 'No Serves Added' when <paramref name="lineCount"/> is zero or less; otherwise <see cref="RuleResult.Empty"/>.</returns>
    public static RuleResult Evaluate(int lineCount)
    {
        if (lineCount > 0)
        {
            return RuleResult.Empty;
        }

        return new RuleResult
        {
            Messages = new[] { new ValidationMessage(null, NoServesAdded, ValidationMessage.Warning, RuleId) },
        };
    }
}
