using System.Text.Json;
using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Domain.Rules;
using Billing.Invoicing.Tests.Parity;

namespace Billing.Invoicing.Tests.Domain;

/// <summary>DR-12 to DR-16 unit parity tests of <see cref="LineEntryRules"/> against their fixtures.</summary>
[Trait("Category", "DomainParity")]
public sealed class LineEntryRulesTests
{
    private const string QuantityRule = "DR-12";

    private const string DiscountTypeRule = "DR-13";

    private const string ApprovalRule = "DR-14";

    private const string ServiceRule = "DR-15";

    private const string NotRequestedRule = "DR-16";

    private const string PassOutcome = "Pass";

    private const string WarningOutcome = "Warning";

    private const string BlockingOutcome = "Blocking";

    private const string T066Locator = "05_Complex/Inv_Small_Cash.xml:389";

    private const string T068Locator = "05_Complex/Inv_Small_Cash.xml:396";

    private const string T074Locator = "05_Complex/Inv_Small_Cash.xml:439";

    private const string T052Locator = "05_Complex/Inv_Small_Cash.xml:559";

    private const string T077Locator = "05_Complex/Inv_Small_Cash.xml:727";

    private const string T078Locator = "05_Complex/Inv_Small_Cash.xml:728";

    private static readonly string[] QuantityInputs = { "service", "qty" };

    private static readonly string[] DiscountTypeInputs = { "classCode", "discountType" };

    private static readonly string[] ApprovalInputs = { "serviceId", "payType", "x422ApprovCheck", "reqNeedA", "approvRefNo" };

    private static readonly string[] ServiceInputs = { "serviceId" };

    private static readonly string[] NotRequestedInputs = { "header", "service", "requested" };

    /// <summary>Compares <see cref="LineEntryRules.ValidateQuantity"/> with one DR-12 fixture case.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", "DR-12")]
    [MemberData(nameof(ParityFixture.CaseNames), QuantityRule, MemberType = typeof(ParityFixture))]
    public void ValidateQuantity_matches_DR_12(string caseName)
    {
        var fixtureCase = LoadCase(QuantityRule, caseName, QuantityInputs);
        var input = fixtureCase.Input;

        var result = LineEntryRules.ValidateQuantity(ReadObject<ServiceProfile>(input, "service"), ReadDecimal(input, "qty"));

        AssertCase(QuantityRule, fixtureCase, result);
    }

    /// <summary>Compares <see cref="LineEntryRules.ValidateDiscountType"/>, including the adjusted LDISCT, with one DR-13 fixture case.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", "DR-13")]
    [MemberData(nameof(ParityFixture.CaseNames), DiscountTypeRule, MemberType = typeof(ParityFixture))]
    public void ValidateDiscountType_matches_DR_13(string caseName)
    {
        var fixtureCase = LoadCase(DiscountTypeRule, caseName, DiscountTypeInputs);
        var input = fixtureCase.Input;

        var result = LineEntryRules.ValidateDiscountType(ReadString(input, "classCode"), ReadString(input, "discountType"));

        AssertCase(DiscountTypeRule, fixtureCase, result);
    }

    /// <summary>Compares <see cref="LineEntryRules.ValidateApproval"/> with one DR-14 fixture case.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", "DR-14")]
    [MemberData(nameof(ParityFixture.CaseNames), ApprovalRule, MemberType = typeof(ParityFixture))]
    public void ValidateApproval_matches_DR_14(string caseName)
    {
        var fixtureCase = LoadCase(ApprovalRule, caseName, ApprovalInputs);
        var input = fixtureCase.Input;

        var result = LineEntryRules.ValidateApproval(
            ReadString(input, "serviceId"),
            ReadInt(input, "payType"),
            ReadInt(input, "x422ApprovCheck"),
            ReadInt(input, "reqNeedA"),
            ReadString(input, "approvRefNo"));

        AssertCase(ApprovalRule, fixtureCase, result);
    }

    /// <summary>Compares <see cref="LineEntryRules.RequireService"/> with one DR-15 fixture case.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", "DR-15")]
    [MemberData(nameof(ParityFixture.CaseNames), ServiceRule, MemberType = typeof(ParityFixture))]
    public void RequireService_matches_DR_15(string caseName)
    {
        var fixtureCase = LoadCase(ServiceRule, caseName, ServiceInputs);

        var result = LineEntryRules.RequireService(ReadString(fixtureCase.Input, "serviceId"));

        AssertCase(ServiceRule, fixtureCase, result);
    }

