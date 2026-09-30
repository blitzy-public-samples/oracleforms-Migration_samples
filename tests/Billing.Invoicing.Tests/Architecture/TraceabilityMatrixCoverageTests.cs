using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Billing.Invoicing.Tests.Oracle;

namespace Billing.Invoicing.Tests.Architecture;

/// <summary>Coverage checks of the bidirectional traceability matrix in docs/legacy-form-spec.md §9 and of the rule evidence it relies on.</summary>
[Trait("Category", "Compliance")]
public sealed class TraceabilityMatrixCoverageTests
{
    private const string SolutionFileName = "SmallCashInvoice.sln";
    private const string FormsNamespace = "http://xmlns.oracle.com/Forms";
    private const string FormModuleElement = "FormModule";
    private const string NameAttribute = "Name";
    private const string FormOwner = "FORM";
    private const string NoEvent = "-";
    private const string BlockKind = "Block";
    private const string ItemKind = "Item";
    private const string RelationKind = "Relation";
    private const string TriggerKind = "Trigger";
    private const int ExpectedKeyCount = 413;

    private const string KeyHeader = "Key";
    private const string CodesHeader = "Code";
    private const string SourceHeader = "Source";
    private const string ForwardRegionWord = "Forward";
    private const string ReverseRegionWord = "Reverse";
    private const string InfrastructurePrefix = "Infrastructure:";
    private const string NotMigratedCode = "N";
    private const string OpenItemCodePrefix = "OI-";

    private const string RuleTrait = "Rule";
    private const string SideEffectsTrait = "SideEffects";
    private const string DomainRulePrefix = "DR";
    private const string PackageRulePrefix = "PR";
    private const int RuleCount = 25;
    private const int CreatePathSmokeTestCount = 1;

    private const int ListingCap = 50;

    private const BindingFlags AllDeclaredMethods =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly HashSet<string> FormLevelKinds = new(StringComparer.Ordinal)
    {
        "Block", "Canvas", "Window", "Report", "Alert", "LOV", "RecordGroup", "ModuleParameter", "VisualAttribute", "ProgramUnit",
    };

