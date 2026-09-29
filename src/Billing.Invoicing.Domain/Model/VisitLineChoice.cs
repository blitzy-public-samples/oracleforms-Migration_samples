namespace Billing.Invoicing.Domain.Model;

/// <summary>The automatic visit line a draft receives after its doctor is validated (DR-25).</summary>
/// <param name="Kind">One of "None", "Consultation", "Review" or "FixedService".</param>
/// <param name="ServiceId">The service of a "FixedService" line; null for every other kind.</param>
public sealed record VisitLineChoice(string Kind, string? ServiceId)
{
    private const string NoneKind = "None";
    private const string ConsultationKind = "Consultation";
    private const string ReviewKind = "Review";
    private const string FixedServiceKind = "FixedService";

    /// <summary>No automatic visit line.</summary>
    public static VisitLineChoice None { get; } = new(NoneKind, null);

    /// <summary>The doctor's consultation line, supplied by BIL_IMPORT.GET_VISIT_LINE.</summary>
    public static VisitLineChoice Consultation { get; } = new(ConsultationKind, null);

    /// <summary>The doctor's review line, supplied by BIL_IMPORT.GET_VISIT_LINE.</summary>
    public static VisitLineChoice Review { get; } = new(ReviewKind, null);

    /// <summary>An ordinary draft line for the given service with quantity 1, priced by the package.</summary>
    /// <param name="serviceId">The service id of the line.</param>
    /// <returns>A "FixedService" choice carrying <paramref name="serviceId"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="serviceId"/> is null, empty or white space.</exception>
    public static VisitLineChoice FixedService(string serviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        return new(FixedServiceKind, serviceId);
    }
}
