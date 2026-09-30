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

    private const string InvDateItemLocator = "05_Complex/Inv_Small_Cash.xml:16";

    private const string T015Locator = "05_Complex/Inv_Small_Cash.xml:367";

    private static readonly DateTime DraftDate = new(2026, 9, 28);

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

    /// <summary>The DR-03 fixture is a domain fixture derived from T023, INVDATE and T015, with derivable and daytime cases.</summary>
    [Fact]
    [Trait("Rule", RuleId)]
    public void Fixture_is_a_domain_fixture_with_derivable_cases()
    {
        var fixture = ParityFixture.Load(RuleId);

        Assert.Equal(RuleId, fixture.Id);
        Assert.Equal(ParityFixture.DomainClass, fixture.Class);
        Assert.NotEmpty(fixture.Cases);
        Assert.All(fixture.Cases, fixtureCase => Assert.True(ParityFixture.IsDerivable(fixtureCase), fixtureCase.Name));
        Assert.All(
            new[] { T023Locator, InvDateItemLocator, T015Locator },
            locator => Assert.Contains(fixture.Source, source => string.Equals(source.Locator, locator, StringComparison.Ordinal)));
        Assert.Contains(fixture.Cases, fixtureCase => ReadInput(fixtureCase).DraftDate.TimeOfDay != TimeSpan.Zero);
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