    private static readonly IReadOnlyDictionary<string, int> ExpectedKindCounts = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["Block"] = 5,
        ["Canvas"] = 2,
        ["Window"] = 1,
        ["Relation"] = 2,
        ["Report"] = 1,
        ["Alert"] = 5,
        ["Trigger"] = 95,
        ["ProgramUnit"] = 30,
        ["LOV"] = 15,
        ["RecordGroup"] = 16,
        ["ModuleParameter"] = 32,
        ["VisualAttribute"] = 7,
        ["Item"] = 202,
    };

    private static readonly string[] CreatePathPackageRules =
        ["PR-09", "PR-10", "PR-11", "PR-12", "PR-20", "PR-22", "PR-23", "PR-25"];

    private static readonly Regex UnescapedPipe = new(@"(?<!\\)\|", RegexOptions.CultureInvariant);
    private static readonly Regex SeparatorCell = new(@"^:?-{3,}:?$", RegexOptions.CultureInvariant);
    private static readonly Regex HeadingLine = new(@"^(#{1,6})\s", RegexOptions.CultureInvariant);
    private static readonly Regex MatrixCode = new(@"^(DR-[0-9]{2}|PR-[0-9]{2}|OI-[0-9]{2}(\.[0-9]{2})?|N)$", RegexOptions.CultureInvariant);
    private static readonly Regex ConstructIdToken = new(@"\b(T[0-9]{3}|PU[0-9]{2}|DR-[0-9]{2}|PR-[0-9]{2}|OI-[0-9]{2})\b", RegexOptions.CultureInvariant);

    private static readonly Lazy<string> RepositoryRoot = new(FindRepositoryRoot);
    private static readonly Lazy<FormInventory> Inventory = new(LoadFormInventory);
    private static readonly Lazy<MatrixSection> Section9 = new(ReadSection9);
    private static readonly Lazy<Regex> ForwardSegment = new(BuildForwardSegmentPattern);
    private static readonly Lazy<IReadOnlyList<TestMethodRecord>> TestMethods = new(DiscoverTestMethods);

    /// <summary>Asserts the XML key walk yields 413 distinct, named keys with the per-kind construct counts and reaches every trigger and item.</summary>
    [Fact]
    public void ForwardKeys_MatchConstructInventory()
    {
        var inventory = Inventory.Value;

        var actualCounts = inventory.Keys
            .GroupBy(key => key.Kind, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var countMismatches = ExpectedKindCounts
            .Where(expected => actualCounts.GetValueOrDefault(expected.Key) != expected.Value)
            .Select(expected => $"{expected.Key}: expected {expected.Value}, found {actualCounts.GetValueOrDefault(expected.Key)}")
            .ToList();

        var totalMismatch = inventory.Keys.Count == ExpectedKeyCount
            ? []
            : new List<string> { $"expected {ExpectedKeyCount} keys, found {inventory.Keys.Count}" };

        var duplicates = inventory.Keys
            .GroupBy(key => key.Text, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} ({group.Count()} times)")
            .ToList();

        var unnamed = inventory.Keys
            .Where(key => key.Name.Length == 0 || (key.Kind == TriggerKind && key.Event.Length == 0))
            .Select(key => key.Text)
            .ToList();

        var unvisited = new List<string>();
        if (inventory.TriggerDescendants != ExpectedKindCounts[TriggerKind])
        {
            unvisited.Add($"Trigger elements in the document: expected {ExpectedKindCounts[TriggerKind]}, found {inventory.TriggerDescendants}");
        }

        if (inventory.ItemDescendants != ExpectedKindCounts[ItemKind])
        {
            unvisited.Add($"Item elements in the document: expected {ExpectedKindCounts[ItemKind]}, found {inventory.ItemDescendants}");
        }

        AssertNoProblems(
            "Forward key inventory of 05_Complex/Inv_Small_Cash.xml",
            ("Per-kind count mismatches", countMismatches),
            ("Total key count mismatch", totalMismatch),
            ("Duplicated keys", duplicates),
            ("Keys with an empty name or trigger event", unnamed),
            ("Document totals the key walk does not reach", unvisited));
    }

    /// <summary>Asserts §9.1 holds exactly one row per forward key, each with valid codes and, for N or OI codes, a reason.</summary>
    [Fact]
    public void ForwardHalf_CoversEveryConstructExactlyOnce()
    {
        var expectedKeys = Inventory.Value.Keys.Select(key => key.Text).ToList();
        var expectedSet = expectedKeys.ToHashSet(StringComparer.Ordinal);
        var rows = Section9.Value.Forward;

        // Columns are located by header name, falling back to position 0 for the key and 1 for the codes.
        var entries = rows
            .Select(row =>
            {
                var codesColumn = row.Column(CodesHeader, 1);
                return (Row: row, Key: row.Cell(row.Column(KeyHeader, 0)), Codes: row.Cell(codesColumn), Target: row.CellsFrom(codesColumn + 1));
            })
            .ToList();

        var rowKeySet = entries.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);

        var omitted = expectedKeys.Where(key => !rowKeySet.Contains(key)).ToList();

        var extra = entries
            .Where(entry => !expectedSet.Contains(entry.Key))
            .Select(entry => $"line {entry.Row.LineNumber}: '{entry.Key}'")
            .ToList();

        var duplicates = DuplicateKeys(entries.Select(entry => (entry.Key, entry.Row.LineNumber)));

        var invalidCodes = new List<string>();
        var missingReasons = new List<string>();
        foreach (var entry in entries)
        {
            var codes = entry.Codes.Split(',', StringSplitOptions.TrimEntries);
            if (entry.Codes.Length == 0 || !codes.All(code => MatrixCode.IsMatch(code)))
            {
                invalidCodes.Add($"line {entry.Row.LineNumber}: '{entry.Key}' codes '{entry.Codes}'");
                continue;
            }

            var needsReason = codes.Any(code =>
                code == NotMigratedCode || code.StartsWith(OpenItemCodePrefix, StringComparison.Ordinal));
            if (needsReason && entry.Target.Length == 0)
            {
                missingReasons.Add($"line {entry.Row.LineNumber}: '{entry.Key}' codes '{entry.Codes}'");
            }
        }

        AssertNoProblems(
            "docs/legacy-form-spec.md §9 forward half",
            ("Generated keys with no row", omitted),
            ("Rows whose key is not generated", extra),
            ("Keys on more than one row", duplicates),
            ("Rows without a valid DR / PR / OI / N code", invalidCodes),
            ("N or OI rows without a target or reason", missingReasons));
    }

    /// <summary>Asserts §9.2 holds exactly one row per generated reverse key, each naming a source construct or an Infrastructure reason.</summary>
    [Fact]
    public void ReverseHalf_CoversEveryGeneratedKeyExactlyOnce()
    {
        var expectedSet = ReverseMatrixKeys.Generate();
        var rows = Section9.Value.Reverse;

        var entries = rows
            .Select(row => (Row: row, Key: row.Cell(row.Column(KeyHeader, 0)), Source: row.Cell(row.Column(SourceHeader, 1))))
            .ToList();

        var rowKeySet = entries.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);

        var omitted = expectedSet.Where(key => !rowKeySet.Contains(key)).ToList();

        var extra = entries
            .Where(entry => !expectedSet.Contains(entry.Key))
            .Select(entry => $"line {entry.Row.LineNumber}: '{entry.Key}'")
            .ToList();

        var duplicates = DuplicateKeys(entries.Select(entry => (entry.Key, entry.Row.LineNumber)));

        var unsourced = entries
            .Where(entry => !IsInfrastructureReason(entry.Source) && !NamesSourceConstruct(entry.Source))
            .Select(entry => $"line {entry.Row.LineNumber}: '{entry.Key}' source '{entry.Source}'")
            .ToList();

        AssertNoProblems(
            "docs/legacy-form-spec.md §9 reverse half",
            ("Generated keys with no row", omitted),
            ("Rows whose key is not generated", extra),
            ("Keys on more than one row", duplicates),
            ("Rows with neither a source construct nor an Infrastructure: reason", unsourced));
    }

    /// <summary>Asserts every domain rule id has a parity fixture and at least one test carrying its Rule trait.</summary>
    [Fact]
    public void EveryDomainRule_HasFixtureAndRuleTest()
    {
        var missing = new List<string>();
        foreach (var id in RuleIds(DomainRulePrefix))
        {
            if (!File.Exists(FixturePath(id)))
            {
                missing.Add($"{id}: fixture {FixtureRelativePath(id)} not found");
            }

            if (!TestMethods.Value.Any(test => test.HasTrait(RuleTrait, id)))
            {
                missing.Add($"{id}: no test method carries [Trait(\"{RuleTrait}\", \"{id}\")]");
            }
        }

        AssertNoProblems("Domain rule evidence", ("Missing fixtures or rule tests", missing));
    }

    /// <summary>Asserts every package rule id has a parity fixture and at least one [OracleFact] test carrying its Rule trait.</summary>
    [Fact]
    public void EveryPackageRule_HasFixtureAndOracleFact()
    {
        var missing = new List<string>();
        foreach (var id in RuleIds(PackageRulePrefix))
        {
            if (!File.Exists(FixturePath(id)))
            {
                missing.Add($"{id}: fixture {FixtureRelativePath(id)} not found");
            }

            if (!TestMethods.Value.Any(test => test.IsOracleFact && test.HasTrait(RuleTrait, id)))
            {
                missing.Add($"{id}: no [OracleFact] test method carries [Trait(\"{RuleTrait}\", \"{id}\")]");
            }
        }

        AssertNoProblems("Package rule evidence", ("Missing fixtures or Oracle rule tests", missing));
    }

    /// <summary>Asserts the create-path SideEffects attribute property and trait mark the same tests, and only the expected package parity and binding smoke tests.</summary>
    [Fact]
    public void CreatePathTests_AttributeAndTraitAgree()
    {
        var tests = TestMethods.Value;

        var byAttribute = tests.Where(test => test.DeclaresCreateSideEffects).ToList();
        var byTrait = tests.Where(test => test.HasTrait(SideEffectsTrait, OracleFactAttribute.CreateSideEffects)).ToList();

        var attributeNames = byAttribute.Select(test => test.DisplayName).ToHashSet(StringComparer.Ordinal);
        var traitNames = byTrait.Select(test => test.DisplayName).ToHashSet(StringComparer.Ordinal);

        var attributeOnly = attributeNames.Where(name => !traitNames.Contains(name)).Order(StringComparer.Ordinal).ToList();
        var traitOnly = traitNames.Where(name => !attributeNames.Contains(name)).Order(StringComparer.Ordinal).ToList();

        var createPath = byAttribute.Concat(byTrait).DistinctBy(test => test.DisplayName, StringComparer.Ordinal).ToList();
        var packageTests = createPath.Where(test => test.DeclaringType == typeof(PackageParityTests)).ToList();
        var smokeTests = createPath.Where(test => test.DeclaringType == typeof(OracleBindingSmokeTests)).ToList();

        var otherTypes = createPath
            .Where(test => test.DeclaringType != typeof(PackageParityTests) && test.DeclaringType != typeof(OracleBindingSmokeTests))
            .Select(test => test.DisplayName)
            .Order(StringComparer.Ordinal)
            .ToList();

        var expectedRules = CreatePathPackageRules.ToHashSet(StringComparer.Ordinal);
        var packageRules = packageTests.SelectMany(test => test.TraitValues(RuleTrait)).ToHashSet(StringComparer.Ordinal);

        var shapeMismatches = new List<string>();
        var expectedTotal = CreatePathPackageRules.Length + CreatePathSmokeTestCount;
        if (createPath.Count != expectedTotal)
        {
            shapeMismatches.Add($"expected {expectedTotal} create-path tests, found {createPath.Count}");
        }

        if (packageTests.Count != CreatePathPackageRules.Length)
        {
            shapeMismatches.Add(
                $"{nameof(PackageParityTests)}: expected {CreatePathPackageRules.Length} create-path tests, found {packageTests.Count}");
        }

        if (!packageRules.SetEquals(expectedRules))
        {
            shapeMismatches.Add(
                $"{nameof(PackageParityTests)}: create-path Rule traits are [{string.Join(", ", packageRules.Order(StringComparer.Ordinal))}], "
                + $"expected [{string.Join(", ", CreatePathPackageRules)}]");
        }

        if (smokeTests.Count != CreatePathSmokeTestCount)
        {
            shapeMismatches.Add(
                $"{nameof(OracleBindingSmokeTests)}: expected {CreatePathSmokeTestCount} create-path test, found {smokeTests.Count}");
        }

        AssertNoProblems(
            $"Create-path tests ({nameof(OracleFactAttribute)}.{nameof(OracleFactAttribute.SideEffects)} and [Trait(\"{SideEffectsTrait}\", \"{OracleFactAttribute.CreateSideEffects}\")])",
            ("Attribute property without the trait", attributeOnly),
            ("Trait without the attribute property", traitOnly),
            ("Create-path tests declared on other types", otherTypes),
            ("Create-path set mismatches", shapeMismatches));
    }

    /// <summary>Returns the nearest directory at or above the test output directory that holds the solution file.</summary>
    /// <returns>The repository root path.</returns>
    private static string FindRepositoryRoot()
    {
        var start = AppContext.BaseDirectory;
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No directory containing {SolutionFileName} was found at or above {start}.");
    }

    /// <summary>Loads the Form XML export and builds its forward keys and document-wide trigger and item totals.</summary>
    /// <returns>The forward keys in document order with the descendant totals.</returns>
    private static FormInventory LoadFormInventory()
    {
        var path = Path.Combine(RepositoryRoot.Value, "05_Complex", "Inv_Small_Cash.xml");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Form XML export not found.", path);
        }

        var document = XDocument.Load(path, LoadOptions.SetLineInfo);
        XNamespace ns = FormsNamespace;
        var formModule = document.Root?.Element(ns + FormModuleElement)
            ?? throw new InvalidDataException($"{path}: no {FormModuleElement} element under the document root.");

        var keys = new List<ForwardKey>();
        foreach (var element in formModule.Elements())
        {
            if (element.Name.Namespace != ns)
            {
                continue;
            }

            var kind = element.Name.LocalName;
            if (FormLevelKinds.Contains(kind))
            {
                keys.Add(NewKey(kind, FormOwner, NameOf(element), NoEvent, element));
            }
            else if (kind == TriggerKind)
            {
                keys.Add(NewKey(TriggerKind, FormOwner, FormOwner, NameOf(element), element));
            }

            if (kind == BlockKind)
            {
                AddBlockKeys(element, ns, keys);
            }
        }

        return new FormInventory(
            keys,
            document.Descendants(ns + TriggerKind).Count(),
            document.Descendants(ns + ItemKind).Count());
    }

    /// <summary>Adds the relation, block-trigger, item and item-trigger keys of one block.</summary>
    /// <param name="block">Block element.</param>
    /// <param name="ns">Forms XML namespace.</param>
    /// <param name="keys">Receives the keys.</param>
    private static void AddBlockKeys(XElement block, XNamespace ns, List<ForwardKey> keys)
    {
        var blockName = NameOf(block);
        foreach (var child in block.Elements())
        {
            if (child.Name == ns + RelationKind)
            {
                keys.Add(NewKey(RelationKind, blockName, NameOf(child), NoEvent, child));
            }
            else if (child.Name == ns + TriggerKind)
            {
                keys.Add(NewKey(TriggerKind, blockName, blockName, NameOf(child), child));
            }
            else if (child.Name == ns + ItemKind)
            {
                var itemName = NameOf(child);
                keys.Add(NewKey(ItemKind, blockName, itemName, NoEvent, child));

                foreach (var trigger in child.Elements(ns + TriggerKind))
                {
                    keys.Add(NewKey(TriggerKind, blockName, itemName, NameOf(trigger), trigger));
                }
            }
        }
    }

    /// <summary>Returns an element's Name attribute, or an empty string when it has none.</summary>
    /// <param name="element">Forms XML element.</param>
    private static string NameOf(XElement element) => (string?)element.Attribute(NameAttribute) ?? string.Empty;

    /// <summary>Creates a forward key at the element's start-tag line.</summary>
    /// <param name="kind">Element kind.</param>
    /// <param name="owner">Enclosing block name, or FORM.</param>
    /// <param name="name">Construct name.</param>
    /// <param name="triggerEvent">Trigger event, or '-'.</param>
    /// <param name="element">Element whose line is keyed.</param>
    private static ForwardKey NewKey(string kind, string owner, string name, string triggerEvent, XElement element) =>
        new(kind, owner, name, triggerEvent, ((IXmlLineInfo)element).LineNumber);


    /// <summary>Reads the forward and reverse table rows of §9 in docs/legacy-form-spec.md.</summary>
    /// <returns>The data rows of both halves.</returns>
    private static MatrixSection ReadSection9()
    {
        var path = Path.Combine(RepositoryRoot.Value, "docs", "legacy-form-spec.md");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Traceability matrix document not found.", path);
        }

        var lines = File.ReadAllLines(path);
        var start = Array.FindIndex(lines, line =>
            line.StartsWith("## ", StringComparison.Ordinal)
            && line.Contains('9')
            && line.Contains("traceability", StringComparison.OrdinalIgnoreCase));
        if (start < 0)
        {
            throw new InvalidDataException($"{path}: no '## ' heading containing '9' and 'traceability' was found.");
        }

        var end = start + 1;
        while (end < lines.Length && !lines[end].StartsWith("## ", StringComparison.Ordinal))
        {
            end++;
        }

        return new MatrixSection(
            ReadRegionRows(lines, start + 1, end, ForwardRegionWord, path),
            ReadRegionRows(lines, start + 1, end, ReverseRegionWord, path));
    }

    /// <summary>Reads the table rows under the first level-3-or-deeper heading of §9 that contains a word, up to the next heading of the same or a higher level.</summary>
    /// <param name="lines">Document lines.</param>
    /// <param name="from">First line index of §9 after its heading.</param>
    /// <param name="to">Line index where §9 ends (exclusive).</param>
    /// <param name="word">Word the region heading contains, compared case-insensitively.</param>
    /// <param name="path">Document path for failure messages.</param>
    /// <returns>The region's data rows.</returns>
    private static IReadOnlyList<TableRow> ReadRegionRows(string[] lines, int from, int to, string word, string path)
    {
        var headingIndex = -1;
        var headingLevel = 0;
        for (var index = from; index < to; index++)
        {
            var level = HeadingLevel(lines[index]);
            if (level >= 3 && lines[index].Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                headingIndex = index;
                headingLevel = level;
                break;
            }
        }

        if (headingIndex < 0)
        {
            throw new InvalidDataException($"{path} §9: no '###' (or deeper) heading containing '{word}' was found.");
        }

        var regionEnd = headingIndex + 1;
        while (regionEnd < to)
        {
            var level = HeadingLevel(lines[regionEnd]);
            if (level > 0 && level <= headingLevel)
            {
                break;
            }

            regionEnd++;
        }

        return ParseTableRows(lines, headingIndex + 1, regionEnd);
    }

    /// <summary>Returns the Markdown heading level of a line, or 0 when it is not a heading.</summary>
    /// <param name="line">Document line.</param>
    private static int HeadingLevel(string line)
    {
        var match = HeadingLine.Match(line);
        return match.Success ? match.Groups[1].Length : 0;
    }

    /// <summary>Parses the Markdown table data rows of a line range, skipping separator rows and the header row above each separator.</summary>
    /// <param name="lines">Document lines.</param>
    /// <param name="from">First line index (inclusive).</param>
    /// <param name="to">Last line index (exclusive).</param>
    /// <returns>Data rows, each with the header of its table.</returns>
    private static IReadOnlyList<TableRow> ParseTableRows(string[] lines, int from, int to)
    {
        var rows = new List<TableRow>();
        IReadOnlyList<string> header = [];

        for (var index = from; index < to; index++)
        {
            if (!IsTableLine(lines[index]))
            {
                header = [];
                continue;
            }

            var cells = SplitRow(lines[index]);
            if (IsSeparator(cells))
            {
                continue;
            }

            if (index + 1 < to && IsTableLine(lines[index + 1]) && IsSeparator(SplitRow(lines[index + 1])))
            {
                header = cells;
                continue;
            }

            rows.Add(new TableRow(index + 1, cells, header));
        }

        return rows;
    }

    /// <summary>Returns whether a line's trimmed text starts with '|'.</summary>
    /// <param name="line">Document line.</param>
    private static bool IsTableLine(string line) => line.TrimStart().StartsWith('|');

    /// <summary>Returns whether every cell of a row is a Markdown separator cell.</summary>
    /// <param name="cells">Row cells.</param>
    private static bool IsSeparator(IReadOnlyList<string> cells) =>
        cells.Count > 0 && cells.All(cell => SeparatorCell.IsMatch(cell));

    /// <summary>Splits a Markdown table line on unescaped pipes, drops the empty edge cells and normalises each cell.</summary>
    /// <param name="line">Table line.</param>
    /// <returns>The normalised cells.</returns>
    private static string[] SplitRow(string line)
    {
        var parts = UnescapedPipe.Split(line.Trim()).ToList();
        if (parts.Count > 0 && string.IsNullOrWhiteSpace(parts[0]))
        {
            parts.RemoveAt(0);
        }

        if (parts.Count > 0 && string.IsNullOrWhiteSpace(parts[^1]))
        {
            parts.RemoveAt(parts.Count - 1);
        }

        return parts.Select(NormaliseCell).ToArray();
    }

    /// <summary>Unescapes '\|', trims, and strips backticks that enclose the whole cell as one code span.</summary>
    /// <param name="cell">Raw cell text.</param>
    private static string NormaliseCell(string cell)
    {
        var text = cell.Replace(@"\|", "|", StringComparison.Ordinal).Trim();
        while (text.Length >= 2 && text[0] == '`' && text[^1] == '`' && !text[1..^1].Contains('`'))
        {
            text = text[1..^1].Trim();
        }

        return text;
    }

    /// <summary>Returns "key (lines a, b)" for every key that occurs on more than one row.</summary>
    /// <param name="keys">Row keys with their document lines.</param>
    private static List<string> DuplicateKeys(IEnumerable<(string Key, int LineNumber)> keys) =>
        keys
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"'{group.Key}' (lines {string.Join(", ", group.Select(entry => entry.LineNumber))})")
            .ToList();

    /// <summary>Returns whether a reverse source cell starts with the Infrastructure: prefix.</summary>
    /// <param name="source">Source construct cell.</param>
    private static bool IsInfrastructureReason(string source) =>
        source.TrimStart('`').TrimStart().StartsWith(InfrastructurePrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns whether a reverse source cell names a T / PU / DR / PR / OI id or a construct name or event of a forward key.</summary>
    /// <param name="source">Source construct cell.</param>
    private static bool NamesSourceConstruct(string source) =>
        source.Length > 0 && (ConstructIdToken.IsMatch(source) || ForwardSegment.Value.IsMatch(source));

    /// <summary>Builds one case-sensitive pattern matching any forward-key name or event segment other than FORM and '-' as a whole identifier.</summary>
    /// <returns>The segment pattern.</returns>
    private static Regex BuildForwardSegmentPattern()
    {
        var segments = Inventory.Value.Keys
            .SelectMany(key => new[] { key.Name, key.Event })
            .Where(segment => segment.Length > 0 && segment != FormOwner && segment != NoEvent)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(segment => segment.Length)
            .ThenBy(segment => segment, StringComparer.Ordinal)
            .Select(Regex.Escape);

        return new Regex(
            "(?<![A-Za-z0-9_])(?:" + string.Join("|", segments) + ")(?![A-Za-z0-9_])",
            RegexOptions.CultureInvariant);
    }

    /// <summary>Reflects every xUnit test method of this assembly with its attribute data and its class- and method-level traits.</summary>
    /// <returns>The test methods.</returns>
    private static IReadOnlyList<TestMethodRecord> DiscoverTestMethods()
    {
        var tests = new List<TestMethodRecord>();
        foreach (var type in typeof(TraceabilityMatrixCoverageTests).Assembly.GetTypes())
        {
            var classTraits = ReadTraits(type.GetCustomAttributesData());
            foreach (var method in type.GetMethods(AllDeclaredMethods))
            {
                var attributes = method.GetCustomAttributesData().ToList();
                if (!attributes.Any(attribute => typeof(FactAttribute).IsAssignableFrom(attribute.AttributeType)))
                {
                    continue;
                }

                tests.Add(new TestMethodRecord(type, method, attributes, classTraits.Concat(ReadTraits(attributes)).ToList()));
            }
        }

        return tests;
    }

    /// <summary>Reads the name and value of each TraitAttribute in attribute data.</summary>
    /// <param name="attributes">Attribute data of a type or method.</param>
    private static List<(string Name, string Value)> ReadTraits(IEnumerable<CustomAttributeData> attributes) =>
        attributes
            .Where(attribute => attribute.AttributeType == typeof(TraitAttribute) && attribute.ConstructorArguments.Count == 2)
            .Select(attribute => (
                (attribute.ConstructorArguments[0].Value as string) ?? string.Empty,
                (attribute.ConstructorArguments[1].Value as string) ?? string.Empty))
            .ToList();

    /// <summary>Returns the rule ids prefix-01 … prefix-25.</summary>
    /// <param name="prefix">DR or PR.</param>
    private static IEnumerable<string> RuleIds(string prefix) =>
        Enumerable.Range(1, RuleCount).Select(number => prefix + "-" + number.ToString("00", CultureInfo.InvariantCulture));

    /// <summary>Returns the repository-relative path of a rule's parity fixture.</summary>
    /// <param name="id">Rule id.</param>
    private static string FixtureRelativePath(string id) => "tests/Billing.Invoicing.Tests/Parity/fixtures/" + id + ".json";

    /// <summary>Returns the absolute path of a rule's parity fixture.</summary>
    /// <param name="id">Rule id.</param>
    private static string FixturePath(string id) =>
        Path.Combine(RepositoryRoot.Value, "tests", "Billing.Invoicing.Tests", "Parity", "fixtures", id + ".json");

    /// <summary>Fails with one message listing every non-empty problem group, each capped at the listing limit.</summary>
    /// <param name="subject">What was checked.</param>
    /// <param name="groups">Problem groups with their offending items.</param>
    private static void AssertNoProblems(string subject, params (string Title, IReadOnlyCollection<string> Items)[] groups)
    {
        var failing = groups.Where(group => group.Items.Count > 0).ToList();
        if (failing.Count == 0)
        {
            return;
        }

        var sections = failing.Select(group =>
        {
            var listed = group.Items.Take(ListingCap).Select(item => "  " + item);
            var more = group.Items.Count > ListingCap ? [$"  … and {group.Items.Count - ListingCap} more"] : Array.Empty<string>();
            return string.Join(Environment.NewLine, new[] { $"{group.Title}: {group.Items.Count}" }.Concat(listed).Concat(more));
        });

        Assert.Fail(subject + Environment.NewLine + string.Join(Environment.NewLine, sections));
    }

    /// <summary>One forward key of the Form XML export.</summary>
    /// <param name="Kind">Element kind.</param>
    /// <param name="Owner">Enclosing block name, or FORM.</param>
    /// <param name="Name">Construct name.</param>
    /// <param name="Event">Trigger event, or '-'.</param>
    /// <param name="Line">Start-tag line.</param>
    private sealed record ForwardKey(string Kind, string Owner, string Name, string Event, int Line)
    {
        /// <summary>The key text kind|owner|name|event|line.</summary>
        public string Text => string.Join('|', Kind, Owner, Name, Event, Line.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Forward keys with the document-wide trigger and item totals.</summary>
    /// <param name="Keys">Forward keys in document order.</param>
    /// <param name="TriggerDescendants">Trigger elements anywhere in the document.</param>
    /// <param name="ItemDescendants">Item elements anywhere in the document.</param>
    private sealed record FormInventory(IReadOnlyList<ForwardKey> Keys, int TriggerDescendants, int ItemDescendants);

    /// <summary>Data rows of the two §9 halves.</summary>
    /// <param name="Forward">Rows of the forward region.</param>
    /// <param name="Reverse">Rows of the reverse region.</param>
    private sealed record MatrixSection(IReadOnlyList<TableRow> Forward, IReadOnlyList<TableRow> Reverse);

    /// <summary>One Markdown table data row with the header of its table.</summary>
    /// <param name="LineNumber">1-based document line.</param>
    /// <param name="Cells">Normalised cells.</param>
    /// <param name="Header">Header cells of the table, empty when the table has none.</param>
    private sealed record TableRow(int LineNumber, IReadOnlyList<string> Cells, IReadOnlyList<string> Header)
    {
        /// <summary>Returns the index of the first header cell starting with a name, or the fallback index.</summary>
        /// <param name="headerPrefix">Header name prefix, compared case-insensitively.</param>
        /// <param name="fallback">Index used when no header cell matches.</param>
        public int Column(string headerPrefix, int fallback)
        {
            for (var index = 0; index < Header.Count; index++)
            {
                if (Header[index].StartsWith(headerPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return fallback;
        }

        /// <summary>Returns the cell at an index, or an empty string beyond the row.</summary>
        /// <param name="index">Cell index.</param>
        public string Cell(int index) => index >= 0 && index < Cells.Count ? Cells[index] : string.Empty;

        /// <summary>Returns the cells from an index onward joined with " | ", or an empty string beyond the row.</summary>
        /// <param name="index">First cell index.</param>
        public string CellsFrom(int index) =>
            index >= 0 && index < Cells.Count ? string.Join(" | ", Cells.Skip(index)).Trim() : string.Empty;
    }

    /// <summary>One xUnit test method with its attribute data and effective traits.</summary>
    /// <param name="DeclaringType">Type that declares the method.</param>
    /// <param name="Method">Test method.</param>
    /// <param name="Attributes">Attribute data of the method.</param>
    /// <param name="Traits">Class- and method-level traits.</param>
    private sealed record TestMethodRecord(
        Type DeclaringType,
        MethodInfo Method,
        IReadOnlyList<CustomAttributeData> Attributes,
        IReadOnlyList<(string Name, string Value)> Traits)
    {
        /// <summary>Type.Method name used in failure listings.</summary>
        public string DisplayName => (DeclaringType.FullName ?? DeclaringType.Name) + "." + Method.Name;

        /// <summary>Whether the method carries an [OracleFact] attribute.</summary>
        public bool IsOracleFact => Attributes.Any(attribute => attribute.AttributeType == typeof(OracleFactAttribute));

        /// <summary>Whether an [OracleFact] attribute of the method sets SideEffects to the create-path value, read from attribute data only.</summary>
        public bool DeclaresCreateSideEffects =>
            Attributes
                .Where(attribute => attribute.AttributeType == typeof(OracleFactAttribute))
                .SelectMany(attribute => attribute.NamedArguments)
                .Any(argument =>
                    argument.MemberName == nameof(OracleFactAttribute.SideEffects)
                    && argument.TypedValue.Value is string value
                    && string.Equals(value, OracleFactAttribute.CreateSideEffects, StringComparison.Ordinal));

        /// <summary>Returns whether the method carries a trait with this name and value.</summary>
        /// <param name="name">Trait name.</param>
        /// <param name="value">Trait value.</param>
        public bool HasTrait(string name, string value) =>
            Traits.Any(trait => string.Equals(trait.Name, name, StringComparison.Ordinal)
                && string.Equals(trait.Value, value, StringComparison.Ordinal));

        /// <summary>Returns the values of the method's traits with this name.</summary>
        /// <param name="name">Trait name.</param>
        public IEnumerable<string> TraitValues(string name) =>
            Traits.Where(trait => string.Equals(trait.Name, name, StringComparison.Ordinal)).Select(trait => trait.Value);
    }
}

