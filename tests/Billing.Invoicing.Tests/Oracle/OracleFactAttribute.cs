using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Ports;
using Oracle.ManagedDataAccess.Client;
using Xunit.Sdk;

namespace Billing.Invoicing.Tests.Oracle;

/// <summary>xUnit fact skipped at discovery unless an Oracle test connection is set and no pending-evidence or isolation gate applies; also checks a create call's session against the isolation clearance.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class OracleFactAttribute : FactAttribute
{
    /// <summary>Name of the environment variable holding the Oracle test connection string.</summary>
    public const string ConnectionVariable = "ORACLE_TEST_CONNECTION";

    /// <summary>Side-effect class of tests that reach the invoice create path and its posting packages.</summary>
    public const string CreateSideEffects = "Create";

    private const string SolutionFileName = "SmallCashInvoice.sln";
    private const string IsolationStatusPrefix = "Isolation status:";
    private const string IsolationTargetServicePrefix = "Isolation target service:";
    private const string IsolationSectionHeading = "## §4";
    private const string SectionHeadingPrefix = "## ";
    private const string NotInspectedPrefix = "Not inspected";
    private const string ProtectedServicePrefix = "SYS$";
    private const string ClearanceDateFormat = "yyyy-MM-dd";
    private const int InspectionCellCount = 4;

    private const string TargetIdentitySql =
        "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA'), SYS_CONTEXT('USERENV', 'SERVICE_NAME') FROM DUAL";

    private const string NoConnectionReason =
        "ORACLE_TEST_CONNECTION is not set; Oracle integration parity test skipped (UNVERIFIED).";

    private const string IsolationNotClearedReason =
        "Create-path Oracle test skipped: docs/dependency-open-items.md §4 does not record an exact isolation clearance (status, target service and recorded inspection) (UNVERIFIED).";

    private static readonly char[] LeadingDecoration = ['>', '-', '*', '`', '_'];

    private static readonly string[] InspectedPackages =
        ["BIL_PAYMENT", "BIL_QUEUE_POSTING", "BIL_STOCK_POSTING", "BIL_AUDIT", "BIL_MESSAGE", "BIL_REPORTS_PRINT"];

    private static readonly string[] ProtectedSchemas = ["SYS", "SYSTEM"];

    private static readonly Regex ClearedStatusPattern = new(
        @"^Isolation status: Cleared \((?<date>[0-9]{4}-[0-9]{2}-[0-9]{2}), (?<schema>[A-Za-z][A-Za-z0-9_$#]{0,127})\)\z",
        RegexOptions.CultureInvariant);

    private static readonly Regex TargetServicePattern = new(
        @"^Isolation target service: (?<service>[A-Za-z][A-Za-z0-9_.$#-]{0,254})\z",
        RegexOptions.CultureInvariant);

    /// <summary>Oracle test connection string from <see cref="ConnectionVariable"/>, or null when it is unset or blank.</summary>
    public static string? ConnectionString
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(ConnectionVariable);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    /// <summary>Open-item id (for example OI-06) whose missing evidence keeps the test skipped.</summary>
    public string? PendingOn { get; set; }

    /// <summary>Side-effect class of the test; <see cref="CreateSideEffects"/> gates it on the recorded isolation clearance.</summary>
    public string? SideEffects { get; set; }

    /// <summary>First applicable skip reason: no connection, pending evidence, no exact create-path isolation clearance; otherwise the assigned reason, null when none.</summary>
    public override string? Skip
    {
        get
        {
            if (ConnectionString is null)
            {
                return NoConnectionReason;
            }

            if (!string.IsNullOrWhiteSpace(PendingOn))
            {
                return $"Expected value pending evidence ({PendingOn}); UNVERIFIED.";
            }

            if (string.Equals(SideEffects, CreateSideEffects, StringComparison.Ordinal) && ReadClearance() is null)
            {
                return IsolationNotClearedReason;
            }

            return base.Skip;
        }

        set => base.Skip = value;
    }

    /// <summary>Reads docs/dependency-open-items.md afresh and returns its exact isolation clearance, or null when it records none or cannot be read.</summary>
    public static IsolationClearance? ReadClearance()
    {
        try
        {
            var root = FindRepositoryRoot();
            if (root is null)
            {
                return null;
            }

            var path = Path.Combine(root, "docs", "dependency-open-items.md");
            return ParseClearance(File.ReadAllLines(path, Encoding.UTF8));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Returns the clearance the open-item register lines record, or null when the status line, the target service line or the §4 inspection is missing, repeated, malformed or names a SYS target.</summary>
    public static IsolationClearance? ParseClearance(IReadOnlyList<string> lines)
    {
        if (lines is null)
        {
            return null;
        }

        var status = SingleRecordLine(lines, IsolationStatusPrefix);
        var target = SingleRecordLine(lines, IsolationTargetServicePrefix);
        if (status is null || target is null)
        {
            return null;
        }

        var statusMatch = ClearedStatusPattern.Match(status);
        var targetMatch = TargetServicePattern.Match(target);
        if (!statusMatch.Success
            || !targetMatch.Success
            || !DateOnly.TryParseExact(
                statusMatch.Groups["date"].Value, ClearanceDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return null;
        }

        var schema = statusMatch.Groups["schema"].Value;
        var service = targetMatch.Groups["service"].Value;
        if (ProtectedSchemas.Contains(schema, StringComparer.OrdinalIgnoreCase)
            || service.StartsWith(ProtectedServicePrefix, StringComparison.OrdinalIgnoreCase)
            || !InspectionRecorded(lines))
        {
            return null;
        }

        return new IsolationClearance(date, schema, service);
    }

    /// <summary>Returns null when the session's schema equals the cleared schema exactly and its service equals the cleared service ignoring case; otherwise a reason naming the cleared value and withholding the connected one.</summary>
    public static string? TargetMismatch(IsolationClearance clearance, string? currentSchema, string? serviceName)
    {
        ArgumentNullException.ThrowIfNull(clearance);

        var reasons = new List<string>(2);
        if (string.IsNullOrEmpty(currentSchema))
        {
            reasons.Add($"the session returned no CURRENT_SCHEMA; the cleared schema is {clearance.Schema}");
        }
        else if (!string.Equals(currentSchema, clearance.Schema, StringComparison.Ordinal))
        {
            reasons.Add($"the session's CURRENT_SCHEMA differs from the cleared schema {clearance.Schema} (connected value withheld)");
        }

        if (string.IsNullOrEmpty(serviceName))
        {
            reasons.Add($"the session returned no SERVICE_NAME; the cleared service is {clearance.Service}");
        }
        else if (!string.Equals(serviceName, clearance.Service, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add($"the session's SERVICE_NAME differs from the cleared service {clearance.Service} (connected value withheld)");
        }

        return reasons.Count == 0 ? null : string.Join("; ", reasons);
    }

    /// <summary>Fails as a precondition, before a create call, unless the re-read clearance exists and matches the session's CURRENT_SCHEMA and SERVICE_NAME. UNVERIFIED against Oracle.</summary>
    public static async Task AssertCreateTargetCleared(IOracleSession session, InvoicingDataOptions options, string caller)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(caller);

        var clearance = ReadClearance()
            ?? throw Refusal(caller, "docs/dependency-open-items.md §4 does not record an exact isolation clearance");

        if (session is not OracleSession oracleSession)
        {
            throw Refusal(caller, "the session is not an OracleSession, so its schema and service cannot be read");
        }

        string? currentSchema;
        string? serviceName;
        try
        {
            await using var command = new OracleCommand(TargetIdentitySql, oracleSession.Connection)
            {
                Transaction = oracleSession.Transaction,
                CommandTimeout = options.CommandTimeoutSeconds,
            };
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                throw Refusal(caller, "the CURRENT_SCHEMA and SERVICE_NAME query returned no row");
            }

            currentSchema = reader.IsDBNull(0) ? null : reader.GetString(0);
            serviceName = reader.IsDBNull(1) ? null : reader.GetString(1);
        }
        catch (Exception exception) when (exception is not XunitException)
        {
            throw Refusal(caller, $"reading CURRENT_SCHEMA and SERVICE_NAME failed with {ErrorCategory(exception)}");
        }

        if (TargetMismatch(clearance, currentSchema, serviceName) is { } mismatch)
        {
            throw Refusal(caller, mismatch);
        }
    }

    /// <summary>Builds the precondition failure that refuses a create call.</summary>
    private static FailException Refusal(string caller, string reason) =>
        FailException.ForFailure($"Precondition: {caller}: {reason}; the create call was not made (UNVERIFIED).");

    /// <summary>Names an exception by its type and, for an <see cref="OracleException"/>, its ORA number only.</summary>
    private static string ErrorCategory(Exception exception) => exception is OracleException oracle
        ? "OracleException ORA-" + oracle.Number.ToString("D5", CultureInfo.InvariantCulture)
        : exception.GetType().Name;

    /// <summary>Returns the trimmed raw text of the only line whose normalised text starts with the prefix, or null when none or several do.</summary>
    private static string? SingleRecordLine(IReadOnlyList<string> lines, string prefix)
    {
        string? found = null;
        foreach (var line in lines)
        {
            if (line is null || !NormaliseStatusLine(line).StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (found is not null)
            {
                return null;
            }

            found = line.Trim();
        }

        return found;
    }

    /// <summary>True when the §4 table holds each inspected package exactly once, with four cells each filled and none reading Not inspected.</summary>
    private static bool InspectionRecorded(IReadOnlyList<string> lines)
    {
        var start = -1;
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index] is { } heading && heading.StartsWith(IsolationSectionHeading, StringComparison.Ordinal))
            {
                start = index + 1;
                break;
            }
        }

        if (start < 0)
        {
            return false;
        }

        var rows = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = start; index < lines.Count; index++)
        {
            var line = lines[index] ?? string.Empty;
            if (line.StartsWith(SectionHeadingPrefix, StringComparison.Ordinal))
            {
                break;
            }

            var cells = TableCells(line);
            if (cells is null)
            {
                continue;
            }

            var package = cells[0];
            if (!InspectedPackages.Contains(package, StringComparer.Ordinal))
            {
                continue;
            }

            if (cells.Count != InspectionCellCount + 1
                || cells.Skip(1).Any(cell => cell.Length == 0 || cell.StartsWith(NotInspectedPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            rows[package] = rows.GetValueOrDefault(package) + 1;
        }

        return InspectedPackages.All(package => rows.GetValueOrDefault(package) == 1);
    }

    /// <summary>Splits a Markdown table row into its cells, each without Markdown emphasis or code markers, or returns null when the line is not a table row.</summary>
    private static List<string>? TableCells(string line)
    {
        var text = line.Trim();
        if (!text.StartsWith('|'))
        {
            return null;
        }

        text = text[1..];
        if (text.EndsWith('|'))
        {
            text = text[..^1];
        }

        return text.Split('|').Select(cell => NormaliseStatusLine(cell).Trim()).ToList();
    }

    /// <summary>Strips leading whitespace and Markdown decoration, then removes every remaining '*' and '`'.</summary>
    /// <param name="line">One line of the open-item register.</param>
    /// <returns>The line text without Markdown emphasis, quote, list or code markers.</returns>
    private static string NormaliseStatusLine(string line)
    {
        var start = 0;
        while (start < line.Length
            && (char.IsWhiteSpace(line[start]) || Array.IndexOf(LeadingDecoration, line[start]) >= 0))
        {
            start++;
        }

        var builder = new StringBuilder(line.Length - start);
        for (var index = start; index < line.Length; index++)
        {
            var character = line[index];
            if (character is not ('*' or '`'))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    /// <summary>Returns the nearest directory at or above the test output directory that holds the solution file.</summary>
    /// <returns>The repository root path, or null when no such directory exists.</returns>
    private static string? FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    /// <summary>Isolation clearance recorded in §4: its date, the cleared schema and the cleared service.</summary>
    public sealed record IsolationClearance(DateOnly Date, string Schema, string Service);
}
