namespace Billing.Invoicing.Api.Contracts;

/// <summary>New draft with its defaults and preloads.</summary>
public sealed record NewDraftResponse
{
    /// <summary>The new draft with its defaults applied.</summary>
    public DraftDto Draft { get; init; } = new();

    /// <summary>Warnings raised while defaulting the draft.</summary>
    public IReadOnlyList<MessageDto> Messages { get; init; } = [];

    /// <summary>Advisory open-item ids that apply to the new draft.</summary>
    public IReadOnlyList<string> OpenItems { get; init; } = [];
}
