using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Billing.Invoicing.Data.Errors;

namespace Billing.Invoicing.Tests.Architecture;

/// <summary>Checks the raise sites of the three package sources and their rows in the legacy Form specification §8 register.</summary>
[Trait("Category", "Compliance")]
public sealed class ErrorRegisterCoverageTests
{
    private const string SolutionFileName = "SmallCashInvoice.sln";
    private const string EnginePackage = "BIL_INVOICE_ENGINE";
    private const string RequestUnavailableConstant = "c_request_unavailable_error";
    private const int RequestUnavailableNumber = -20931;
    private const int RequestUnavailableCallLine = 619;
    private const int RequestUnavailableDeclarationLine = 176;
    private const int ExpectedDistinctNumbers = 119;
    private const int MaxListedProblems = 40;
    private const char SpaceSymbol = '\u2420';
    private const string NoLeadingLiteral = "\u2014";
    private const string CataloguedYes = "Yes";
    private const string CataloguedNo = "No";

    private static readonly (string FileName, int ExpectedCount)[] PackageSources =
    [
        ("BIL_INVOICE_ENGINE.sql", 85),
        ("BIL_IMPORT.sql", 54),
        ("BIL_INVOICE_API.sql", 17),
    ];

    private static readonly Regex RaiseCall = new(
        @"raise_application_error\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex IntegerLiteral = new(@"^-?\s*\d+$", RegexOptions.CultureInvariant);
    private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_$#]*$", RegexOptions.CultureInvariant);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);
    private static readonly Regex UnescapedPipe = new(@"(?<!\\)\|", RegexOptions.CultureInvariant);
    private static readonly Regex SeparatorCell = new(@"^:?-{3,}:?$", RegexOptions.CultureInvariant);
    private static readonly Regex FirstInteger = new(@"\d+", RegexOptions.CultureInvariant);

    private static readonly Lazy<IReadOnlyList<PackageExtraction>> Extraction = new(Extract);

    private enum ScanState
    {
        Normal,
        Literal,
        LineComment,
        BlockComment,
    }

    /// <summary>Asserts the comment-free call count of each package source.</summary>
    [Fact]
    public void RaiseCallCounts_MatchExpectedPerPackage()
    {
        var expected = PackageSources.Select(source => $"{PackageName(source.FileName)}={source.ExpectedCount}");
        var actual = Extraction.Value.Select(package => $"{package.Package}={package.CallCount}");

        Assert.Equal(expected, actual);
    }

    /// <summary>Asserts every counted call parses into a number and a message template.</summary>
    [Fact]
    public void EveryRaiseSite_Parses()
    {
        var unparsed = Extraction.Value
            .SelectMany(package => package.Unparsed)
            .Select(site => $"{site.Package}:{site.Line}: {site.Reason}")
            .ToList();

        Assert.True(
            unparsed.Count == 0,
            $"{unparsed.Count} raise site(s) could not be parsed:{Environment.NewLine}"
            + string.Join(Environment.NewLine, unparsed));
        Assert.Equal(
            Extraction.Value.Select(package => $"{package.Package}={package.CallCount}"),
            Extraction.Value.Select(package => $"{package.Package}={package.Sites.Count}"));
    }

    /// <summary>Asserts no two parsed sites share a package and line.</summary>
    [Fact]
    public void RaiseSites_AreUniqueByPackageAndLine()
    {
        var duplicates = AllSites()
            .GroupBy(site => (site.Package, site.Line))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.Package}:{group.Key.Line} ({group.Count()} sites)")
            .ToList();

        Assert.True(
            duplicates.Count == 0,
            "Raise sites repeat a (package, line): " + string.Join(", ", duplicates));
    }

    /// <summary>Asserts the resolved error numbers give the expected number of distinct values.</summary>
    [Fact]
    public void ResolvedNumbers_Give119DistinctValues()
    {
        var distinct = AllSites().Select(site => site.Number).Distinct().Count();

        Assert.Equal(ExpectedDistinctNumbers, distinct);
    }

