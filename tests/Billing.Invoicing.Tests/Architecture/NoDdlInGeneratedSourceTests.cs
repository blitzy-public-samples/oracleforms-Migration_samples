using System.Text.RegularExpressions;

namespace Billing.Invoicing.Tests.Architecture;

/// <summary>Scans generated source and root build files for schema-changing SQL.</summary>
[Trait("Category", "Compliance")]
public sealed class NoDdlInGeneratedSourceTests
{
    private const string SolutionFileName = "SmallCashInvoice.sln";
    private const string SelfRelativePath = "tests/Billing.Invoicing.Tests/Architecture/NoDdlInGeneratedSourceTests.cs";

    private const string CreateWord = "cre" + "ate";
    private const string OrWord = "o" + "r";
    private const string ReplaceWord = "rep" + "lace";
    private const string AlterWord = "al" + "ter";
    private const string DropWord = "dr" + "op";
    private const string TruncateWord = "trun" + "cate";
    private const string ExecuteWord = "exe" + "cute";
    private const string ImmediateWord = "imme" + "diate";
    private const string TableWord = "ta" + "ble";
    private const string Gap = @"\s+";

    private static readonly string[] ObjectWords =
    [
        TableWord,
        "vi" + "ew",
        "seq" + "uence",
        "trig" + "ger",
        "pack" + "age",
        "proce" + "dure",
        "func" + "tion",
        "ty" + "pe",
        "in" + "dex",
        "syn" + "onym",
    ];

    private static readonly Regex DdlPattern = new(
        string.Join(
            "|",
            CreateWord + Gap + OrWord + Gap + ReplaceWord,
            "(?:" + CreateWord + "|" + AlterWord + "|" + DropWord + ")" + Gap + "(?:" + string.Join("|", ObjectWords) + ")",
            TruncateWord + Gap + TableWord,
            ExecuteWord + Gap + ImmediateWord),
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);

    private static readonly HashSet<string> ScannedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".ts",
        ".tsx",
        ".json",
        ".props",
        ".csproj",
        ".sln",
    };

    private static readonly HashSet<string> SkippedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        "node_modules",
        "wwwroot",
    };

    private static readonly string[] ScannedRootDirectories = ["src", "tests"];

    private static readonly string[] ScannedRootFiles =
    [
        SolutionFileName,
        "Directory.Build.props",
        "Directory.Packages.props",
        "global.json",
    ];

    /// <summary>Fails when any scanned file holds schema-changing SQL, listing every hit by file and line.</summary>
    [Fact]
    public void GeneratedSource_ContainsNoDdl()
    {
        var root = FindRepositoryRoot();
        var files = EnumerateScannedFiles(root);
        var relativePaths = files.Select(file => ToRelativePath(root, file)).ToList();

        Assert.True(
            relativePaths.Any(path => path.StartsWith("src/", StringComparison.Ordinal)),
            $"No file was scanned under {Path.Combine(root, "src")}.");
        Assert.True(
            relativePaths.Any(path => path.StartsWith("tests/", StringComparison.Ordinal)),
            $"No file was scanned under {Path.Combine(root, "tests")}.");
        Assert.True(
            relativePaths.Contains(SelfRelativePath, StringComparer.Ordinal),
            $"The scanned set does not include {SelfRelativePath}.");

        var hits = new List<string>();
        for (var index = 0; index < files.Count; index++)
        {
            hits.AddRange(FindHits(relativePaths[index], File.ReadAllText(files[index])));
        }

        Assert.True(
            hits.Count == 0,
            $"{hits.Count} schema-changing SQL statement(s) found in generated source:{Environment.NewLine}"
            + string.Join(Environment.NewLine, hits));
    }

    /// <summary>Returns one "path:line: text" entry per pattern match in <paramref name="text"/>.</summary>
    /// <param name="relativePath">Repository-relative path of the scanned file, with '/' separators.</param>
    /// <param name="text">Full file content.</param>
    private static IEnumerable<string> FindHits(string relativePath, string text)
    {
        foreach (Match match in DdlPattern.Matches(text))
        {
            var line = text.AsSpan(0, match.Index).Count('\n') + 1;
            yield return $"{relativePath}:{line}: {Whitespace.Replace(match.Value, " ")}";
        }
    }

    /// <summary>Lists the root build files present and every scanned file below the scanned root directories, sorted.</summary>
    /// <param name="root">Repository root.</param>
    private static List<string> EnumerateScannedFiles(string root)
    {
        var files = new List<string>();

        foreach (var name in ScannedRootFiles)
        {
            var path = Path.Combine(root, name);
            if (File.Exists(path))
            {
                files.Add(path);
            }
        }

        foreach (var name in ScannedRootDirectories)
        {
            var directory = Path.Combine(root, name);
            if (Directory.Exists(directory))
            {
                CollectFiles(directory, files);
            }
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    /// <summary>Adds files with a scanned extension below <paramref name="start"/>, never entering skipped or linked directories.</summary>
    /// <param name="start">Directory to walk.</param>
    /// <param name="files">Receives the full paths found.</param>
    private static void CollectFiles(string start, List<string> files)
    {
        var pending = new Stack<string>();
        pending.Push(start);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            foreach (var file in Directory.EnumerateFiles(current))
            {
                if (ScannedExtensions.Contains(Path.GetExtension(file)))
                {
                    files.Add(file);
                }
            }

            foreach (var child in Directory.EnumerateDirectories(current))
            {
                var info = new DirectoryInfo(child);
                if (!SkippedDirectoryNames.Contains(info.Name) && info.LinkTarget is null)
                {
                    pending.Push(child);
                }
            }
        }
    }

    /// <summary>Converts a full path to a repository-relative path with '/' separators.</summary>
    /// <param name="root">Repository root.</param>
    /// <param name="path">Full path below the root.</param>
    private static string ToRelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    /// <summary>Returns the nearest directory at or above the test output directory that holds the solution file.</summary>
    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"No directory containing {SolutionFileName} was found at or above {AppContext.BaseDirectory}.");
    }
}
