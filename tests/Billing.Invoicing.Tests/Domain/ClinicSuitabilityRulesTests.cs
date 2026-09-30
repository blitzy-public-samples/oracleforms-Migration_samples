using System.Text.Json;
using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Domain.Rules;
using Billing.Invoicing.Tests.Parity;

namespace Billing.Invoicing.Tests.Domain;

/// <summary>DR-04 unit parity tests of <see cref="ClinicSuitabilityRules"/> against the T031 fixture.</summary>
[Trait("Category", "DomainParity")]
public sealed class ClinicSuitabilityRulesTests
{
    private const string RuleId = "DR-04";

    private const string T031Locator = "05_Complex/Inv_Small_Cash.xml:47";

    private const string MethodKey = "method";

    private const string ClinicKey = "clinic";

    private const string AgeYearsKey = "ageYears";

    private const string CheckSexMethod = nameof(ClinicSuitabilityRules.CheckSex);

    private const string CheckAgeMethod = nameof(ClinicSuitabilityRules.CheckAge);

    private const string BlockingOutcome = "Blocking";

    private const string WarningOutcome = "Warning";

    private const string PassOutcome = "Pass";

    /// <summary>Runs the check named by the case and compares outcome and messages; DR-04 never blocks.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", RuleId)]
    [MemberData(nameof(ParityFixture.CaseNames), RuleId, MemberType = typeof(ParityFixture))]
    public void Clinic_suitability_matches_DR_04(string caseName)
    {
        var fixtureCase = ParityFixture.Case(RuleId, caseName);
        Assert.True(ParityFixture.IsDerivable(fixtureCase), $"Fixture '{RuleId}', case '{caseName}' is not {ParityFixture.Derivable}.");
        var expectedMessages = fixtureCase.Expected.Messages
            ?? throw new InvalidDataException($"Fixture '{RuleId}', case '{caseName}': expected.messages is missing.");

        var method = ReadMethod(fixtureCase);
        var clinic = ReadClinic(fixtureCase);

        RuleResult result;
        switch (method)
        {
            case CheckSexMethod:
                result = ClinicSuitabilityRules.CheckSex(clinic);
                break;
            case CheckAgeMethod:
                result = ClinicSuitabilityRules.CheckAge(clinic, ReadAgeYears(fixtureCase));
                break;
            default:
                Assert.Fail($"Fixture '{RuleId}', case '{caseName}': unknown input.{MethodKey} '{method}'.");
                return;
        }

        Assert.False(result.IsBlocking, $"Case '{caseName}': DR-04 raised a blocking message.");
        ParityFixture.AssertExact(fixtureCase.Expected.Outcome, Outcome(result));
        ParityFixture.AssertMessages(
            expectedMessages,
            result.Messages.Select(message => (message.Field, message.Text, message.Severity)));
        Assert.All(result.Messages, message => ParityFixture.AssertExact(RuleId, message.Rule));
    }

    /// <summary>The DR-04 fixture is a derivable domain fixture traced to T031 with Pass and Warning cases for both checks.</summary>
    [Fact]
    [Trait("Rule", RuleId)]
    public void Fixture_DR_04_covers_sex_and_age_checks()
    {
        var document = ParityFixture.Load(RuleId);

        Assert.Equal(RuleId, document.Id);
        Assert.Equal(ParityFixture.DomainClass, document.Class);
        Assert.NotEmpty(document.Cases);
        Assert.All(document.Cases, fixtureCase => Assert.True(ParityFixture.IsDerivable(fixtureCase), fixtureCase.Name));
        Assert.Contains(document.Source, source => string.Equals(source.Locator, T031Locator, StringComparison.Ordinal));

        var cases = document.Cases
            .Select(fixtureCase => (Method: ReadMethod(fixtureCase), fixtureCase.Expected.Outcome))
            .ToList();

        foreach (var method in new[] { CheckSexMethod, CheckAgeMethod })
        {
            var outcomes = cases.Where(c => c.Method == method).Select(c => c.Outcome).ToList();
            Assert.Contains(WarningOutcome, outcomes);
            Assert.Contains(PassOutcome, outcomes);
        }
    }

    /// <summary>With no clinic selected T031 returns before both checks, so neither raises a message.</summary>
    [Fact]
    [Trait("Rule", RuleId)]
    public void No_clinic_selected_raises_no_DR_04_message()
    {
        var sex = ClinicSuitabilityRules.CheckSex(null);
        var age = ClinicSuitabilityRules.CheckAge(null, 0m);

        ParityFixture.AssertExact(PassOutcome, Outcome(sex));
        Assert.Empty(sex.Messages);
        ParityFixture.AssertExact(PassOutcome, Outcome(age));
        Assert.Empty(age.Messages);
    }

    private static string Outcome(RuleResult result) =>
        result.IsBlocking ? BlockingOutcome
        : result.Messages.Count > 0 ? WarningOutcome
        : PassOutcome;

    private static string ReadMethod(FixtureCase fixtureCase) =>
        fixtureCase.Input.TryGetProperty(MethodKey, out var method) && method.ValueKind == JsonValueKind.String
            ? method.GetString()!
            : throw new InvalidDataException($"Fixture '{RuleId}', case '{fixtureCase.Name}': input.{MethodKey} is not a string.");

    private static ClinicProfile? ReadClinic(FixtureCase fixtureCase) =>
        fixtureCase.Input.TryGetProperty(ClinicKey, out var clinic) && clinic.ValueKind != JsonValueKind.Null
            ? clinic.Deserialize<ClinicProfile>(ParityFixture.JsonOptions)
            : null;

    private static decimal ReadAgeYears(FixtureCase fixtureCase) =>
        fixtureCase.Input.TryGetProperty(AgeYearsKey, out var ageYears) && ageYears.ValueKind == JsonValueKind.Number
            ? ageYears.GetDecimal()
            : throw new InvalidDataException($"Fixture '{RuleId}', case '{fixtureCase.Name}': input.{AgeYearsKey} is not a number.");
}
