using System.Globalization;
using System.Text.Json;
using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Domain.Rules;
using Billing.Invoicing.Tests.Parity;

namespace Billing.Invoicing.Tests.Domain;

/// <summary>DR-03 unit parity tests of <see cref="PatientEligibilityRules"/> against the T023 fixture.</summary>
[Trait("Category", "DomainParity")]
public sealed class PatientEligibilityRulesTests
{
    private const string RuleId = "DR-03";

    private const string PassOutcome = "Pass";

    private const string T023Locator = "05_Complex/Inv_Small_Cash.xml:18";

    private static readonly DateTime DraftDate = new(2026, 9, 28);

    private static readonly string[] DraftTimesOfDay = ["00:00:01", "10:15:00", "12:00:00", "18:30:00", "23:59:59"];

    /// <summary>Each fixture case yields its expected outcome and PATIENTNO messages, in order and verbatim.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", RuleId)]
    [MemberData(nameof(ParityFixture.CaseNames), RuleId, MemberType = typeof(ParityFixture))]
    public void Evaluate_matches_DR_03(string caseName)
    {
        var fixtureCase = ParityFixture.Case(RuleId, caseName);
        Assert.True(ParityFixture.IsDerivable(fixtureCase), $"Fixture '{RuleId}', case '{caseName}' is not derivable.");
        var expectedMessages = fixtureCase.Expected.Messages
            ?? throw new InvalidDataException($"Fixture '{RuleId}', case '{caseName}': expected.messages is missing.");
        var (coverage, parameters, draftDate) = ReadInput(fixtureCase);

        var result = PatientEligibilityRules.Evaluate(coverage, parameters, draftDate);

        ParityFixture.AssertExact(fixtureCase.Expected.Outcome, Outcome(result));
        ParityFixture.AssertMessages(expectedMessages, result.Messages.Select(m => (m.Field, m.Text, m.Severity)));
        Assert.All(result.Messages, m => Assert.Equal(RuleId, m.Rule));
        Assert.Empty(result.Adjusted);
    }

    /// <summary>Each midnight-dated fixture case yields its expected outcome and messages at any time of the same draft day.</summary>
    /// <param name="caseName">Fixture case name.</param>
    /// <param name="timeOfDay">Time of day added to the case's draft date, as <c>HH:mm:ss</c>.</param>
    [Theory]
    [Trait("Rule", RuleId)]
    [MemberData(nameof(CaseNamesAtDraftTimesOfDay))]
    public void Evaluate_matches_DR_03_at_any_time_of_the_draft_day(string caseName, string timeOfDay)
    {
        var fixtureCase = ParityFixture.Case(RuleId, caseName);
        Assert.True(ParityFixture.IsDerivable(fixtureCase), $"Fixture '{RuleId}', case '{caseName}' is not derivable.");
        var expectedMessages = fixtureCase.Expected.Messages
            ?? throw new InvalidDataException($"Fixture '{RuleId}', case '{caseName}': expected.messages is missing.");
        var (coverage, parameters, draftDate) = ReadInput(fixtureCase);
        Assert.Equal(TimeSpan.Zero, draftDate.TimeOfDay);
        var draftTime = draftDate + TimeSpan.ParseExact(timeOfDay, @"hh\:mm\:ss", CultureInfo.InvariantCulture);

        var result = PatientEligibilityRules.Evaluate(coverage, parameters, draftTime);

        ParityFixture.AssertExact(fixtureCase.Expected.Outcome, Outcome(result));
        ParityFixture.AssertMessages(expectedMessages, result.Messages.Select(m => (m.Field, m.Text, m.Severity)));
        Assert.All(result.Messages, m => Assert.Equal(RuleId, m.Rule));
        Assert.Empty(result.Adjusted);
    }

    /// <summary>The DR-03 fixture is a domain fixture derived from T023 with at least one derivable case.</summary>
    [Fact]
    [Trait("Rule", RuleId)]
    public void Fixture_is_a_domain_fixture_with_derivable_cases()
    {
        var fixture = ParityFixture.Load(RuleId);

        Assert.Equal(RuleId, fixture.Id);
        Assert.Equal(ParityFixture.DomainClass, fixture.Class);
        Assert.NotEmpty(fixture.Cases);
        Assert.All(fixture.Cases, fixtureCase => Assert.True(ParityFixture.IsDerivable(fixtureCase), fixtureCase.Name));
        Assert.Contains(fixture.Source, source => string.Equals(source.Locator, T023Locator, StringComparison.Ordinal));
    }

