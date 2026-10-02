using System.Text.Json;
using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Domain.Rules;
using Billing.Invoicing.Tests.Parity;

namespace Billing.Invoicing.Tests.Domain;

/// <summary>DR-10 unit parity tests of <see cref="ClaimNumberRule"/> against the T029 fixture.</summary>
[Trait("Category", "DomainParity")]
public sealed class ClaimNumberRuleTests
{
    private const string RuleId = "DR-10";

    private const string ClaimNoKey = "CLAIM_NO";

    private const string PassOutcome = "Pass";

    private const string T029Locator = "05_Complex/Inv_Small_Cash.xml:43";

    /// <summary>Each fixture case yields its expected CLAIM_NO, a Pass outcome and no messages.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", RuleId)]
    [MemberData(nameof(ParityFixture.CaseNames), RuleId, MemberType = typeof(ParityFixture))]
    public void Build_matches_DR_10(string caseName)
    {
        var fixture = ParityFixture.Load(RuleId);
        var fixtureCase = ParityFixture.Case(RuleId, caseName);
        var (header, parameters) = ReadInput(fixtureCase);

        var claimNo = ClaimNumberRule.Build(header, parameters);

        var expected = fixtureCase.Expected;
        Assert.NotNull(expected.Messages);
        ParityFixture.AssertExact(expected.Outcome, PassOutcome);
        ParityFixture.AssertMessages(expected.Messages, Array.Empty<(string? Field, string Text, string Severity)>());

        var values = Assert.IsType<JsonElement>(expected.Values);
        Assert.True(values.TryGetProperty(ClaimNoKey, out _), $"Case '{caseName}' has no expected {ClaimNoKey}.");
        ParityFixture.AssertValues(
            values,
            new Dictionary<string, object?>(StringComparer.Ordinal) { [ClaimNoKey] = claimNo },
            fixture.Compare);
    }

    /// <summary>The DR-10 fixture is a domain fixture derived from T029 with at least one derivable case.</summary>
    [Fact]
    [Trait("Rule", RuleId)]
    public void Fixture_is_a_domain_fixture_with_derivable_cases()
    {
        var fixture = ParityFixture.Load(RuleId);

        Assert.Equal(RuleId, fixture.Id);
        Assert.Equal(ParityFixture.DomainClass, fixture.Class);
        Assert.NotEmpty(fixture.Cases);
        Assert.All(fixture.Cases, fixtureCase => Assert.True(ParityFixture.IsDerivable(fixtureCase), fixtureCase.Name));
        Assert.Contains(fixture.Source, source => string.Equals(source.Locator, T029Locator, StringComparison.Ordinal));
    }

    private static (InvoiceHeaderDraft Header, InvoiceEntryParameters Parameters) ReadInput(FixtureCase fixtureCase)
    {
        var input = fixtureCase.Input.Deserialize<CaseInput>(ParityFixture.JsonOptions);
        Assert.NotNull(input);
        Assert.NotNull(input.Header);
        return (input.Header, input.Parameters ?? new InvoiceEntryParameters());
    }

    /// <summary>DR-10 case input: the draft header and the entry parameters.</summary>
    /// <param name="Header">Draft header supplying PATIENTNO, CLINICID and the draft date.</param>
    /// <param name="Parameters">Entry parameters supplying CLAIM_FLAG and CLAIM_NO; the Form defaults when absent.</param>
    private sealed record CaseInput(InvoiceHeaderDraft? Header, InvoiceEntryParameters? Parameters);
}
