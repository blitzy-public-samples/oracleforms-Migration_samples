using System.Text.Json;
using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Domain.Rules;
using Billing.Invoicing.Tests.Parity;

namespace Billing.Invoicing.Tests.Domain;

/// <summary>DR-02 unit parity tests of <see cref="InvoiceDetailRules"/> against the T009 fixture.</summary>
[Trait("Category", "DomainParity")]
public sealed class InvoiceDetailRulesTests
{
    private const string RuleId = "DR-02";

    private const string LineCountKey = "lineCount";

    private const string BlockingOutcome = "Blocking";

    private const string WarningOutcome = "Warning";

    private const string PassOutcome = "Pass";

    private const string T009Locator = "05_Complex/Inv_Small_Cash.xml:1102";

    /// <summary>Each fixture case yields its expected outcome and messages from <see cref="InvoiceDetailRules.RequireDetails"/>.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", RuleId)]
    [MemberData(nameof(ParityFixture.CaseNames), RuleId, MemberType = typeof(ParityFixture))]
    public void RequireDetails_matches_DR_02(string caseName)
    {
        var fixtureCase = ParityFixture.Case(RuleId, caseName);
        Assert.True(ParityFixture.IsDerivable(fixtureCase), $"Fixture '{RuleId}', case '{caseName}' is not derivable.");

        var result = InvoiceDetailRules.RequireDetails(ReadLineCount(fixtureCase));

        var expected = fixtureCase.Expected;
        Assert.NotNull(expected.Messages);
        ParityFixture.AssertExact(expected.Outcome, Outcome(result));
        ParityFixture.AssertMessages(
            expected.Messages,
            result.Messages.Select(message => (message.Field, message.Text, message.Severity)));
        Assert.All(result.Messages, message => Assert.Equal(RuleId, message.Rule));
    }

    /// <summary>The DR-02 fixture is a domain fixture derived from T009 with at least one derivable case.</summary>
    [Fact]
    [Trait("Rule", RuleId)]
    public void Fixture_is_a_domain_fixture_with_derivable_cases()
    {
        var fixture = ParityFixture.Load(RuleId);

        Assert.Equal(RuleId, fixture.Id);
        Assert.Equal(ParityFixture.DomainClass, fixture.Class);
        Assert.NotEmpty(fixture.Cases);
        Assert.All(fixture.Cases, fixtureCase => Assert.True(ParityFixture.IsDerivable(fixtureCase), fixtureCase.Name));
        Assert.Contains(fixture.Source, source => string.Equals(source.Locator, T009Locator, StringComparison.Ordinal));
    }

    private static int ReadLineCount(FixtureCase fixtureCase)
    {
        Assert.True(
            fixtureCase.Input.TryGetProperty(LineCountKey, out var lineCount) && lineCount.ValueKind == JsonValueKind.Number,
            $"Case '{fixtureCase.Name}' has no numeric {LineCountKey} input.");
        return lineCount.GetInt32();
    }

    private static string Outcome(RuleResult result)
    {
        if (result.IsBlocking)
        {
            return BlockingOutcome;
        }

        return result.Messages.Any(message => message.Severity == ValidationMessage.Warning) ? WarningOutcome : PassOutcome;
    }
}