    /// <summary>A card company without a sub-company or class skips the policy and class checks of T023.</summary>
    [Fact]
    [Trait("Rule", RuleId)]
    public void Evaluate_skips_policy_and_class_checks_without_sub_company_or_class()
    {
        var coverage = CardCompanyCoverage() with
        {
            SubCompCode = null,
            SubCompanyContractEnd = new DateTime(2026, 9, 10),
            SubCompanyIsActive = 2,
            MyClass = null,
            ClassWithRef = 1,
            ClassIsActive = 2,
        };

        var result = PatientEligibilityRules.Evaluate(coverage, new InvoiceEntryParameters { InvDateAdmin = 2 }, DraftDate);

        ParityFixture.AssertExact(PassOutcome, Outcome(result));
        Assert.Empty(result.Messages);
    }

    /// <summary>A direct company (type 1) skips the card, policy and class checks of T023.</summary>
    [Fact]
    [Trait("Rule", RuleId)]
    public void Evaluate_skips_card_policy_and_class_checks_for_direct_company()
    {
        var coverage = CardCompanyCoverage() with
        {
            CompanyType = 1,
            CardEnd = new DateTime(2026, 9, 20),
            SubCompanyContractEnd = new DateTime(2026, 9, 10),
            SubCompanyIsActive = 2,
            ClassIsActive = 2,
        };

        var result = PatientEligibilityRules.Evaluate(coverage, new InvoiceEntryParameters { InvDateAdmin = 2 }, DraftDate);

        ParityFixture.AssertExact(PassOutcome, Outcome(result));
        Assert.Empty(result.Messages);
    }

    /// <summary>A daytime draft compares the contract, card and policy end dates as read against the draft day, with T023's texts.</summary>
    /// <param name="endField">Coverage end date varied from the valid card-company coverage.</param>
    /// <param name="endDate">That end date as read, in the sortable <c>s</c> format.</param>
    /// <param name="draftTime">Draft date and time, in the sortable <c>s</c> format.</param>
    /// <param name="invDateAdmin"><c>INV_DATE_ADMIN</c>: 1 date admin, 2 normal user.</param>
    /// <param name="outcome">Expected outcome, also the severity of the expected message.</param>
    /// <param name="text">Expected <c>PATIENTNO</c> text, verbatim; null when no message is expected.</param>
    [Theory]
    [Trait("Rule", RuleId)]
    [InlineData(nameof(PatientCoverageSnapshot.ContractEnd), "2026-09-28T00:00:00", "2026-09-28T15:30:00", 2, PassOutcome, null)]
    [InlineData(nameof(PatientCoverageSnapshot.ContractEnd), "2026-09-27T00:00:00", "2026-09-28T00:00:01", 2, ValidationMessage.Blocking, "Contract  Ended 27/09/2026")]
    [InlineData(nameof(PatientCoverageSnapshot.CardEnd), "2026-09-28T00:00:00", "2026-09-28T18:30:00", 2, ValidationMessage.Warning, "Card Expired 28/09/2026 , Today last Date")]
    [InlineData(nameof(PatientCoverageSnapshot.CardEnd), "2026-09-28T00:00:00", "2026-09-28T18:30:00", 1, ValidationMessage.Warning, "Card Expired 28/09/2026 , Today last Date")]
    [InlineData(nameof(PatientCoverageSnapshot.CardEnd), "2026-09-27T00:00:00", "2026-09-28T00:00:01", 2, ValidationMessage.Blocking, "Card Expired 27/09/2026")]
    [InlineData(nameof(PatientCoverageSnapshot.CardEnd), "2026-09-28T18:00:00", "2026-09-28T10:15:00", 2, PassOutcome, null)]
    [InlineData(nameof(PatientCoverageSnapshot.SubCompanyContractEnd), "2026-09-28T00:00:00", "2026-09-28T23:59:59", 2, PassOutcome, null)]
    [InlineData(nameof(PatientCoverageSnapshot.SubCompanyContractEnd), "2026-09-27T00:00:00", "2026-09-28T23:59:59", 2, ValidationMessage.Blocking, "Policy  Ended 27/09/2026 Patient well treated as cash patient ")]
    public void Evaluate_compares_end_dates_against_the_draft_day(
        string endField,
        string endDate,
        string draftTime,
        int invDateAdmin,
        string outcome,
        string? text)
    {
        var end = DateTime.ParseExact(endDate, "s", CultureInfo.InvariantCulture);
        var coverage = endField switch
        {
            nameof(PatientCoverageSnapshot.ContractEnd) => CardCompanyCoverage() with { ContractEnd = end },
            nameof(PatientCoverageSnapshot.CardEnd) => CardCompanyCoverage() with { CardEnd = end },
            nameof(PatientCoverageSnapshot.SubCompanyContractEnd) => CardCompanyCoverage() with { SubCompanyContractEnd = end },
            _ => throw new ArgumentOutOfRangeException(nameof(endField), endField, "Not a DR-03 coverage end date."),
        };
        var parameters = new InvoiceEntryParameters { InvDateAdmin = invDateAdmin };

        var result = PatientEligibilityRules.Evaluate(coverage, parameters, DateTime.ParseExact(draftTime, "s", CultureInfo.InvariantCulture));

        ParityFixture.AssertExact(outcome, Outcome(result));
        ParityFixture.AssertMessages(
            text is null ? [] : [new FixtureMessage("PATIENTNO", text, outcome)],
            result.Messages.Select(m => (m.Field, m.Text, m.Severity)));
        Assert.All(result.Messages, m => Assert.Equal(RuleId, m.Rule));
        Assert.Empty(result.Adjusted);
    }

