using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Xunit.Sdk;

namespace Billing.Invoicing.Tests.Parity;

/// <summary>Source construct a fixture is derived from.</summary>
/// <param name="Construct">Trigger, program unit or package routine name.</param>
/// <param name="Locator">Source file and line, as <c>path:line</c>.</param>
public sealed record FixtureSource(string Construct, string Locator);

/// <summary>Expected rule message.</summary>
/// <param name="Field">Item the message is attached to, or null for a form-level message.</param>
/// <param name="Text">Message text.</param>
/// <param name="Severity">Message severity.</param>
public sealed record FixtureMessage(string? Field, string Text, string Severity);

/// <summary>Expected Oracle application error.</summary>
/// <param name="Package">Raising package.</param>
/// <param name="Number">Signed Oracle error number.</param>
/// <param name="MessagePrefix">Leading text of the error message.</param>
public sealed record FixtureError(string Package, int Number, string MessagePrefix);

/// <summary>Comparison settings of a fixture document.</summary>
/// <param name="MoneyDecimals">Scale at which numeric values are compared.</param>
/// <param name="Exact">Value keys compared as exact text.</param>
public sealed record FixtureCompare(int MoneyDecimals, IReadOnlyList<string> Exact);

/// <summary>Database rows and shared-package outputs a package case assumes.</summary>
/// <param name="Rows">Assumed rows: table, key and column values.</param>
/// <param name="Context">Assumed outputs of shared packages.</param>
public sealed record FixtureRequires(IReadOnlyList<JsonElement> Rows, IReadOnlyList<JsonElement> Context);

/// <summary>Expected result of a fixture case.</summary>
/// <param name="Outcome">Expected outcome, such as Pass, Warning or Blocking.</param>
/// <param name="Messages">Expected messages, in order.</param>
/// <param name="Values">Expected output values, as a JSON object.</param>
/// <param name="Error">Expected Oracle application error.</param>
public sealed record FixtureExpected(string? Outcome, IReadOnlyList<FixtureMessage>? Messages, JsonElement? Values, FixtureError? Error);

/// <summary>One case of a fixture document.</summary>
/// <param name="Name">Case name, unique within the document.</param>
/// <param name="Status"><see cref="ParityFixture.Derivable"/> or <see cref="ParityFixture.PendingEvidence"/>.</param>
/// <param name="Input">Case input, deserialised by the test into its own shape.</param>
/// <param name="Expected">Expected result.</param>
/// <param name="Requires">Assumed rows and shared-package outputs of a package case.</param>
/// <param name="PendingOn">Open-item id of a pending-evidence case.</param>
public sealed record FixtureCase(
    string Name,
    string Status,
    JsonElement Input,
    FixtureExpected Expected,
    FixtureRequires? Requires = null,
    string? PendingOn = null);

/// <summary>One parity fixture file.</summary>
/// <param name="Id">Rule id, equal to the file name.</param>
/// <param name="Class"><see cref="ParityFixture.DomainClass"/> or <see cref="ParityFixture.PackageClass"/>.</param>
/// <param name="Source">Constructs the expected values are derived from.</param>
/// <param name="Cases">Cases, in file order.</param>
/// <param name="Compare">Comparison settings.</param>
public sealed record FixtureDocument(
    string Id,
    string Class,
    IReadOnlyList<FixtureSource> Source,
    IReadOnlyList<FixtureCase> Cases,
    FixtureCompare Compare);

/// <summary>Loads parity fixtures and compares rule outputs with their expected values.</summary>
public static partial class ParityFixture
{
    /// <summary>Status of a case whose expected values are derived from cited source text.</summary>
    public const string Derivable = "derivable";

    /// <summary>Status of a case whose expected values await an open item.</summary>
    public const string PendingEvidence = "pending-evidence";

    /// <summary>Class of a Domain rule fixture.</summary>
    public const string DomainClass = "domain";

    /// <summary>Class of a package-resident rule fixture.</summary>
    public const string PackageClass = "package";