    /// <summary>Asserts the engine call at line 619 resolves through the request-unavailable constant declared at line 176.</summary>
    [Fact]
    public void EngineLine619_ResolvesThroughRequestUnavailableConstant()
    {
        var engine = Extraction.Value.Single(package => package.Package == EnginePackage);

        var site = engine.Sites.SingleOrDefault(candidate => candidate.Line == RequestUnavailableCallLine);
        Assert.True(site is not null, $"{EnginePackage} has no parsed raise site at line {RequestUnavailableCallLine}.");
        Assert.Equal(RequestUnavailableNumber, site.Number);

        var declaration = FindConstantDeclaration(engine.StrippedText, RequestUnavailableConstant);
        Assert.True(declaration.Success, $"{EnginePackage} declares no numeric constant {RequestUnavailableConstant}.");
        Assert.Equal(RequestUnavailableDeclarationLine, LineOf(engine.StrippedText, declaration.Index));
        Assert.True(
            TryParseSignedInteger(declaration.Groups[1].Value, out var declared) && declared == RequestUnavailableNumber,
            $"{RequestUnavailableConstant} is declared as {declaration.Groups[1].Value}, not {RequestUnavailableNumber}.");
    }

    /// <summary>Asserts §8 of the legacy Form specification holds exactly one row per raise site, with its number and template.</summary>
    [Fact]
    public void LegacyFormSpecSection8_ListsEveryRaiseSite()
    {
        var specPath = Path.Combine(FindRepositoryRoot(), "docs", "legacy-form-spec.md");
        Assert.True(File.Exists(specPath), $"Legacy Form specification not found: {specPath}");

        var section = ReadErrorRegisterSection(File.ReadAllLines(specPath));
        Assert.True(section is not null, $"{specPath} has no level-2 heading naming §8 and the error register.");

        var problems = new List<string>();
        var registerTableCount = 0;
        var register = new Dictionary<(string Package, int Line), RegisterRow>();

        foreach (var table in ReadTables(section))
        {
            var rows = ReadRegisterRows(table, problems);
            if (rows is null)
            {
                continue;
            }

            registerTableCount++;
            foreach (var row in rows)
            {
                if (!register.TryAdd((row.Package, row.Line), row))
                {
                    problems.Add(
                        $"{row.Package}:{row.Line} is listed twice (§8 lines {register[(row.Package, row.Line)].SpecLine} and {row.SpecLine}).");
                }
            }
        }

        Assert.True(registerTableCount > 0, $"§8 of {specPath} holds no table with Package, Line, Number and template columns.");

        var sites = AllSites().ToList();
        foreach (var site in sites)
        {
            if (!register.TryGetValue((site.Package, site.Line), out var row))
            {
                problems.Add($"{site.Package}:{site.Line} ({site.Number}) has no §8 row.");
                continue;
            }

            if (row.Number != site.Number)
            {
                problems.Add($"{site.Package}:{site.Line}: §8 line {row.SpecLine} lists {row.Number}, the source resolves {site.Number}.");
            }

            if (!string.Equals(row.Template, site.Template, StringComparison.Ordinal))
            {
                problems.Add(
                    $"{site.Package}:{site.Line}: §8 line {row.SpecLine} template [{row.Template}] differs from the source [{site.Template}].");
            }
        }

        var siteKeys = sites.Select(site => (site.Package, site.Line)).ToHashSet();
        problems.AddRange(register.Values
            .Where(row => !siteKeys.Contains((row.Package, row.Line)))
            .OrderBy(row => row.SpecLine)
            .Select(row => $"§8 line {row.SpecLine} names {row.Package}:{row.Line}, which is not a raise site."));

        Assert.True(problems.Count == 0, DescribeProblems(problems));
    }

