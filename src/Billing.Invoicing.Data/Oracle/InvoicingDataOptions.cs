namespace Billing.Invoicing.Data.Oracle;

/// <summary>Settings for the Oracle data layer.</summary>
public sealed record InvoicingDataOptions
{
    /// <summary>ODP.NET connection string for the HIS Oracle database.</summary>
    public string ConnectionString { get; init; } = "";

    /// <summary>Application id passed as <c>p_app_id</c> to the <c>BIL_IMPORT</c> request-selection calls.</summary>
    public int ApplicationId { get; init; } = 48;

    /// <summary>Capacity of each OUT associative array bound for a package output table; at least 1.</summary>
    public int MaxOutputLines { get; init; } = 1000;

    /// <summary>Timeout, in seconds and at least 1, applied to every Oracle command.</summary>
    public int CommandTimeoutSeconds { get; init; } = 30;
}
