using System.Text.Json;
using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Domain.Rules;
using Billing.Invoicing.Tests.Parity;

namespace Billing.Invoicing.Tests.Domain;

/// <summary>DR-11 unit parity tests of <see cref="DoctorSelectionRules.Validate"/> against the T029 fixture.</summary>
[Trait("Category", "DomainParity")]
public sealed class DoctorSelectionRulesTests
{
    private const string RuleId = "DR-11";

    private const string DoctorKey = "DOCIDX";

    private const string BlockingOutcome = "Blocking";

    private const string WarningOutcome = "Warning";

    private const string PassOutcome = "Pass";

    private const string T029Locator = "05_Complex/Inv_Small_Cash.xml:43";

    /// <summary>Each fixture case yields its expected outcome, messages and DOCIDX reset, and never blocks.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", RuleId)]
    [MemberData(nameof(ParityFixture.CaseNames), RuleId, MemberType = typeof(ParityFixture))]
    public void Validate_matches_DR_11(string caseName)
    {
        var fixture = ParityFixture.Load(RuleId);
        var fixtureCase = ParityFixture.Case(RuleId, caseName);
        Assert.True(ParityFixture.IsDerivable(fixtureCase), $"Case '{caseName}' must be {ParityFixture.Derivable}.");
        var (header, parameters) = ReadInput(fixtureCase);

        var result = DoctorSelectionRules.Validate(header, parameters);

        var expected = fixtureCase.Expected;
        Assert.NotNull(expected.Messages);
        ParityFixture.AssertExact(expected.Outcome, Outcome(result));
        ParityFixture.AssertMessages(
            expected.Messages,
            result.Messages.Select(message => (message.Field, message.Text, message.Severity)));
        Assert.All(result.Messages, message => ParityFixture.AssertExact(RuleId, message.Rule));
        Assert.False(result.IsBlocking);

        if (expected.Values is { } values)
        {
            ParityFixture.AssertValues(values, result.Adjusted, fixture.Compare);
        }

        if (!ExpectsReset(fixtureCase))
        {
            Assert.DoesNotContain(
                result.Adjusted.Keys,
                key => string.Equals(key, DoctorKey, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>The DR-11 fixture is a derivable domain fixture from T029 holding reset, missing-doctor and pass cases.</summary>
    [Fact]
    [Trait("Rule", RuleId)]
    public void Fixture_DR_11_holds_reset_warning_and_pass_cases()
    {
        var fixture = ParityFixture.Load(RuleId);

        Assert.Equal(RuleId, fixture.Id);
        Assert.Equal(ParityFixture.DomainClass, fixture.Class);
        Assert.NotEmpty(fixture.Cases);
        Assert.All(fixture.Cases, fixtureCase => Assert.True(ParityFixture.IsDerivable(fixtureCase), fixtureCase.Name));
        Assert.Contains(fixture.Source, source => string.Equals(source.Locator, T029Locator, StringComparison.Ordinal));

        Assert.DoesNotContain(fixture.Cases, fixtureCase => fixtureCase.Expected.Outcome == BlockingOutcome);
        Assert.Contains(fixture.Cases, fixtureCase => fixtureCase.Expected.Outcome == WarningOutcome && ExpectsReset(fixtureCase));
        Assert.Contains(fixture.Cases, fixtureCase => fixtureCase.Expected.Outcome == WarningOutcome && !ExpectsReset(fixtureCase));
        Assert.Contains(fixture.Cases, fixtureCase => fixtureCase.Expected.Outcome == PassOutcome);
    }

    private static string Outcome(RuleResult result) =>
        result.IsBlocking ? BlockingOutcome : result.Messages.Count > 0 ? WarningOutcome : PassOutcome;

    private static bool ExpectsReset(FixtureCase fixtureCase) =>
        fixtureCase.Expected.Values is { } values && values.TryGetProperty(DoctorKey, out _);

    private static (InvoiceHeaderDraft Header, InvoiceEntryParameters Parameters) ReadInput(FixtureCase fixtureCase)
    {
        var input = fixtureCase.Input.Deserialize<CaseInput>(ParityFixture.JsonOptions);
        Assert.NotNull(input);
        Assert.NotNull(input.Header);
        return (input.Header, input.Parameters ?? new InvoiceEntryParameters());
    }

    /// <summary>DR-11 case input: the draft header and the entry parameters.</summary>
    /// <param name="Header">Draft header supplying DOCIDX.</param>
    /// <param name="Parameters">Entry parameters supplying THE_DOC; the Form defaults when absent.</param>
    private sealed record CaseInput(InvoiceHeaderDraft? Header, InvoiceEntryParameters? Parameters);
}