    /// <summary>Compares <see cref="LineEntryRules.WarnNotRequested"/> with one DR-16 fixture case; no case may block.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", "DR-16")]
    [MemberData(nameof(ParityFixture.CaseNames), NotRequestedRule, MemberType = typeof(ParityFixture))]
    public void WarnNotRequested_matches_DR_16(string caseName)
    {
        var fixtureCase = LoadCase(NotRequestedRule, caseName, NotRequestedInputs);
        var input = fixtureCase.Input;
        var header = ReadObject<InvoiceHeaderDraft>(input, "header")
            ?? throw new InvalidDataException($"Fixture '{NotRequestedRule}', case '{caseName}': input.header is missing.");

        var result = LineEntryRules.WarnNotRequested(header, ReadObject<ServiceProfile>(input, "service"), ReadBool(input, "requested"));

        AssertCase(NotRequestedRule, fixtureCase, result);
        Assert.False(result.IsBlocking);
    }

    /// <summary>Checks that the DR-12 fixture is a domain fixture traced to T068 with blocking and passing cases.</summary>
    [Fact]
    [Trait("Rule", "DR-12")]
    public void Fixture_DR_12_is_a_domain_fixture_traced_to_T068() =>
        AssertDomainFixture(QuantityRule, new[] { T068Locator }, BlockingOutcome, PassOutcome);

    /// <summary>Checks that the DR-13 fixture is a domain fixture traced to T077 and T078 with blocking and passing cases.</summary>
    [Fact]
    [Trait("Rule", "DR-13")]
    public void Fixture_DR_13_is_a_domain_fixture_traced_to_T077_and_T078() =>
        AssertDomainFixture(DiscountTypeRule, new[] { T077Locator, T078Locator }, BlockingOutcome, PassOutcome);

    /// <summary>Checks that the DR-14 fixture is a domain fixture traced to T074 with blocking and passing cases.</summary>
    [Fact]
    [Trait("Rule", "DR-14")]
    public void Fixture_DR_14_is_a_domain_fixture_traced_to_T074() =>
        AssertDomainFixture(ApprovalRule, new[] { T074Locator }, BlockingOutcome, PassOutcome);

    /// <summary>Checks that the DR-15 fixture is a domain fixture traced to T066 with blocking and passing cases.</summary>
    [Fact]
    [Trait("Rule", "DR-15")]
    public void Fixture_DR_15_is_a_domain_fixture_traced_to_T066() =>
        AssertDomainFixture(ServiceRule, new[] { T066Locator }, BlockingOutcome, PassOutcome);

    /// <summary>Checks that the DR-16 fixture is a domain fixture traced to T052 with warning and passing cases only.</summary>
    [Fact]
    [Trait("Rule", "DR-16")]
    public void Fixture_DR_16_is_a_domain_fixture_traced_to_T052()
    {
        AssertDomainFixture(NotRequestedRule, new[] { T052Locator }, WarningOutcome, PassOutcome);
        Assert.DoesNotContain(
            ParityFixture.Load(NotRequestedRule).Cases,
            fixtureCase => string.Equals(fixtureCase.Expected.Outcome, BlockingOutcome, StringComparison.Ordinal));
    }

    /// <summary>Returns a derivable fixture case whose input names only the rule's inputs.</summary>
    /// <param name="ruleId">Rule id of the fixture.</param>
    /// <param name="caseName">Fixture case name.</param>
    /// <param name="inputs">Input property names the rule reads.</param>
    /// <returns>The fixture case.</returns>
    private static FixtureCase LoadCase(string ruleId, string caseName, IReadOnlyCollection<string> inputs)
    {
        var fixtureCase = ParityFixture.Case(ruleId, caseName);
        Assert.True(ParityFixture.IsDerivable(fixtureCase), $"Fixture '{ruleId}', case '{caseName}' is not {ParityFixture.Derivable}.");

        foreach (var property in fixtureCase.Input.EnumerateObject())
        {
            Assert.True(
                inputs.Contains(property.Name, StringComparer.OrdinalIgnoreCase),
                $"Fixture '{ruleId}', case '{caseName}': input.{property.Name} is not an input of {ruleId}.");
        }

        return fixtureCase;
    }

    /// <summary>Asserts the outcome, the messages byte for byte, their rule id and the exact adjusted item keys and values of one case.</summary>
    /// <param name="ruleId">Rule id of the fixture.</param>
    /// <param name="fixtureCase">Fixture case.</param>
    /// <param name="result">Actual rule result.</param>
    private static void AssertCase(string ruleId, FixtureCase fixtureCase, RuleResult result)
    {
        var expected = fixtureCase.Expected;
        var expectedMessages = expected.Messages
            ?? throw new InvalidDataException($"Fixture '{ruleId}', case '{fixtureCase.Name}': expected.messages is missing.");

        ParityFixture.AssertExact(expected.Outcome, Outcome(result));
        ParityFixture.AssertMessages(expectedMessages, result.Messages.Select(message => (message.Field, message.Text, message.Severity)));
        Assert.All(result.Messages, message => ParityFixture.AssertExact(ruleId, message.Rule));

        if (expected.Values is { } values)
        {
            var expectedKeys = values.EnumerateObject().Select(property => property.Name.ToUpperInvariant()).Order(StringComparer.Ordinal).ToList();
            var actualKeys = result.Adjusted.Keys.Select(key => key.ToUpperInvariant()).Order(StringComparer.Ordinal).ToList();
            Assert.Equal(expectedKeys, actualKeys);
            ParityFixture.AssertValues(values, result.Adjusted, ParityFixture.Load(ruleId).Compare);
        }
        else
        {
            Assert.Empty(result.Adjusted);
        }
    }

