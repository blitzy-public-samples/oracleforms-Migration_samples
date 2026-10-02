using System.Collections.ObjectModel;

namespace Billing.Invoicing.Domain.Model;

/// <summary>Outcome of a domain rule: the messages it raised and the item values it set.</summary>
public sealed record RuleResult
{
    private static readonly IReadOnlyDictionary<string, object?> NoAdjustments =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(StringComparer.Ordinal));

    /// <summary>Messages raised by the rule.</summary>
    public IReadOnlyList<ValidationMessage> Messages { get; init; } = Array.Empty<ValidationMessage>();

    /// <summary>Item values the rule set, keyed by legacy item name in upper case, such as <c>AMOUNT_2</c>.</summary>
    public IReadOnlyDictionary<string, object?> Adjusted { get; init; } = NoAdjustments;

    /// <summary>True when any message is blocking.</summary>
    public bool IsBlocking => Messages.Any(m => m.Severity == ValidationMessage.Blocking);

    /// <summary>Result with no messages and no adjustments.</summary>
    public static RuleResult Empty { get; } = new();
}
