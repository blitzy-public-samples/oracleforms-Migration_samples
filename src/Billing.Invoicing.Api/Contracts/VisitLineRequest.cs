namespace Billing.Invoicing.Api.Contracts;

/// <summary>Request to add the automatic visit line for the draft's doctor.</summary>
public sealed record VisitLineRequest
{
    /// <summary>The current draft, from which the server chooses the visit line.</summary>
    public DraftDto Draft { get; init; } = new();
}
