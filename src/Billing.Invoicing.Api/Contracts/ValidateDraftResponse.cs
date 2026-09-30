using System.Collections.ObjectModel;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Api.Contracts;

/// <summary>Result of validating one draft item, line or record.</summary>
public sealed record ValidateDraftResponse
{
    /// <summary>Blocking and warning messages raised by the validation, together.</summary>
    public IReadOnlyList<MessageDto> Messages { get; init; } = [];

    /// <summary>Item values the rules changed, keyed by upper-case legacy item name such as <c>AMOUNT_2</c>.</summary>
    public IReadOnlyDictionary<string, object?> Adjusted
    {
        get;
        init => field = new ReadOnlyDictionary<string, object?>(
            new Dictionary<string, object?>(value ?? throw new ArgumentNullException(nameof(value)), StringComparer.Ordinal));
    } = ReadOnlyDictionary<string, object?>.Empty;

    /// <summary>Advisory open-item ids that apply to the validated target.</summary>
    public IReadOnlyList<string> OpenItems { get; init; } = [];

    /// <summary>Automatic visit line chosen for a doctor validation; null otherwise.</summary>
    public VisitLineChoice? VisitLine { get; init; }
}