    /// <summary>Theory rows pairing each DR-03 fixture case name with each draft time of day, in file order.</summary>
    /// <returns>One row per case and time, holding the case name and the time as <c>HH:mm:ss</c>.</returns>
    public static IEnumerable<object[]> CaseNamesAtDraftTimesOfDay() =>
        ParityFixture.CaseNames(RuleId)
            .SelectMany(row => DraftTimesOfDay.Select(time => new object[] { row[0], time }))
            .ToArray();

    private static string Outcome(RuleResult result) =>
        result.IsBlocking
            ? ValidationMessage.Blocking
            : result.Messages.Any(m => m.Severity == ValidationMessage.Warning)
                ? ValidationMessage.Warning
                : PassOutcome;

    private static PatientCoverageSnapshot CardCompanyCoverage() => new()
    {
        CompCode = "205",
        CompanyType = 2,
        ContractEnd = new DateTime(2026, 12, 31),
        CompanyIsActive = 1,
        CardEnd = new DateTime(2026, 12, 31),
        SubCompCode = "305",
        SubCompanyContractEnd = new DateTime(2026, 12, 31),
        SubCompanyIsActive = 1,
        MyClass = 1,
        ClassWithRef = 0,
        ClassIsActive = 1,
    };

    private static (PatientCoverageSnapshot? Coverage, InvoiceEntryParameters Parameters, DateTime DraftDate) ReadInput(FixtureCase fixtureCase)
    {
        var input = fixtureCase.Input.Deserialize<CaseInput>(ParityFixture.JsonOptions)
            ?? throw new InvalidDataException($"Fixture '{RuleId}', case '{fixtureCase.Name}': input is null.");
        var draftDate = input.DraftDate
            ?? throw new InvalidDataException($"Fixture '{RuleId}', case '{fixtureCase.Name}': input.draftDate is missing.");
        return (input.Coverage, input.Parameters ?? new InvoiceEntryParameters(), draftDate);
    }

    /// <summary>DR-03 case input.</summary>
    /// <param name="Coverage">V_PAT_DATA coverage snapshot; null when the patient has none.</param>
    /// <param name="Parameters">Entry parameters supplying INV_DATE_ADMIN and CASH_OR_CREDIT; the Form defaults when absent.</param>
    /// <param name="DraftDate">Draft invoice date (INVDATE), ISO 8601.</param>
    private sealed record CaseInput(PatientCoverageSnapshot? Coverage, InvoiceEntryParameters? Parameters, DateTime? DraftDate);
}
