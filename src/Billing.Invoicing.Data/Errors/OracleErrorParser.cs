using System.Globalization;
using System.Text.RegularExpressions;
using Oracle.ManagedDataAccess.Client;

namespace Billing.Invoicing.Data.Errors;

/// <summary>Copies ODP.NET exception values into <see cref="OracleErrorInfo"/> and parses the signed number, error text and ORA-06512 frames. UNVERIFIED against Oracle.</summary>
public static partial class OracleErrorParser
{
    /// <summary><see cref="Exception.Data"/> key whose value <c>true</c> marks a failure raised while opening the connection.</summary>
    public const string DuringOpenKey = "Billing.Invoicing.Data.DuringOpen";

    /// <summary><see cref="Exception.Data"/> key whose value names the Data gateway operation that failed.</summary>
    public const string OperationKey = "Billing.Invoicing.Data.Operation";

    private const int ApplicationErrorFirst = 20000;
    private const int ApplicationErrorLast = 20999;

    private const RegexOptions ParseOptions = RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture;

    /// <summary>Copies number, message, open phase and failing operation from an ODP.NET exception and parses them.</summary>
    /// <param name="exception">The driver exception.</param>
    /// <returns>The parsed error.</returns>
    /// <exception cref="ArgumentException">The exception number disagrees with the first ORA code in its message.</exception>
    public static OracleErrorInfo FromException(OracleException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        bool duringOpen = exception.Data[DuringOpenKey] is true;
        string? operation = exception.Data[OperationKey] as string;

        return FromParts(exception.Number, exception.Message ?? string.Empty, duringOpen, operation);
    }

    /// <summary>Parses an Oracle error number and message into a signed number, first-line error text and ORA-06512 frames.</summary>
    /// <param name="number">ORA code as reported by the driver, positive or already signed.</param>
    /// <param name="message">Full error message, including any ORA-06512 lines.</param>
    /// <param name="duringOpen">Whether the failure occurred while opening the connection.</param>
    /// <param name="operation">Data gateway operation that failed, or null when unknown.</param>
    /// <returns>The parsed error.</returns>
    /// <exception cref="ArgumentException">The number disagrees with the first ORA code in the message, or is <see cref="int.MinValue"/>.</exception>
    public static OracleErrorInfo FromParts(int number, string message, bool duringOpen = false, string? operation = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        // int.MinValue has no positive int counterpart; it is rejected as an argument error.
        ArgumentOutOfRangeException.ThrowIfEqual(number, int.MinValue);

        // Normalise: application errors 20000 to 20999 become negative; every other code is positive.
        int code = Math.Abs(number);
        int signed = code is >= ApplicationErrorFirst and <= ApplicationErrorLast ? -code : code;

        // Cross-check: the first ORA code in the message, when present, must equal the number.
        Match oraCode = OraCodeRegex().Match(message);
        if (oraCode.Success)
        {
            int messageCode = int.Parse(oraCode.Groups["code"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture);
            if (messageCode != code)
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Oracle error number {number} disagrees with ORA-{messageCode:D5} in the error message."),
                    nameof(number));
            }
        }

        return new OracleErrorInfo
        {
            Number = signed,
            Message = message,
            Text = ParseText(message),
            Frames = ParseFrames(message),
            DuringOpen = duringOpen,
            Operation = operation,
        };
    }

    /// <summary>Returns the text after the first "ORA-nnnnn: " prefix up to the end of its line, or the whole message when no prefix exists.</summary>
    private static string ParseText(string message)
    {
        Match prefix = OraPrefixRegex().Match(message);
        if (!prefix.Success)
        {
            return message;
        }

        ReadOnlySpan<char> rest = message.AsSpan(prefix.Index + prefix.Length);
        int lineEnd = rest.IndexOfAny('\r', '\n');
        ReadOnlySpan<char> line = lineEnd < 0 ? rest : rest[..lineEnd];
        return line.TrimEnd().ToString();
    }

    /// <summary>Returns every ORA-06512 frame that names a quoted object, in message order.</summary>
    private static IReadOnlyList<(string Schema, string Package, int Line)> ParseFrames(string message)
    {
        MatchCollection matches = FrameRegex().Matches(message);
        if (matches.Count == 0)
        {
            return Array.Empty<(string Schema, string Package, int Line)>();
        }

        var frames = new List<(string Schema, string Package, int Line)>(matches.Count);
        foreach (Match match in matches)
        {
            // Frames whose line number does not fit an int are skipped.
            if (!int.TryParse(match.Groups["line"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int line))
            {
                continue;
            }

            Group schema = match.Groups["schema"];
            frames.Add((schema.Success ? schema.Value : string.Empty, match.Groups["object"].Value, line));
        }

        return frames.AsReadOnly();
    }

    /// <summary>First "ORA-" followed by a five-digit code anywhere in the message.</summary>
    [GeneratedRegex("ORA-(?<code>[0-9]{5})", ParseOptions)]
    private static partial Regex OraCodeRegex();

    /// <summary>First "ORA-nnnnn: " prefix in the message.</summary>
    [GeneratedRegex("ORA-[0-9]{5}: ", ParseOptions)]
    private static partial Regex OraPrefixRegex();

    /// <summary>An ORA-06512 frame with a quoted, optionally schema-qualified object name and a line number.</summary>
    [GeneratedRegex("ORA-06512: at \"(?:(?<schema>[^\".]+)\\.)?(?<object>[^\"]+)\", line (?<line>[0-9]+)", ParseOptions)]
    private static partial Regex FrameRegex();
}
