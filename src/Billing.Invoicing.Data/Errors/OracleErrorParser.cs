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

    private const int FrameMatchTimeoutMilliseconds = 1000;

    private const string TextSeparator = ": ";

    private const string UnreadableText = "The Oracle error text could not be read.";

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

    /// <summary>Finds the first ODP.NET exception in an exception and its inner-exception chain and parses it.</summary>
    /// <param name="exception">The exception raised by a Data member.</param>
    /// <param name="error">The parsed error, or null when no driver exception exists or its number and message cannot be parsed.</param>
    /// <param name="number">The signed driver number, or 0 when no driver exception exists.</param>
    /// <returns>True when the chain holds a driver exception.</returns>
    internal static bool TryFromExceptionChain(Exception exception, out OracleErrorInfo? error, out int number)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is not OracleException oracleException)
            {
                continue;
            }

            number = Normalise(oracleException.Number);
            try
            {
                error = FromException(oracleException);
            }
            catch (Exception parseFailure) when (parseFailure is ArgumentException or RegexMatchTimeoutException)
            {
                error = null;
            }

            return true;
        }

        error = null;
        number = 0;
        return false;
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

        // Rejects int.MinValue before normalizing the Oracle error number (D-116).
        ArgumentOutOfRangeException.ThrowIfEqual(number, int.MinValue);

        int signed = Normalise(number);
        int code = Math.Abs(signed);

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
            Text = ParseText(message, oraCode),
            Frames = ParseFrames(message),
            DuringOpen = duringOpen,
            Operation = operation,
        };
    }

    /// <summary>Returns the signed ORA code: application errors 20000 to 20999 negative, every other code positive.</summary>
    /// <param name="number">ORA code as reported by the driver, positive or already signed.</param>
    /// <returns>The signed code; <see cref="int.MinValue"/> unchanged.</returns>
    private static int Normalise(int number)
    {
        if (number == int.MinValue)
        {
            return number;
        }

        int code = Math.Abs(number);
        return code is >= ApplicationErrorFirst and <= ApplicationErrorLast ? -code : code;
    }

    /// <summary>Returns the text after the first ORA code and its ": " up to the end of that line, or a fixed text when that code is missing or not followed by ": " (D-117).</summary>
    private static string ParseText(string message, Match oraCode)
    {
        if (!oraCode.Success)
        {
            return UnreadableText;
        }

        ReadOnlySpan<char> rest = message.AsSpan(oraCode.Index + oraCode.Length);
        if (!rest.StartsWith(TextSeparator, StringComparison.Ordinal))
        {
            return UnreadableText;
        }

        rest = rest[TextSeparator.Length..];
        int lineEnd = rest.IndexOfAny('\r', '\n');
        ReadOnlySpan<char> line = lineEnd < 0 ? rest : rest[..lineEnd];
        return line.TrimEnd().ToString();
    }

    /// <summary>Returns, in message order, every ORA-06512 frame whose quoted name parts fit the frame pattern; on a match timeout, the frames found before it.</summary>
    private static IReadOnlyList<(string Schema, string Package, int Line)> ParseFrames(string message)
    {
        var frames = new List<(string Schema, string Package, int Line)>();
        try
        {
            Match match = FrameRegex().Match(message);
            while (match.Success)
            {
                // Frames whose line number does not fit an int are skipped (D-116).
                if (int.TryParse(match.Groups["line"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int line))
                {
                    Group schema = match.Groups["schema"];
                    frames.Add((schema.Success ? schema.Value : string.Empty, match.Groups["object"].Value, line));
                }

                match = match.NextMatch();
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return frames.AsReadOnly();
        }

        return frames.Count == 0 ? Array.Empty<(string Schema, string Package, int Line)>() : frames.AsReadOnly();
    }

    /// <summary>Returns, in message order, every trigger named by an ORA-04088 line; on a match timeout, the triggers found before it.</summary>
    /// <param name="message">Full error message.</param>
    /// <returns>The trigger names; Schema is empty when the name has no schema part.</returns>
    internal static IReadOnlyList<(string Schema, string Name)> ParseTriggers(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var triggers = new List<(string Schema, string Name)>();
        try
        {
            Match match = TriggerRegex().Match(message);
            while (match.Success)
            {
                Group schema = match.Groups["schema"];
                triggers.Add((schema.Success ? schema.Value : string.Empty, match.Groups["object"].Value));
                match = match.NextMatch();
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return triggers.AsReadOnly();
        }

        return triggers.AsReadOnly();
    }

    /// <summary>First "ORA-" followed by a five-digit code anywhere in the message.</summary>
    [GeneratedRegex("ORA-(?<code>[0-9]{5})", ParseOptions)]
    private static partial Regex OraCodeRegex();

    /// <summary>An ORA-06512 frame with a quoted, optionally schema-qualified object name on one line, each name part at most 128 characters, and a line number.</summary>
    [GeneratedRegex(
        "ORA-06512: at \"(?:(?<schema>[^\".\\r\\n]{1,128})\\.)?(?<object>[^\"\\r\\n]{1,128})\", line (?<line>[0-9]+)",
        ParseOptions,
        matchTimeoutMilliseconds: FrameMatchTimeoutMilliseconds)]
    private static partial Regex FrameRegex();

    /// <summary>An ORA-04088 line with a quoted, optionally schema-qualified trigger name on one line, each name part at most 128 characters.</summary>
    [GeneratedRegex(
        "ORA-04088: error during execution of trigger '(?:(?<schema>[^'.\\r\\n]{1,128})\\.)?(?<object>[^'\\r\\n]{1,128})'",
        ParseOptions,
        matchTimeoutMilliseconds: FrameMatchTimeoutMilliseconds)]
    private static partial Regex TriggerRegex();
}
