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

    /// <summary>For a line target, whether an operator-entered PRICE is accepted on the validated line; null otherwise.</summary>
    public bool? PriceEditable { get; init; }

    /// <summary>Service id of the validated line that PriceEditable was judged on; null when PriceEditable is null.</summary>
    public string? PriceJudgedServiceId { get; init; }

    /// <summary>Patient number of the request that PriceEditable was judged on; null when PriceEditable is null.</summary>
    public string? PriceJudgedPatientNo { get; init; }

    /// <summary>Company code of the request that PriceEditable was judged on; null when PriceEditable is null.</summary>
    public string? PriceJudgedCompCode { get; init; }

    /// <summary>Coverage of the validated patient for a PATIENTNO target with a patient number; null otherwise.</summary>
    public CoverageResponse? Coverage { get; init; }
}
