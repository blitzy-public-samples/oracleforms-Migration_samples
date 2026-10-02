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
    private const string ProhibitedPhrase = CreateWord + " " + TableWord;

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

    /// <summary>Fails when a root build file, scanned root directory or scanned file is a link, or a scanned file holds schema-changing SQL, listing each by path; linked subdirectories are skipped unread.</summary>
    [Fact]
    public void GeneratedSource_ContainsNoDdl()
    {
        var root = FindRepositoryRoot();
        var scan = Scan(root);

        Assert.True(
            scan.ScannedPaths.Any(path => path.StartsWith("src/", StringComparison.Ordinal)),
            $"No file was scanned under {Path.Combine(root, "src")}.");
        Assert.True(
            scan.ScannedPaths.Any(path => path.StartsWith("tests/", StringComparison.Ordinal)),
            $"No file was scanned under {Path.Combine(root, "tests")}.");
        Assert.True(
            scan.ScannedPaths.Contains(SelfRelativePath, StringComparer.Ordinal),
            $"The scanned set does not include {SelfRelativePath}.");
        Assert.True(
            scan.RejectedLinks.Count == 0,
            $"{scan.RejectedLinks.Count} linked path(s) rejected; nothing was read through them:{Environment.NewLine}"
            + string.Join(Environment.NewLine, scan.RejectedLinks));
        Assert.True(
            scan.Hits.Count == 0,
            $"{scan.Hits.Count} schema-changing SQL statement(s) found in generated source:{Environment.NewLine}"
            + string.Join(Environment.NewLine, scan.Hits));
    }

    /// <summary>Asserts linked files and root build files are rejected unread and linked subdirectories are skipped.</summary>
    [Fact]
    public void Scan_RejectsLinkedFilesAndSkipsLinkedSubdirectories()
    {
        var baseDir = CreateTempBase();
        try
        {
            var root = Path.Combine(baseDir, "repo");
            var outside = Path.Combine(baseDir, "outside");
            var outsideFile = Path.Combine(outside, "x.cs");
            var missingFile = Path.Combine(outside, "missing.cs");
            Directory.CreateDirectory(Path.Combine(root, "src"));
            Directory.CreateDirectory(Path.Combine(root, "tests"));
            Directory.CreateDirectory(Path.Combine(outside, "linked"));
            File.WriteAllText(outsideFile, ProhibitedPhrase);
            File.WriteAllText(Path.Combine(outside, "linked", "y.cs"), ProhibitedPhrase);
            File.WriteAllText(Path.Combine(root, "src", "control.cs"), "namespace Control;\n" + ProhibitedPhrase);
            File.WriteAllText(Path.Combine(root, "tests", "clean.json"), "{}");
            File.CreateSymbolicLink(Path.Combine(root, "Directory.Build.props"), outsideFile);
            File.CreateSymbolicLink(Path.Combine(root, "src", "absolute.cs"), outsideFile);
            File.CreateSymbolicLink(Path.Combine(root, "src", "relative.cs"), "../../outside/x.cs");
            File.CreateSymbolicLink(Path.Combine(root, "src", "broken.cs"), missingFile);
            Directory.CreateSymbolicLink(Path.Combine(root, "src", "linked"), Path.Combine(outside, "linked"));

            var scan = Scan(root);

            Assert.Equal(
                new[]
                {
                    $"Directory.Build.props -> {outsideFile}",
                    $"src/absolute.cs -> {outsideFile}",
                    $"src/broken.cs -> {missingFile}",
                    "src/relative.cs -> ../../outside/x.cs",
                },
                scan.RejectedLinks);
            Assert.Equal(new[] { $"src/control.cs:2: {ProhibitedPhrase}" }, scan.Hits);
            Assert.Equal(new[] { "src/control.cs", "tests/clean.json" }, scan.ScannedPaths);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    /// <summary>Asserts a linked start directory is rejected and nothing below it is scanned.</summary>
    [Fact]
    public void Scan_RejectsLinkedStartDirectory()
    {
        var baseDir = CreateTempBase();
        try
        {
            var root = Path.Combine(baseDir, "repo");
            var outside = Path.Combine(baseDir, "outside");
            Directory.CreateDirectory(Path.Combine(root, "src"));
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(root, "src", "clean.cs"), "namespace Clean;");
            File.WriteAllText(Path.Combine(outside, "z.cs"), ProhibitedPhrase);
            Directory.CreateSymbolicLink(Path.Combine(root, "tests"), outside);

            var scan = Scan(root);

            Assert.Equal(new[] { $"tests -> {outside}" }, scan.RejectedLinks);
            Assert.Equal(new[] { "src/clean.cs" }, scan.ScannedPaths);
            Assert.Empty(scan.Hits);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    /// <summary>Scans the root build files and scanned root directories below <paramref name="root"/>, reading no linked entry.</summary>
    /// <param name="root">Repository root.</param>
    private static ScanResult Scan(string root)
    {
        var rejectedLinks = new List<string>();
        var files = EnumerateScannedFiles(root, rejectedLinks);
        var scannedPaths = files.Select(file => ToRelativePath(root, file)).ToList();

        var hits = new List<string>();
        for (var index = 0; index < files.Count; index++)
        {
            hits.AddRange(FindHits(scannedPaths[index], File.ReadAllText(files[index])));
        }

        rejectedLinks.Sort(StringComparer.Ordinal);
        return new ScanResult(scannedPaths, hits, rejectedLinks);
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

    /// <summary>Lists the unlinked root build files present and every unlinked scanned file below the unlinked scanned root directories, sorted.</summary>
    /// <param name="root">Repository root.</param>
    /// <param name="rejectedLinks">Receives each linked root build file, root directory or scanned file as "path -> target".</param>
    private static List<string> EnumerateScannedFiles(string root, List<string> rejectedLinks)
    {
        var files = new List<string>();

        foreach (var name in ScannedRootFiles)
        {
            var file = new FileInfo(Path.Combine(root, name));
            if (file.LinkTarget is not null)
            {
                rejectedLinks.Add(DescribeLink(root, file));
            }
            else if (file.Exists)
            {
                files.Add(file.FullName);
            }
        }

        foreach (var name in ScannedRootDirectories)
        {
            var directory = new DirectoryInfo(Path.Combine(root, name));
            if (directory.LinkTarget is not null)
            {
                rejectedLinks.Add(DescribeLink(root, directory));
            }
            else if (directory.Exists)
            {
                CollectFiles(root, directory.FullName, files, rejectedLinks);
            }
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    /// <summary>Adds unlinked files with a scanned extension below <paramref name="start"/>, never entering skipped or linked directories.</summary>
    /// <param name="root">Repository root.</param>
    /// <param name="start">Directory to walk.</param>
    /// <param name="files">Receives the full paths found.</param>
    /// <param name="rejectedLinks">Receives each linked file with a scanned extension as "path -> target".</param>
    private static void CollectFiles(string root, string start, List<string> files, List<string> rejectedLinks)
    {
        var pending = new Stack<string>();
        pending.Push(start);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            foreach (var path in Directory.EnumerateFiles(current))
            {
                if (!ScannedExtensions.Contains(Path.GetExtension(path)))
                {
                    continue;
                }

                var file = new FileInfo(path);
                if (file.LinkTarget is null)
                {
                    files.Add(path);
                }
                else
                {
                    rejectedLinks.Add(DescribeLink(root, file));
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

    /// <summary>Returns "relative/path -> target" for a link, read from the link itself and never from its target.</summary>
    /// <param name="root">Repository root.</param>
    /// <param name="link">Linked file or directory below the root.</param>
    private static string DescribeLink(string root, FileSystemInfo link) =>
        $"{ToRelativePath(root, link.FullName)} -> {link.LinkTarget}";

    /// <summary>Creates and returns a new, uniquely named directory under the system temp path.</summary>
    private static string CreateTempBase() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "noddl-scan-" + Guid.NewGuid().ToString("N"))).FullName;

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

    /// <summary>Outcome of one scan.</summary>
    /// <param name="ScannedPaths">Repository-relative paths of the files read, sorted.</param>
    /// <param name="Hits">One "path:line: text" entry per match.</param>
    /// <param name="RejectedLinks">One "path -> target" entry per linked entry left unread, sorted.</param>
    private sealed record ScanResult(
        IReadOnlyList<string> ScannedPaths,
        IReadOnlyList<string> Hits,
        IReadOnlyList<string> RejectedLinks);
}