    /// <summary>Asserts each §8 row's Catalogue prefix and Catalogued cells give the longest matching <see cref="OracleErrorCatalog"/> prefix, else the site's leading literal.</summary>
    [Fact]
    public void LegacyFormSpecSection8_ShowsEachSitePrefixAndCatalogueFlag()
    {
        var specPath = Path.Combine(FindRepositoryRoot(), "docs", "legacy-form-spec.md");
        Assert.True(File.Exists(specPath), $"Legacy Form specification not found: {specPath}");

        var section = ReadErrorRegisterSection(File.ReadAllLines(specPath));
        Assert.True(section is not null, $"{specPath} has no level-2 heading naming §8 and the error register.");

        var problems = new List<string>();
        var rows = new List<RegisterRow>();
        foreach (var table in ReadTables(section))
        {
            rows.AddRange(ReadRegisterRows(table, problems) ?? []);
        }

        Assert.True(rows.Count > 0, $"§8 of {specPath} holds no register row.");

        var sites = AllSites().ToDictionary(site => (site.Package, site.Line));
        foreach (var row in rows)
        {
            if (!sites.TryGetValue((row.Package, row.Line), out var site))
            {
                problems.Add($"§8 line {row.SpecLine} names {row.Package}:{row.Line}, which is not a raise site.");
                continue;
            }

            var cataloguePrefix = site.LeadingLiteral is not { } literal
                ? null
                : OracleErrorCatalog.Rows
                    .Where(entry => string.Equals(entry.Package, site.Package, StringComparison.Ordinal)
                        && entry.Number == site.Number
                        && literal.StartsWith(entry.MessagePrefix, StringComparison.Ordinal))
                    .Select(entry => entry.MessagePrefix)
                    .OrderByDescending(prefix => prefix.Length)
                    .FirstOrDefault();
            var expectedPrefix = cataloguePrefix ?? site.LeadingLiteral ?? NoLeadingLiteral;
            var expectedCatalogued = cataloguePrefix is null ? CataloguedNo : CataloguedYes;

            if (row.Prefix is null)
            {
                problems.Add($"{site.Package}:{site.Line}: §8 line {row.SpecLine} has no Catalogue prefix cell; expected [{expectedPrefix}].");
            }
            else if (!string.Equals(row.Prefix, expectedPrefix, StringComparison.Ordinal))
            {
                problems.Add(
                    $"{site.Package}:{site.Line}: §8 line {row.SpecLine} Catalogue prefix [{row.Prefix}] differs from [{expectedPrefix}].");
            }

            if (row.Catalogued is null)
            {
                problems.Add($"{site.Package}:{site.Line}: §8 line {row.SpecLine} has no Catalogued cell; expected [{expectedCatalogued}].");
            }
            else if (!string.Equals(row.Catalogued, expectedCatalogued, StringComparison.Ordinal))
            {
                problems.Add(
                    $"{site.Package}:{site.Line}: §8 line {row.SpecLine} Catalogued [{row.Catalogued}] differs from [{expectedCatalogued}].");
            }
        }

        Assert.True(problems.Count == 0, DescribeProblems(problems));
    }

    /// <summary>Returns the parsed sites of all three packages in source order.</summary>
    private static IEnumerable<RaiseSite> AllSites() => Extraction.Value.SelectMany(package => package.Sites);

    /// <summary>Reads, counts and parses the raise sites of each package source.</summary>
    private static IReadOnlyList<PackageExtraction> Extract()
    {
        var root = FindRepositoryRoot();
        var extractions = new List<PackageExtraction>();

        foreach (var (fileName, _) in PackageSources)
        {
            var path = Path.Combine(root, "05_Complex", "APEX_Reference", "backend", fileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Package source not found: {path}", path);
            }

            var package = PackageName(fileName);
            var stripped = StripComments(File.ReadAllText(path));
            var calls = RaiseCall.Matches(stripped);
            var sites = new List<RaiseSite>();
            var unparsed = new List<UnparsedSite>();

            foreach (Match call in calls)
            {
                var line = LineOf(stripped, call.Index);
                var arguments = SplitArguments(stripped, call.Index + call.Length);
                if (arguments is null)
                {
                    unparsed.Add(new UnparsedSite(package, line, "argument list does not close"));
                }
                else if (arguments.Count < 2)
                {
                    unparsed.Add(new UnparsedSite(package, line, $"{arguments.Count} argument(s), two required"));
                }
                else if (!TryResolveNumber(arguments[0].Trim(), stripped, out var number))
                {
                    unparsed.Add(new UnparsedSite(package, line, $"first argument not resolved: {arguments[0].Trim()}"));
                }
                else
                {
                    sites.Add(new RaiseSite(
                        package,
                        line,
                        number,
                        Whitespace.Replace(arguments[1], " ").Trim(),
                        ReadLeadingLiteral(arguments[1])));
                }
            }

            extractions.Add(new PackageExtraction(package, calls.Count, sites, unparsed, stripped));
        }

        return extractions;
    }

