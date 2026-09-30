using System.Text;

namespace Billing.Invoicing.Tests.Oracle;

/// <summary>xUnit fact that is skipped at discovery unless an Oracle test connection is set and no pending-evidence or isolation gate applies.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class OracleFactAttribute : FactAttribute
{
    /// <summary>Name of the environment variable holding the Oracle test connection string.</summary>
    public const string ConnectionVariable = "ORACLE_TEST_CONNECTION";

    /// <summary>Side-effect class of tests that reach the invoice create path and its posting packages.</summary>
    public const string CreateSideEffects = "Create";

    private const string SolutionFileName = "SmallCashInvoice.sln";
    private const string IsolationStatusPrefix = "Isolation status:";
    private const string IsolationClearedPrefix = "Isolation status: Cleared";

    private const string NoConnectionReason =
        "ORACLE_TEST_CONNECTION is not set; Oracle integration parity test skipped (UNVERIFIED).";

    private const string IsolationNotClearedReason =
        "Create-path Oracle test skipped: docs/dependency-open-items.md §4 does not record 'Isolation status: Cleared' (UNVERIFIED).";

    private static readonly char[] LeadingDecoration = ['>', '-', '*', '`', '_'];

    private static readonly Lazy<bool> Cleared = new(IsolationCleared, LazyThreadSafetyMode.ExecutionAndPublication);

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

    /// <summary>Side-effect class of the test; <see cref="CreateSideEffects"/> gates it on the recorded isolation status.</summary>
    public string? SideEffects { get; set; }

    /// <summary>First applicable skip reason: no connection, pending evidence, uncleared create-path isolation; otherwise the assigned reason, null when none.</summary>
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

            if (string.Equals(SideEffects, CreateSideEffects, StringComparison.Ordinal) && !Cleared.Value)
            {
                return IsolationNotClearedReason;
            }

            return base.Skip;
        }

        set => base.Skip = value;
    }

    /// <summary>Reads docs/dependency-open-items.md and reports whether its first isolation status line reads Cleared.</summary>
    /// <returns>True only when the first status line starts with "Isolation status: Cleared"; false when it does not, when no such line exists, or on any read failure.</returns>
    private static bool IsolationCleared()
    {
        try
        {
            var root = FindRepositoryRoot();
            if (root is null)
            {
                return false;
            }

            var path = Path.Combine(root, "docs", "dependency-open-items.md");
            foreach (var line in File.ReadLines(path, Encoding.UTF8))
            {
                var text = NormaliseStatusLine(line);
                if (text.StartsWith(IsolationStatusPrefix, StringComparison.Ordinal))
                {
                    return text.StartsWith(IsolationClearedPrefix, StringComparison.Ordinal);
                }
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
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
}
