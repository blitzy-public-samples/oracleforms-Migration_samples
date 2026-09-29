namespace Billing.Invoicing.Data.Errors;

/// <summary>A Data-layer failure translated into an HTTP status and error-contract payload.</summary>
public sealed record DataFailure
{
    /// <summary>Type value for an Oracle application error in -20000 … -20999.</summary>
    public const string OracleBusinessErrorType = "oracle-business-error";

    /// <summary>Type value for missing operator context (application, session or user).</summary>
    public const string OperatorContextMissingType = "operator-context-missing";

    /// <summary>Type value for an Oracle connectivity or availability failure.</summary>
    public const string OracleUnavailableType = "oracle-unavailable";

    /// <summary>Type value for a blocked operation that names an open item.</summary>
    public const string OpenItemType = "open-item";

    /// <summary>Type value for any other Oracle error.</summary>
    public const string OracleErrorType = "oracle-error";

    /// <summary>HTTP status: 422, 501, 503 or 500.</summary>
    public int Status { get; init; }

    /// <summary>Error-contract type; one of the type constants of this record.</summary>
    public required string Type { get; init; }

    /// <summary>Catalogue kind such as RequestLinesStale, DefinitionStale, IdempotencyConflict or OperatorContextMissing; null when uncatalogued.</summary>
    public string? Kind { get; init; }

    /// <summary>Signed Oracle error number (ORA-20931 as -20931, other codes positive); null for non-Oracle failures.</summary>
    public int? Number { get; init; }

    /// <summary>Attributed source package, or UNKNOWN; null when no package applies.</summary>
    public string? Package { get; init; }

    /// <summary>Operator-facing text: the Oracle text after the ORA prefix, or the exception message.</summary>
    public required string Message { get; init; }

    /// <summary>Legacy item name the failure maps to; null for a form-level failure.</summary>
    public string? Field { get; init; }

    /// <summary>Verbatim legacy Form text shown instead of Message when set.</summary>
    public string? LegacyText { get; init; }

    /// <summary>Open-item id (OI-xx) for a 501; otherwise null.</summary>
    public string? OpenItemId { get; init; }
}