    /// <summary>Replaces every character of <c>--</c> and <c>/* */</c> comments outside single-quoted literals with a space, keeping line breaks and length.</summary>
    /// <param name="text">Package source text.</param>
    private static string StripComments(string text)
    {
        var output = new StringBuilder(text.Length);
        var state = ScanState.Normal;

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';

            switch (state)
            {
                case ScanState.Normal when current == '\'':
                    state = ScanState.Literal;
                    output.Append(current);
                    break;
                case ScanState.Normal when current == '-' && next == '-':
                    state = ScanState.LineComment;
                    output.Append("  ");
                    index++;
                    break;
                case ScanState.Normal when current == '/' && next == '*':
                    state = ScanState.BlockComment;
                    output.Append("  ");
                    index++;
                    break;
                case ScanState.Normal:
                case ScanState.Literal when current != '\'':
                    output.Append(current);
                    break;
                case ScanState.Literal when next == '\'':
                    output.Append("''");
                    index++;
                    break;
                case ScanState.Literal:
                    state = ScanState.Normal;
                    output.Append(current);
                    break;
                case ScanState.LineComment when current == '\n':
                    state = ScanState.Normal;
                    output.Append(current);
                    break;
                case ScanState.BlockComment when current == '*' && next == '/':
                    state = ScanState.Normal;
                    output.Append("  ");
                    index++;
                    break;
                default:
                    output.Append(current is '\n' or '\r' ? current : ' ');
                    break;
            }
        }

        var stripped = output.ToString();
        if (stripped.Length != text.Length)
        {
            throw new InvalidOperationException(
                $"Comment stripping changed the text length from {text.Length} to {stripped.Length}.");
        }

