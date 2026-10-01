namespace Billing.Invoicing.Data.Oracle;

/// <summary>Settings for the Oracle data layer.</summary>
public sealed record InvoicingDataOptions
{
    /// <summary>Largest accepted <see cref="CommandTimeoutSeconds"/>: the longest call deadline, in whole seconds.</summary>
    public const int MaxCommandTimeoutSeconds = 4_294_967;

    internal const string CommandTimeoutSecondsKey = "Invoicing:CommandTimeoutSeconds";

    /// <summary>ODP.NET connection string for the HIS Oracle database.</summary>
    public string ConnectionString { get; init; } = "";

    /// <summary>Application id passed as <c>p_app_id</c> to the <c>BIL_IMPORT</c> request-selection calls.</summary>
    public int ApplicationId { get; init; } = 48;

    /// <summary>Capacity of each OUT associative array bound for a package output table; at least 1.</summary>
    public int MaxOutputLines { get; init; } = 1000;

    /// <summary>Timeout, in seconds from 1 to <see cref="MaxCommandTimeoutSeconds"/>, applied to every Oracle command and to each connection open, commit and rollback.</summary>
    public int CommandTimeoutSeconds { get; init; } = 30;

    /// <summary>Base64 secret of at least 32 bytes that seals the draft date issued with each draft; empty uses a key of this instance only.</summary>
    public string DraftSealKey { get; init; } = "";

    /// <summary>Whether <see cref="ConnectionString"/> holds an attribute: false when it is null, empty, or only white space and semicolons (D-162).</summary>
    public bool HasConnectionString =>
        ConnectionString is { } connectionString && !connectionString.All(character => char.IsWhiteSpace(character) || character == ';');

    /// <summary>Throws when <see cref="CommandTimeoutSeconds"/> is below 1 or above <see cref="MaxCommandTimeoutSeconds"/>.</summary>
    /// <param name="paramName">Name of the caller's options parameter, reported in the exception.</param>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="CommandTimeoutSeconds"/> is below 1 or above <see cref="MaxCommandTimeoutSeconds"/>.</exception>
    internal void EnsureCommandTimeout(string paramName)
    {
        if (CommandTimeoutSeconds < 1)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                CommandTimeoutSeconds,
                $"{CommandTimeoutSecondsKey} ({nameof(InvoicingDataOptions)}.{nameof(CommandTimeoutSeconds)}) must be at least 1.");
        }

        if (CommandTimeoutSeconds > MaxCommandTimeoutSeconds)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                CommandTimeoutSeconds,
                $"{CommandTimeoutSecondsKey} ({nameof(InvoicingDataOptions)}.{nameof(CommandTimeoutSeconds)}) must be at most {MaxCommandTimeoutSeconds}.");
        }
    }
}
