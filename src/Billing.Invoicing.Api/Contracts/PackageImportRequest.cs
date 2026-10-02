namespace Billing.Invoicing.Api.Contracts;

/// <summary>Request to expand a package service into draft lines.</summary>
public sealed record PackageImportRequest
{
    /// <summary>The current invoice draft.</summary>
    public DraftDto Draft { get; init; } = new();

    /// <summary>Service id of the package to expand (<c>SERVICEID</c>).</summary>
    public string PackageServiceId { get; init; } = string.Empty;

    /// <summary>Optional parent source id passed as <c>p_parent_source_id</c>; null when none.</summary>
    public string? ParentSourceId { get; init; }
}
