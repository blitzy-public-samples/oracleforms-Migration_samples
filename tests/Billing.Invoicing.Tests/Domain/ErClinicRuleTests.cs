using System.Text.Json;
using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Domain.Rules;
using Billing.Invoicing.Tests.Parity;

namespace Billing.Invoicing.Tests.Domain;

/// <summary>DR-05 unit parity tests of <see cref="ErClinicRule"/> against the T032 / T033 fixture.</summary>
[Trait("Category", "DomainParity")]
public sealed class ErClinicRuleTests
{
    private const string RuleId = "DR-05";

    private const string BlockingOutcome = "Blocking";

    private const string WarningOutcome = "Warning";

    private const string PassOutcome = "Pass";

    private const string T032Locator = "05_Complex/Inv_Small_Cash.xml:50";

    private const string T033Locator = "05_Complex/Inv_Small_Cash.xml:53";

    /// <summary>Compares <see cref="ErClinicRule.Validate"/> with one DR-05 fixture case.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", "DR-05")]
    [MemberData(nameof(ParityFixture.CaseNames), RuleId, MemberType = typeof(ParityFixture))]
    public void Validate_matches_DR_05(string caseName)
    {
        var document = ParityFixture.Load(RuleId);
        var fixtureCase = ParityFixture.Case(RuleId, caseName);
        Assert.True(ParityFixture.IsDerivable(fixtureCase), $"Fixture '{RuleId}', case '{caseName}' is not derivable.");

        var expectedMessages = fixtureCase.Expected.Messages
            ?? throw new InvalidDataException($"Fixture '{RuleId}', case '{caseName}': expected.messages is missing.");
        var (header, clinic) = ReadInput(fixtureCase);

        var result = ErClinicRule.Validate(header, clinic);

        ParityFixture.AssertExact(fixtureCase.Expected.Outcome, Outcome(result));
        ParityFixture.AssertMessages(
            expectedMessages,
            result.Messages.Select(message => (message.Field, message.Text, message.Severity)));
        Assert.All(result.Messages, message => ParityFixture.AssertExact(RuleId, message.Rule));

        if (fixtureCase.Expected.Values is { } expectedValues)
        {
            ParityFixture.AssertValues(expectedValues, result.Adjusted, document.Compare);
        }
        else
        {
            Assert.Empty(result.Adjusted);
        }
    }

    /// <summary>Checks that the DR-05 fixture is a derivable domain fixture from T032 and T033 holding a blocking case and a CALL-only pass case.</summary>
    [Fact]
    [Trait("Rule", "DR-05")]
    public void Fixture_DR_05_holds_blocking_and_call_only_pass_cases()
    {
        var document = ParityFixture.Load(RuleId);

        Assert.Equal(RuleId, document.Id);
        Assert.Equal(ParityFixture.DomainClass, document.Class);
        Assert.NotEmpty(document.Cases);
        Assert.All(document.Cases, fixtureCase => Assert.True(ParityFixture.IsDerivable(fixtureCase), fixtureCase.Name));
        Assert.Contains(document.Source, source => string.Equals(source.Locator, T032Locator, StringComparison.Ordinal));
        Assert.Contains(document.Source, source => string.Equals(source.Locator, T033Locator, StringComparison.Ordinal));
        Assert.All(document.Cases, AssertDeclaresFlags);

        var cases = document.Cases
            .Select(fixtureCase => (Input: ReadInput(fixtureCase), fixtureCase.Expected.Outcome))
            .ToList();

        Assert.Contains(
            cases,
            c => c.Input.Header.Call == 1 && c.Input.Header.DeptWise == 0 && c.Outcome == PassOutcome);
        Assert.Contains(
            cases,
            c => c.Input.Header.DeptWise == 1 && c.Outcome == BlockingOutcome);
    }

    /// <summary>Classifies a rule result as Blocking, Warning or Pass.</summary>
    /// <param name="result">The rule result.</param>
    /// <returns>The outcome name used by the fixtures.</returns>
    private static string Outcome(RuleResult result)
    {
        if (result.IsBlocking)
        {
            return BlockingOutcome;
        }

        return result.Messages.Any(message => string.Equals(message.Severity, ValidationMessage.Warning, StringComparison.Ordinal))
            ? WarningOutcome
            : PassOutcome;
    }

    private static (InvoiceHeaderDraft Header, ClinicProfile? Clinic) ReadInput(FixtureCase fixtureCase)
    {
        var input = fixtureCase.Input.Deserialize<CaseInput>(ParityFixture.JsonOptions)
            ?? throw new InvalidDataException($"Fixture '{RuleId}', case '{fixtureCase.Name}': input is null.");
        var header = input.Header
            ?? throw new InvalidDataException($"Fixture '{RuleId}', case '{fixtureCase.Name}': input.header is missing.");
        return (header, input.Clinic);
    }

    private static void AssertDeclaresFlags(FixtureCase fixtureCase)
    {
        var header = RequireObject(fixtureCase, fixtureCase.Input, "header");
        Assert.True(HasProperty(header, "deptWise"), $"Case '{fixtureCase.Name}': input.header.deptWise is not declared.");
        Assert.True(HasProperty(header, "call"), $"Case '{fixtureCase.Name}': input.header.call is not declared.");

        if (TryGetProperty(fixtureCase.Input, "clinic", out var clinic) && clinic.ValueKind == JsonValueKind.Object)
        {
            Assert.True(HasProperty(clinic, "sysCatType"), $"Case '{fixtureCase.Name}': input.clinic.sysCatType is not declared.");
        }
    }

    private static JsonElement RequireObject(FixtureCase fixtureCase, JsonElement parent, string name)
    {
        Assert.True(
            TryGetProperty(parent, name, out var value) && value.ValueKind == JsonValueKind.Object,
            $"Case '{fixtureCase.Name}': input.{name} is not a JSON object.");
        return value;
    }

    private static bool HasProperty(JsonElement element, string name) => TryGetProperty(element, name, out _);

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>DR-05 case input: the draft header and the profile of its clinic.</summary>
    /// <param name="Header">Draft header supplying DEPT_WISE and CALL.</param>
    /// <param name="Clinic">Clinic profile supplying SYS_CAT_TYPE; null when the clinic is unknown.</param>
    private sealed record CaseInput(InvoiceHeaderDraft? Header, ClinicProfile? Clinic);
}