    private static readonly ConcurrentDictionary<string, FixtureDocument> Documents = new(StringComparer.Ordinal);

    /// <summary>Serializer options for fixture documents and case inputs.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    /// <summary>Loads and validates a fixture document, cached per id.</summary>
    /// <param name="id">Rule id such as <c>DR-05</c> or <c>PR-06</c>.</param>
    /// <returns>The validated document.</returns>
    /// <exception cref="ArgumentException">The id is not <c>DR-nn</c> or <c>PR-nn</c>.</exception>
    /// <exception cref="FileNotFoundException">The fixture file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is malformed or fails validation.</exception>
    public static FixtureDocument Load(string id)
    {
        if (id is null || !FixtureIdPattern().IsMatch(id))
        {
            throw new ArgumentException($"Fixture id '{id}' is not of the form DR-nn or PR-nn.", nameof(id));
        }

        return Documents.GetOrAdd(id, static key => ReadDocument(key));
    }

    /// <summary>Theory rows holding the case names of a fixture, in file order.</summary>
    /// <param name="id">Rule id of the fixture.</param>
    /// <returns>One row per case, each holding the case name.</returns>
    public static IEnumerable<object[]> CaseNames(string id) =>
        Load(id).Cases.Select(fixtureCase => new object[] { fixtureCase.Name }).ToArray();

    /// <summary>Returns the named case of a fixture.</summary>
    /// <param name="id">Rule id of the fixture.</param>
    /// <param name="name">Case name, matched ordinally.</param>
    /// <returns>The matching case.</returns>
    /// <exception cref="KeyNotFoundException">The fixture has no case of that name.</exception>
    public static FixtureCase Case(string id, string name)
    {
        foreach (var fixtureCase in Load(id).Cases)
        {
            if (string.Equals(fixtureCase.Name, name, StringComparison.Ordinal))
            {
                return fixtureCase;
            }
        }

        throw new KeyNotFoundException($"Fixture '{id}' has no case '{name}'.");
    }

    /// <summary>Whether a case is derivable and therefore asserted.</summary>
    /// <param name="c">The case.</param>
    /// <returns>True when the case status is <see cref="Derivable"/>.</returns>
    public static bool IsDerivable(FixtureCase c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return string.Equals(c.Status, Derivable, StringComparison.Ordinal);
    }

    /// <summary>Asserts that two amounts are equal once both are rounded half away from zero.</summary>
    /// <param name="expected">Expected amount, or null when no value is expected.</param>
    /// <param name="actual">Actual amount.</param>
    /// <param name="decimals">Scale of the comparison, 0 to 28.</param>
    public static void AssertMoney(decimal? expected, decimal? actual, int decimals = 2)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(decimals);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(decimals, 28);

        if (expected is null)
        {
            Assert.Null(actual);
            return;
        }