        return stripped;
    }

    /// <summary>Splits the argument list that starts after an opening parenthesis on top-level commas, or returns null when it does not close.</summary>
    /// <param name="text">Comment-free source text.</param>
    /// <param name="start">Index just after the opening parenthesis.</param>
    private static List<string>? SplitArguments(string text, int start)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var depth = 1;
        var index = start;

        while (index < text.Length)
        {
            var character = text[index];
            if (character == '\'')
            {
                var end = SkipLiteral(text, index);
                current.Append(text, index, end - index);
                index = end;
                continue;
            }

            if (character == '(')
            {
                depth++;
            }
            else if (character == ')')
            {
                depth--;
                if (depth == 0)
                {
                    arguments.Add(current.ToString());
                    return arguments;
                }
            }
            else if (character == ',' && depth == 1)
            {
                arguments.Add(current.ToString());
                current.Clear();
                index++;
                continue;
            }

            current.Append(character);
            index++;
        }

        return null;
    }

    /// <summary>Returns the index after the quote closing the literal opened at <paramref name="open"/>, or the text length when it never closes.</summary>
    /// <param name="text">Source text.</param>
    /// <param name="open">Index of the opening single quote.</param>
    private static int SkipLiteral(string text, int open)
    {
        var index = open + 1;
        while (index < text.Length)
        {
            if (text[index] != '\'')
            {
                index++;
            }
            else if (index + 1 < text.Length && text[index + 1] == '\'')
            {
                index += 2;
            }
            else
            {
                return index + 1;
            }
        }

        return text.Length;
    }

    /// <summary>Returns the uncollapsed text of the single-quoted literal that opens a message argument, with <c>''</c> decoded to <c>'</c>; null when the argument opens with an expression.</summary>
    /// <param name="argument">Message argument of a parsed call.</param>
    /// <exception cref="InvalidOperationException">The opening literal never closes.</exception>
    private static string? ReadLeadingLiteral(string argument)
    {
        var text = argument.TrimStart();
        if (text.Length == 0 || text[0] != '\'')
        {
            return null;
        }

        var literal = new StringBuilder();
        for (var index = 1; index < text.Length; index++)
        {
            if (text[index] != '\'')
            {
                literal.Append(text[index]);
            }
            else if (index + 1 < text.Length && text[index + 1] == '\'')
            {
                literal.Append('\'');
                index++;
            }
            else
            {
                return literal.ToString();
            }
        }

        throw new InvalidOperationException($"The leading literal of the message argument never closes: {argument.Trim()}");
    }

    /// <summary>Resolves a first argument that is a signed integer literal or a numeric constant declared in the same source.</summary>
    /// <param name="argument">Trimmed first argument.</param>
    /// <param name="stripped">Comment-free text of the package source holding the call.</param>
    /// <param name="number">The resolved error number.</param>
    private static bool TryResolveNumber(string argument, string stripped, out int number)
    {
        number = 0;
        if (IntegerLiteral.IsMatch(argument))
        {
            return TryParseSignedInteger(argument, out number);
        }

        if (!Identifier.IsMatch(argument))
        {
            return false;
        }

        var declaration = FindConstantDeclaration(stripped, argument);
        return declaration.Success && TryParseSignedInteger(declaration.Groups[1].Value, out number);
    }

    /// <summary>Finds the declaration <c>name constant … := -n;</c> of a numeric constant, capturing its value in group 1.</summary>
    /// <param name="stripped">Comment-free package source text.</param>
    /// <param name="name">Constant identifier.</param>
    private static Match FindConstantDeclaration(string stripped, string name) =>
        Regex.Match(
            stripped,
            @"\b" + Regex.Escape(name) + @"\s+constant\s+[^;:]*:=\s*(-\s*\d+)\s*;",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Parses an optionally negative integer after removing all whitespace.</summary>
    /// <param name="value">Text such as <c>-20931</c> or <c>- 20931</c>.</param>
    /// <param name="number">The parsed value.</param>
    private static bool TryParseSignedInteger(string value, out int number) =>
        int.TryParse(
            Whitespace.Replace(value, string.Empty),
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out number);

    /// <summary>Returns the 1-based line holding the character at <paramref name="index"/>.</summary>
    /// <param name="text">Source text.</param>
    /// <param name="index">Character index.</param>
    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    /// <summary>Returns the package name of a source file: its name without the <c>.sql</c> extension.</summary>
    /// <param name="fileName">Package source file name.</param>
    private static string PackageName(string fileName) => Path.GetFileNameWithoutExtension(fileName);

    /// <summary>Returns the numbered lines of the first level-2 heading naming §8 and the error register, up to the next level-2 heading; null when absent.</summary>
    /// <param name="lines">Lines of the legacy Form specification.</param>
    private static List<(int Number, string Text)>? ReadErrorRegisterSection(string[] lines)
    {
        var heading = Array.FindIndex(
            lines,
            line => line.StartsWith("## ", StringComparison.Ordinal)
                && line.Contains('8')
                && line.Contains("error", StringComparison.OrdinalIgnoreCase));
        if (heading < 0)
        {
            return null;
        }

        var section = new List<(int Number, string Text)>();
        for (var index = heading + 1; index < lines.Length && !lines[index].StartsWith("## ", StringComparison.Ordinal); index++)
        {
            section.Add((index + 1, lines[index]));
        }

        return section;
    }

    /// <summary>Groups runs of consecutive table lines into tables of numbered, normalised rows.</summary>
    /// <param name="section">Numbered lines of the section.</param>
    private static List<List<(int Number, List<string> Cells)>> ReadTables(IEnumerable<(int Number, string Text)> section)
    {
        var tables = new List<List<(int Number, List<string> Cells)>>();
        List<(int Number, List<string> Cells)>? current = null;

        foreach (var (number, text) in section)
        {
            if (!text.TrimStart().StartsWith('|'))
            {
                current = null;
                continue;
            }

            if (current is null)
            {
                current = [];
                tables.Add(current);
            }

            current.Add((number, SplitRow(text)));
        }

        return tables;
    }

    /// <summary>Returns the rows of a register table, or null when the table has no header with package, line, number and template columns.</summary>
    /// <param name="table">Numbered rows of one table.</param>
    /// <param name="problems">Receives one entry per data row whose cells cannot be read.</param>
    private static List<RegisterRow>? ReadRegisterRows(List<(int Number, List<string> Cells)> table, List<string> problems)
    {
        var separator = table.FindIndex(row => row.Cells.Count > 0 && row.Cells.All(SeparatorCell.IsMatch));
        if (separator < 1)
        {
            return null;
        }

        var header = table[separator - 1].Cells.Select(cell => cell.ToLowerInvariant()).ToList();
        var packageColumn = header.FindIndex(cell => cell.Contains("package", StringComparison.Ordinal));
        var lineColumn = FirstColumn(header, cell => cell == "line", cell => cell.Contains("line", StringComparison.Ordinal));
        var numberColumn = header.FindIndex(cell => cell.Contains("number", StringComparison.Ordinal));
        var templateColumn = FirstColumn(
            header,
            cell => cell.Contains("template", StringComparison.Ordinal),
            cell => cell.Contains("message", StringComparison.Ordinal));
        if (packageColumn < 0 || lineColumn < 0 || numberColumn < 0 || templateColumn < 0)
        {
            return null;
        }

        var prefixColumn = header.FindIndex(cell => cell.Contains("prefix", StringComparison.Ordinal));
        var cataloguedColumn = header.FindIndex(cell => cell.Contains("catalogued", StringComparison.Ordinal));
        var width = new[] { packageColumn, lineColumn, numberColumn, templateColumn }.Max() + 1;
        var rows = new List<RegisterRow>();

        foreach (var (specLine, cells) in table.Skip(separator + 1))
        {
            if (cells.All(SeparatorCell.IsMatch))
            {
                continue;
            }

            if (cells.Count < width)
            {
                problems.Add($"§8 line {specLine} has {cells.Count} cell(s); the register needs {width}.");
                continue;
            }

            var package = cells[packageColumn];
            if (package.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                package = package[..^4];
            }

            var line = FirstInteger.Match(cells[lineColumn]);
            if (!line.Success || !int.TryParse(line.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var lineNumber))
            {
                problems.Add($"§8 line {specLine}: line cell [{cells[lineColumn]}] holds no integer.");
                continue;
            }

            if (!TryParseSignedInteger(cells[numberColumn].Replace('\u2212', '-'), out var errorNumber))
            {
                problems.Add($"§8 line {specLine}: number cell [{cells[numberColumn]}] is not an integer.");
                continue;
            }

            rows.Add(new RegisterRow(
                package.ToUpperInvariant(),
                lineNumber,
                errorNumber,
                cells[templateColumn],
                specLine,
                CellOrNull(cells, prefixColumn)?.Replace(SpaceSymbol, ' '),
                CellOrNull(cells, cataloguedColumn)));
        }

        return rows;
    }

    /// <summary>Returns the cell at <paramref name="column"/>, or null when the column is absent or the row is shorter.</summary>
    /// <param name="cells">Normalised cells of one row.</param>
    /// <param name="column">Zero-based column index; -1 when the header has no such column.</param>
    private static string? CellOrNull(List<string> cells, int column) =>
        column >= 0 && column < cells.Count ? cells[column] : null;

    /// <summary>Returns the index of the first header cell matching <paramref name="primary"/>, else the first matching <paramref name="fallback"/>, else -1.</summary>
    /// <param name="header">Lower-cased header cells.</param>
    /// <param name="primary">Preferred match.</param>
    /// <param name="fallback">Match used when no cell satisfies the preferred one.</param>
    private static int FirstColumn(List<string> header, Predicate<string> primary, Predicate<string> fallback)
    {
        var index = header.FindIndex(primary);
        return index >= 0 ? index : header.FindIndex(fallback);
    }

    /// <summary>Splits a Markdown table row on unescaped pipes and normalises each cell.</summary>
    /// <param name="row">One table line.</param>
    private static List<string> SplitRow(string row)
    {
        var cells = UnescapedPipe.Split(row.Trim()).ToList();
        if (cells.Count > 0 && cells[0].Trim().Length == 0)
        {
            cells.RemoveAt(0);
        }

        if (cells.Count > 0 && cells[^1].Trim().Length == 0)
        {
            cells.RemoveAt(cells.Count - 1);
        }

        return cells.Select(NormaliseCell).ToList();
    }

    /// <summary>Unescapes pipes, trims, removes surrounding backticks and collapses whitespace in one cell.</summary>
    /// <param name="cell">Raw cell text.</param>
    private static string NormaliseCell(string cell)
    {
        var value = cell.Replace(@"\|", "|", StringComparison.Ordinal).Trim();
        while (value.Length >= 2 && value[0] == '`' && value[^1] == '`')
        {
            value = value[1..^1];
        }

        return Whitespace.Replace(value, " ").Trim();
    }

    /// <summary>Formats the problems as one message: the total, then at most <see cref="MaxListedProblems"/> entries.</summary>
    /// <param name="problems">Every problem found.</param>
    private static string DescribeProblems(List<string> problems)
    {
        var message = new StringBuilder()
            .Append(problems.Count)
            .Append(" §8 register problem(s):");
        foreach (var problem in problems.Take(MaxListedProblems))
        {
            message.Append(Environment.NewLine).Append(problem);
        }

        if (problems.Count > MaxListedProblems)
        {
            message.Append(Environment.NewLine).Append("… and ").Append(problems.Count - MaxListedProblems).Append(" more.");
        }

        return message.ToString();
    }

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

    /// <summary>One parsed raise site.</summary>
    /// <param name="Package">Package name.</param>
    /// <param name="Line">1-based line of the call token.</param>
    /// <param name="Number">Resolved error number.</param>
    /// <param name="Template">Message expression with whitespace collapsed.</param>
    /// <param name="LeadingLiteral">Uncollapsed text of the literal opening the message; null when it opens with an expression.</param>
    private sealed record RaiseSite(string Package, int Line, int Number, string Template, string? LeadingLiteral);

    /// <summary>One counted call that could not be parsed.</summary>
    /// <param name="Package">Package name.</param>
    /// <param name="Line">1-based line of the call token.</param>
    /// <param name="Reason">Why parsing failed.</param>
    private sealed record UnparsedSite(string Package, int Line, string Reason);

    /// <summary>Extraction result of one package source.</summary>
    /// <param name="Package">Package name.</param>
    /// <param name="CallCount">Counted calls outside comments.</param>
    /// <param name="Sites">Parsed sites in source order.</param>
    /// <param name="Unparsed">Calls that could not be parsed.</param>
    /// <param name="StrippedText">Comment-free source text.</param>
    private sealed record PackageExtraction(
        string Package,
        int CallCount,
        IReadOnlyList<RaiseSite> Sites,
        IReadOnlyList<UnparsedSite> Unparsed,
        string StrippedText);

    /// <summary>One data row of the §8 register.</summary>
    /// <param name="Package">Upper-case package name.</param>
    /// <param name="Line">Source line the row names.</param>
    /// <param name="Number">Error number the row lists.</param>
    /// <param name="Template">Normalised message template.</param>
    /// <param name="SpecLine">1-based line of the row in the specification.</param>
    /// <param name="Prefix">Normalised Catalogue prefix cell with each U+2420 read as a space; null when absent.</param>
    /// <param name="Catalogued">Normalised Catalogued cell; null when absent.</param>
    private sealed record RegisterRow(string Package, int Line, int Number, string Template, int SpecLine, string? Prefix, string? Catalogued);
}
