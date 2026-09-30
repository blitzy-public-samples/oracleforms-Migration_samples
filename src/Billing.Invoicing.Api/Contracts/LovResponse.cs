namespace Billing.Invoicing.Api.Contracts;

/// <summary>Rows of one list of values.</summary>
public sealed record LovResponse
{
    /// <summary>List-of-values name as requested, in upper case, such as <c>COMPANY1_2</c>.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Record-group rows, each keyed by upper-case column name.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; } = [];

    /// <summary><c>true</c> when the rows can be viewed but not chosen.</summary>
    public bool ViewOnly { get; init; }

    /// <summary>Messages returned with the list.</summary>
    public IReadOnlyList<MessageDto> Messages { get; init; } = [];

    /// <summary>Advisory open-item ids that apply to the list.</summary>
    public IReadOnlyList<string> OpenItems { get; init; } = [];
}