        Assert.NotNull(actual);
        Assert.Equal(
            decimal.Round(expected.Value, decimals, MidpointRounding.AwayFromZero),
            decimal.Round(actual.GetValueOrDefault(), decimals, MidpointRounding.AwayFromZero));
    }

    /// <summary>Asserts ordinal, byte-for-byte text equality, whitespace included.</summary>
    /// <param name="expected">Expected text.</param>
    /// <param name="actual">Actual text.</param>
    public static void AssertExact(string? expected, string? actual) => Assert.Equal(expected, actual);

    /// <summary>Asserts that the actual messages equal the expected ones, in order.</summary>
    /// <param name="expected">Expected messages.</param>
    /// <param name="actual">Actual messages as field, text and severity.</param>
    public static void AssertMessages(
        IReadOnlyList<FixtureMessage> expected,
        IEnumerable<(string? Field, string Text, string Severity)> actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        var expectedRows = expected.Select(message => (message.Field, message.Text, message.Severity)).ToList();
        var actualRows = actual.ToList();

        AtPath($"messages (actual: {DescribeMessages(actualRows)})", () => Assert.Equal(expectedRows.Count, actualRows.Count));
        for (var i = 0; i < expectedRows.Count; i++)
        {
            var (expectedRow, actualRow) = (expectedRows[i], actualRows[i]);
            AtPath($"messages[{i}].field", () => AssertExact(expectedRow.Field, actualRow.Field));
            AtPath($"messages[{i}].text", () => AssertExact(expectedRow.Text, actualRow.Text));
            AtPath($"messages[{i}].severity", () => AssertExact(expectedRow.Severity, actualRow.Severity));
        }
    }

    /// <summary>Asserts an Oracle application error by package, signed number and message prefix.</summary>
    /// <param name="expected">Expected error.</param>
    /// <param name="package">Raising package.</param>
    /// <param name="number">Signed Oracle error number.</param>
    /// <param name="message">Error message text.</param>
    public static void AssertError(FixtureError expected, string package, int number, string message)
    {
        ArgumentNullException.ThrowIfNull(expected);

        AtPath("error.package", () => AssertExact(expected.Package, package));
        AtPath("error.number", () => Assert.Equal(expected.Number, number));
        AtPath("error.messagePrefix", () => Assert.StartsWith(expected.MessagePrefix, message, StringComparison.Ordinal));
    }

    /// <summary>Asserts that each expected value equals the actual value under the same key, matched case-insensitively.</summary>
    /// <param name="expected">Expected values, a JSON object; actual keys it does not name are ignored.</param>
    /// <param name="actual">Actual values; nested objects are dictionaries and arrays are sequences.</param>
    /// <param name="compare">
    /// Strings and <see cref="FixtureCompare.Exact"/> keys compare as exact text (booleans as <c>true</c>/<c>false</c>,
    /// <see cref="DateTime"/> as <c>yyyy-MM-ddTHH:mm:ss</c>); other numbers compare at <see cref="FixtureCompare.MoneyDecimals"/>.
    /// </param>
    public static void AssertValues(JsonElement expected, IReadOnlyDictionary<string, object?> actual, FixtureCompare compare)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(compare);

        Assert.True(
            expected.ValueKind == JsonValueKind.Object,
            $"Expected values must be a JSON object, not {expected.ValueKind}.");
        AssertObject(string.Empty, expected, actual, compare);
    }

    private static void AssertObject(string parentPath, JsonElement expected, IReadOnlyDictionary<string, object?> actual, FixtureCompare compare)
    {
        foreach (var property in expected.EnumerateObject())
        {
            var path = parentPath.Length == 0 ? property.Name : parentPath + "." + property.Name;
            if (!TryGetValue(actual, property.Name, path, out var value))
            {
                Assert.Fail($"Missing value '{path}'");
            }

            AssertValue(path, property.Name, property.Value, value, compare);
        }
    }

    private static void AssertValue(string path, string key, JsonElement expected, object? actual, FixtureCompare compare)
    {
        if (actual is DBNull)
        {
            actual = null;
        }

        var exact = compare.Exact is not null && compare.Exact.Contains(key, StringComparer.Ordinal);
        switch (expected.ValueKind)
        {
            case JsonValueKind.String:
                AtPath(path, () => AssertExact(expected.GetString(), ToText(actual)));
                break;
            case JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False when exact:
                AtPath(path, () => AssertExact(expected.GetRawText(), ToText(actual)));
                break;
            case JsonValueKind.Number:
                AtPath(path, () => AssertMoney(ReadDecimal(expected), ToDecimal(actual), compare.MoneyDecimals));
                break;
            case JsonValueKind.True or JsonValueKind.False:
                AtPath(path, () => Assert.Equal(expected.GetBoolean(), ToBoolean(actual)));
                break;
            case JsonValueKind.Null:
                AtPath(path, () => Assert.Null(actual));
                break;
            case JsonValueKind.Object:
                if (!TryAsDictionary(actual, out var map))
                {
                    Assert.Fail($"Value '{path}': expected an object, actual {Describe(actual)}.");
                }

                AssertObject(path, expected, map, compare);
                break;
            case JsonValueKind.Array:
                AssertArray(path, key, expected, actual, compare);
                break;
            default:
                Assert.Fail($"Value '{path}': unsupported expected JSON kind {expected.ValueKind}.");
                break;
        }
    }

    private static void AssertArray(string path, string key, JsonElement expected, object? actual, FixtureCompare compare)
    {
        if (actual is string || actual is not IEnumerable sequence)
        {
            Assert.Fail($"Value '{path}': expected an array, actual {Describe(actual)}.");
            return;
        }

        var items = sequence.Cast<object?>().ToList();
        var expectedCount = expected.GetArrayLength();
        Assert.True(
            expectedCount == items.Count,
            $"Value '{path}': expected {expectedCount} element(s), actual {items.Count}.");

        var index = 0;
        foreach (var element in expected.EnumerateArray())
        {
            AssertValue($"{path}[{index}]", key, element, items[index], compare);
            index++;
        }
    }

    private static bool TryGetValue(IReadOnlyDictionary<string, object?> actual, string key, string path, out object? value)
    {
        var matches = actual
            .Where(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count > 1)
        {
            Assert.Fail($"Value '{path}': actual keys {string.Join(", ", matches.Select(pair => pair.Key))} differ only in case.");
        }

        if (matches.Count == 1)
        {
            value = matches[0].Value;
            return true;
        }

        return actual.TryGetValue(key, out value);
    }

    private static bool TryAsDictionary(object? actual, [NotNullWhen(true)] out IReadOnlyDictionary<string, object?>? map)
    {
        map = actual switch
        {
            IReadOnlyDictionary<string, object?> readOnly => readOnly,
            IDictionary<string, object?> mutable => new ReadOnlyDictionary<string, object?>(mutable),
            _ => null,
        };
        return map is not null;
    }

    private static void AtPath(string path, Action assertion)
    {
        try
        {
            assertion();
        }
        catch (XunitException ex)
        {
            Assert.Fail($"Value '{path}': {ex.Message}");
        }
    }

    private static decimal ReadDecimal(JsonElement expected) =>
        expected.TryGetDecimal(out var value)
            ? value
            : throw FailException.ForFailure($"expected number {expected.GetRawText()} is outside the decimal range.");

    private static decimal? ToDecimal(object? actual)
    {
        if (actual is null)
        {
            return null;
        }

        try
        {
            return Convert.ToDecimal(actual, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw FailException.ForFailure($"actual {Describe(actual)} is not a number: {ex.Message}");
        }
    }

    private static bool ToBoolean(object? actual)
    {
        if (actual is null)
        {
            throw FailException.ForFailure("actual is null, expected a boolean.");
        }

        try
        {
            return Convert.ToBoolean(actual, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException)
        {
            throw FailException.ForFailure($"actual {Describe(actual)} is not a boolean: {ex.Message}");
        }
    }

    private static string? ToText(object? actual) => actual switch
    {
        null => null,
        bool flag => flag ? "true" : "false",
        DateTime moment => moment.ToString("s", CultureInfo.InvariantCulture),
        _ => Convert.ToString(actual, CultureInfo.InvariantCulture),
    };

    private static string Describe(object? actual) =>
        actual is null ? "null" : $"{actual.GetType().Name} '{ToText(actual)}'";

    private static string DescribeMessages(IReadOnlyList<(string? Field, string Text, string Severity)> messages) =>
        messages.Count == 0
            ? "none"
            : string.Join("; ", messages.Select(message => $"[{message.Field ?? "-"}] {message.Severity}: '{message.Text}'"));

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.Strict,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private static FixtureDocument ReadDocument(string id)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Parity", "fixtures", id + ".json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Fixture '{id}' was not found at '{path}'.", path);
        }

        FixtureDocument? document;
        try
        {
            using var stream = File.OpenRead(path);
            document = JsonSerializer.Deserialize<FixtureDocument>(stream, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Fixture '{id}': {ex.Message}", ex);
        }

        if (document is null)
        {
            throw Invalid(id, null, "the document is null.");
        }

        return Validate(id, document);
    }

    private static FixtureDocument Validate(string id, FixtureDocument document)
    {
        if (!string.Equals(document.Id, id, StringComparison.Ordinal))
        {
            throw Invalid(id, null, $"id '{document.Id}' does not match the file name.");
        }

        var requiredClass = id.StartsWith("DR-", StringComparison.Ordinal) ? DomainClass : PackageClass;
        if (!string.Equals(document.Class, requiredClass, StringComparison.Ordinal))
        {
            throw Invalid(id, null, $"class '{document.Class}' must be '{requiredClass}'.");
        }

        if (document.Source is null || document.Source.Count == 0)
        {
            throw Invalid(id, null, "source is missing or empty.");
        }

        for (var i = 0; i < document.Source.Count; i++)
        {
            var source = document.Source[i];
            if (source is null || string.IsNullOrWhiteSpace(source.Construct) || string.IsNullOrWhiteSpace(source.Locator))
            {
                throw Invalid(id, null, $"source[{i}] needs a construct and a locator.");
            }
        }

        if (document.Compare is null)
        {
            throw Invalid(id, null, "compare is missing.");
        }

        if (document.Compare.MoneyDecimals < 0)
        {
            throw Invalid(id, null, $"compare.moneyDecimals {document.Compare.MoneyDecimals} is negative.");
        }

        if (document.Compare.Exact is null)
        {
            throw Invalid(id, null, "compare.exact is missing.");
        }

        if (document.Cases is null || document.Cases.Count == 0)
        {
            throw Invalid(id, null, "cases is missing or empty.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var cases = new List<FixtureCase>(document.Cases.Count);
        for (var i = 0; i < document.Cases.Count; i++)
        {
            cases.Add(ValidateCase(id, requiredClass, i, document.Cases[i], names));
        }

        return document with { Cases = cases.AsReadOnly() };
    }

    private static FixtureCase ValidateCase(string id, string documentClass, int index, FixtureCase? fixtureCase, HashSet<string> names)
    {
        if (fixtureCase is null)
        {
            throw Invalid(id, $"#{index}", "the case is null.");
        }

        if (string.IsNullOrWhiteSpace(fixtureCase.Name))
        {
            throw Invalid(id, $"#{index}", "name is blank.");
        }

        var label = fixtureCase.Name;
        if (!names.Add(fixtureCase.Name))
        {
            throw Invalid(id, label, "name is not unique.");
        }

        if (fixtureCase.Status is not (Derivable or PendingEvidence))
        {
            throw Invalid(id, label, $"status '{fixtureCase.Status}' must be '{Derivable}' or '{PendingEvidence}'.");
        }

        if (documentClass == DomainClass && fixtureCase.Status != Derivable)
        {
            throw Invalid(id, label, $"a domain case must be '{Derivable}'.");
        }

        if (fixtureCase.Status == PendingEvidence
            && (fixtureCase.PendingOn is null || !OpenItemPattern().IsMatch(fixtureCase.PendingOn)))
        {
            throw Invalid(id, label, $"pendingOn '{fixtureCase.PendingOn}' must be an open-item id OI-nn.");
        }

        if (fixtureCase.Expected is null)
        {
            throw Invalid(id, label, "expected is missing.");
        }

        return fixtureCase.Requires is null
            ? fixtureCase
            : fixtureCase with
            {
                Requires = new FixtureRequires(fixtureCase.Requires.Rows ?? [], fixtureCase.Requires.Context ?? []),
            };
    }

    private static InvalidDataException Invalid(string id, string? caseName, string problem) =>
        new(caseName is null
            ? $"Fixture '{id}': {problem}"
            : $"Fixture '{id}', case '{caseName}': {problem}");

    [GeneratedRegex(@"^(DR|PR)-[0-9]{2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex FixtureIdPattern();

    [GeneratedRegex(@"^OI-[0-9]{2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex OpenItemPattern();
}
