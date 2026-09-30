using System.Globalization;
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

    private const string ClinicField = "CLINICID";

    private const string AgeNotSuitable = "Patient age  not suitable for this clinic";

    private const decimal MinimumAgeYears = 1m;

    private static readonly decimal[] GridAgesYears = [0m, 0.5m, 1m, 5m, 12m, 13m, 17m, 18m, 30m, 60m, 61m];

    private static readonly decimal?[] GridAgeBounds = [null, 1m, 12m, 18m, 60m];

    /// <summary>Runs the check named by the case and compares outcome, messages and exact adjusted values; DR-04 never blocks.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", RuleId)]
    [MemberData(nameof(ParityFixture.CaseNames), RuleId, MemberType = typeof(ParityFixture))]
    public void Clinic_suitability_matches_DR_04(string caseName)
    {
        var fixture = ParityFixture.Load(RuleId);
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

        var expectedKeys = fixtureCase.Expected.Values is { } listed
            ? listed.EnumerateObject().Select(property => property.Name.ToUpperInvariant()).Order(StringComparer.Ordinal).ToList()
            : [];
        var actualKeys = result.Adjusted.Keys.Select(key => key.ToUpperInvariant()).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(expectedKeys, actualKeys);

        if (fixtureCase.Expected.Values is { } values)
        {
            ParityFixture.AssertValues(values, result.Adjusted, fixture.Compare);
        }
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

    /// <summary>With no clinic selected T031 returns before both checks, so neither raises a message nor adjusts a value.</summary>
    [Fact]
    [Trait("Rule", RuleId)]
    public void No_clinic_selected_raises_no_DR_04_message()
    {
        var sex = ClinicSuitabilityRules.CheckSex(null);
        var age = ClinicSuitabilityRules.CheckAge(null, 0m);

        ParityFixture.AssertExact(PassOutcome, Outcome(sex));
        Assert.Empty(sex.Messages);
        Assert.Empty(sex.Adjusted);
        ParityFixture.AssertExact(PassOutcome, Outcome(age));
        Assert.Empty(age.Messages);
        Assert.Empty(age.Adjusted);
    }

    /// <summary>Over every AGE_MIN, AGE_MAX and age of the grid, CheckAge warns exactly when T031's age predicate is TRUE in SQL three-valued logic.</summary>
    [Fact]
    [Trait("Rule", RuleId)]
    public void Check_age_matches_T031_three_valued_predicate()
    {
        var rows = (
            from ageMin in GridAgeBounds
            from ageMax in GridAgeBounds
            from ageYears in GridAgesYears
            let clinic = new ClinicProfile { ClinicId = 5, AgeMin = ageMin, AgeMax = ageMax }
            select (
                Inputs: $"ageMin={Format(ageMin)}, ageMax={Format(ageMax)}, ageYears={Format(ageYears)}",
                Expected: ExpectedAgeResult(T031WarnsOnAge(ageMin, ageMax, ageYears)),
                Actual: Describe(ClinicSuitabilityRules.CheckAge(clinic, ageYears)))).ToList();

        Assert.Contains(rows, row => row.Expected == ExpectedAgeResult(true));
        Assert.Contains(rows, row => row.Expected == ExpectedAgeResult(false));
        Assert.All(rows, row => Assert.Equal(row.Expected, row.Actual));
    }

    /// <summary>Every DR-04 CheckAge fixture case expects T031's three-valued outcome for its inputs, and the minimum-only and maximum-only clinics each have Warning and Pass cases.</summary>
    [Fact]
    [Trait("Rule", RuleId)]
    public void Fixture_DR_04_age_cases_match_T031_three_valued_predicate()
    {
        var ageCases = ParityFixture.Load(RuleId).Cases
            .Where(fixtureCase => ReadMethod(fixtureCase) == CheckAgeMethod)
            .Select(fixtureCase => (fixtureCase.Name, Clinic: ReadClinic(fixtureCase), AgeYears: ReadAgeYears(fixtureCase), fixtureCase.Expected.Outcome))
            .ToList();

        Assert.NotEmpty(ageCases);
        Assert.All(ageCases, ageCase => Assert.Equal(
            ageCase.Clinic is { } clinic && T031WarnsOnAge(clinic.AgeMin, clinic.AgeMax, ageCase.AgeYears) ? WarningOutcome : PassOutcome,
            ageCase.Outcome));

        foreach (var minimumOnly in new[] { true, false })
        {
            var outcomes = ageCases
                .Where(ageCase => ageCase.Clinic is { } clinic && clinic.AgeMin.HasValue == minimumOnly && clinic.AgeMax.HasValue != minimumOnly)
                .Select(ageCase => ageCase.Outcome)
                .ToList();
            Assert.Contains(WarningOutcome, outcomes);
            Assert.Contains(PassOutcome, outcomes);
        }
    }

    /// <summary>True only when T031's <c>not (age &gt;= AGE_MIN and age &lt;= AGE_MAX)</c> is TRUE after <c>nvl(age,0) &lt; 1</c> sets the age to 1; null inputs are SQL NULL.</summary>
    private static bool T031WarnsOnAge(decimal? ageMin, decimal? ageMax, decimal? ageYears)
    {
        decimal? age = (ageYears ?? 0m) < MinimumAgeYears ? MinimumAgeYears : ageYears;
        return Not(And(AtLeast(age, ageMin), AtMost(age, ageMax))) == true;
    }

    /// <summary>SQL <c>AND</c>: FALSE when either operand is FALSE, TRUE when both are TRUE, otherwise UNKNOWN (null).</summary>
    private static bool? And(bool? left, bool? right) => (left, right) switch
    {
        (false, _) or (_, false) => false,
        (true, true) => true,
        _ => null,
    };

    /// <summary>SQL <c>NOT</c>: negates TRUE and FALSE; UNKNOWN (null) stays UNKNOWN.</summary>
    private static bool? Not(bool? operand) => operand is { } value ? !value : null;

    /// <summary>SQL <c>value &gt;= bound</c>: UNKNOWN (null) when either operand is NULL.</summary>
    private static bool? AtLeast(decimal? value, decimal? bound) => value is { } v && bound is { } b ? v >= b : null;

    /// <summary>SQL <c>value &lt;= bound</c>: UNKNOWN (null) when either operand is NULL.</summary>
    private static bool? AtMost(decimal? value, decimal? bound) => value is { } v && bound is { } b ? v <= b : null;

    private static string ExpectedAgeResult(bool warns) =>
        warns ? $"{WarningOutcome} {ClinicField}|{AgeNotSuitable}|{ValidationMessage.Warning}|{RuleId}" : PassOutcome;

    private static string Describe(RuleResult result) =>
        Outcome(result) + string.Concat(result.Messages.Select(message => $" {message.Field}|{message.Text}|{message.Severity}|{message.Rule}"));

    private static string Format(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "null";

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
