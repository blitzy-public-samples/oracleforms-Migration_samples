using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Api.Contracts;

/// <summary>Patient coverage, pay type and eligibility messages.</summary>
public sealed record CoverageResponse
{
    /// <summary>Patient coverage; <c>null</c> when the patient has none.</summary>
    public PatientCoverageSnapshot? Coverage { get; init; }

    /// <summary>Pay type decided for the patient: 0 when undetermined; otherwise 1 cash or 2 credit.</summary>
    public int PayType { get; init; }

    /// <summary>Eligibility messages, blocking and warning.</summary>
    public IReadOnlyList<MessageDto> Messages { get; init; } = [];

    /// <summary>Advisory open-item ids, such as <c>OI-21</c>.</summary>
    public IReadOnlyList<string> OpenItems { get; init; } = [];
}
