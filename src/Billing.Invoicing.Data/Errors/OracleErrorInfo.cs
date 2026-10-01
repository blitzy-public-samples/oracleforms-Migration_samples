namespace Billing.Invoicing.Data.Errors;

/// <summary>Oracle error copied from a driver exception and parsed into number, text and PL/SQL stack frames.</summary>
public sealed record OracleErrorInfo
{
    /// <summary>Signed ORA code: application errors 20000 to 20999 are negative (ORA-20931 is -20931); every other code is positive.</summary>
    public int Number { get; init; }

    /// <summary>Full original error message, unmodified, including every ORA-06512 line.</summary>
    public required string Message { get; init; }

    /// <summary>Text after the first ORA code and its ": " up to the end of that line, trailing whitespace trimmed; a fixed unreadable-text message when that code is missing or not followed by ": ".</summary>
    public required string Text { get; init; }

    /// <summary>ORA-06512 frames naming a quoted object on one line, each name part at most 128 characters, innermost first; Schema is empty when the name has no schema part.</summary>
    public IReadOnlyList<(string Schema, string Package, int Line)> Frames { get; init; } = Array.Empty<(string, string, int)>();

    /// <summary>True when the failure occurred while opening the connection.</summary>
    public bool DuringOpen { get; init; }

    /// <summary>Name of the Data gateway operation that failed, or null when unknown.</summary>
    public string? Operation { get; init; }
}
