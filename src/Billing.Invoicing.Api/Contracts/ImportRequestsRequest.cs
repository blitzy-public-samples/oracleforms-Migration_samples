namespace Billing.Invoicing.Api.Contracts;

/// <summary>Request to import the patient's selected service requests into the draft.</summary>
public sealed record ImportRequestsRequest
{
    /// <summary>The current draft, supplying patient, doctor, pay type and entry parameters.</summary>
    public DraftDto Draft { get; init; } = new();
}