    /// <summary>Asserts that a fixture is a derivable domain fixture citing the given locators and holding each outcome.</summary>
    /// <param name="ruleId">Rule id of the fixture.</param>
    /// <param name="locators">Source locators the fixture must cite.</param>
    /// <param name="outcomes">Outcomes at least one case must expect.</param>
    private static void AssertDomainFixture(string ruleId, IReadOnlyList<string> locators, params string[] outcomes)
    {
        var document = ParityFixture.Load(ruleId);

        Assert.Equal(ruleId, document.Id);
        Assert.Equal(ParityFixture.DomainClass, document.Class);
        Assert.NotEmpty(document.Cases);
        Assert.All(document.Cases, fixtureCase => Assert.True(ParityFixture.IsDerivable(fixtureCase), fixtureCase.Name));

        foreach (var locator in locators)
        {
            Assert.Contains(document.Source, source => string.Equals(source.Locator, locator, StringComparison.Ordinal));
        }

        foreach (var outcome in outcomes)
        {
            Assert.Contains(document.Cases, fixtureCase => string.Equals(fixtureCase.Expected.Outcome, outcome, StringComparison.Ordinal));
        }
    }

    /// <summary>Maps a rule result to its fixture outcome: Blocking, Warning or Pass.</summary>
    /// <param name="r">Rule result.</param>
    /// <returns>The outcome name.</returns>
    private static string Outcome(RuleResult r) =>
        r.IsBlocking ? BlockingOutcome : r.Messages.Count > 0 ? WarningOutcome : PassOutcome;

    /// <summary>Returns a named input property, matched case-insensitively, or null when absent or JSON null.</summary>
    /// <param name="input">Case input object.</param>
    /// <param name="name">Property name.</param>
    /// <returns>The property value, or null.</returns>
    private static JsonElement? Property(JsonElement input, string name)
    {
        foreach (var property in input.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind == JsonValueKind.Null ? null : property.Value;
            }
        }

        return null;
    }

    /// <summary>Reads an optional integer input.</summary>
    /// <param name="input">Case input object.</param>
    /// <param name="name">Property name.</param>
    /// <returns>The integer, or null when absent or JSON null.</returns>
    private static int? ReadInt(JsonElement input, string name) =>
        Property(input, name) is not { } value
            ? null
            : value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
                ? number
                : throw new InvalidDataException($"input.{name} {value.GetRawText()} is not a 32-bit integer.");

    /// <summary>Reads an optional decimal input.</summary>
    /// <param name="input">Case input object.</param>
    /// <param name="name">Property name.</param>
    /// <returns>The decimal, or null when absent or JSON null.</returns>
    private static decimal? ReadDecimal(JsonElement input, string name) =>
        Property(input, name) is not { } value
            ? null
            : value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
                ? number
                : throw new InvalidDataException($"input.{name} {value.GetRawText()} is not a decimal.");

    /// <summary>Reads an optional string input.</summary>
    /// <param name="input">Case input object.</param>
    /// <param name="name">Property name.</param>
    /// <returns>The string, or null when absent or JSON null.</returns>
    private static string? ReadString(JsonElement input, string name) =>
        Property(input, name) is not { } value
            ? null
            : value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : throw new InvalidDataException($"input.{name} {value.GetRawText()} is not a string.");

    /// <summary>Reads a required boolean input.</summary>
    /// <param name="input">Case input object.</param>
    /// <param name="name">Property name.</param>
    /// <returns>The boolean.</returns>
    private static bool ReadBool(JsonElement input, string name) =>
        Property(input, name) is { ValueKind: JsonValueKind.True or JsonValueKind.False } value
            ? value.GetBoolean()
            : throw new InvalidDataException($"input.{name} is missing or not a boolean.");

    /// <summary>Deserializes an optional object input with <see cref="ParityFixture.JsonOptions"/>.</summary>
    /// <typeparam name="T">Target type.</typeparam>
    /// <param name="input">Case input object.</param>
    /// <param name="name">Property name.</param>
    /// <returns>The object, or null when absent or JSON null.</returns>
    private static T? ReadObject<T>(JsonElement input, string name)
        where T : class =>
        Property(input, name) is not { } value
            ? null
            : value.ValueKind == JsonValueKind.Object
                ? value.Deserialize<T>(ParityFixture.JsonOptions)
                : throw new InvalidDataException($"input.{name} {value.GetRawText()} is not an object.");
}
