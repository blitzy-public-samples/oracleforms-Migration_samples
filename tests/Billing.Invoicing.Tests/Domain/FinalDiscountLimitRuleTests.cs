using System.Text.Json;
using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Domain.Rules;
using Billing.Invoicing.Tests.Parity;

namespace Billing.Invoicing.Tests.Domain;

/// <summary>DR-06 unit parity tests of <see cref="FinalDiscountLimitRule"/> against the T039, T041 and DISC_ALERT fixture.</summary>
[Trait("Category", "DomainParity")]
public sealed class FinalDiscountLimitRuleTests
{
    private const string RuleId = "DR-06";

    private const string EvaluateMethod = "Evaluate";

    private const string ApplyChoiceMethod = "ApplyChoice";

    private const string BlockingOutcome = "Blocking";

    private const string WarningOutcome = "Warning";

    private const string PassOutcome = "Pass";

    private const string FinalDiscPercKey = "FINALDISC_PERC";

    private const int PercentMode = 1;

    private const int ValueMode = 0;

    private const string T039Locator = "05_Complex/Inv_Small_Cash.xml:74";

    private const string T041Locator = "05_Complex/Inv_Small_Cash.xml:78";

    private const string DiscAlertLocator = "05_Complex/Inv_Small_Cash.xml:8";

    /// <summary>Each fixture case yields its expected outcome, messages and adjusted FINALDISC_PERC, FINALDISC and DISC_T.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", RuleId)]
    [MemberData(nameof(ParityFixture.CaseNames), RuleId, MemberType = typeof(ParityFixture))]
    public void Final_discount_limit_matches_DR_06(string caseName)
    {
        var fixture = ParityFixture.Load(RuleId);
        var fixtureCase = ParityFixture.Case(RuleId, caseName);
        Assert.True(ParityFixture.IsDerivable(fixtureCase), $"Fixture '{RuleId}', case '{caseName}' is not derivable.");
        var input = ReadInput(fixtureCase);

        RuleResult result;
        switch (input.Method)
        {
            case EvaluateMethod:
                result = FinalDiscountLimitRule.Evaluate(input.Header, input.MaxDisc, input.PatPay);
                break;
            case ApplyChoiceMethod:
                result = FinalDiscountLimitRule.ApplyChoice(input.Header, ParseChoice(caseName, input.Choice), input.MaxDisc);
                break;
            default:
                Assert.Fail($"Fixture '{RuleId}', case '{caseName}': unknown method '{input.Method}'.");
                return;
        }

        var expected = fixtureCase.Expected;
        Assert.NotNull(expected.Messages);
        ParityFixture.AssertExact(expected.Outcome, Outcome(result));
        ParityFixture.AssertMessages(expected.Messages, result.Messages.Select(message => (message.Field, message.Text, message.Severity)));
        Assert.All(result.Messages, message => ParityFixture.AssertExact(RuleId, message.Rule));

        var expectedKeys = expected.Values is { } listed
            ? listed.EnumerateObject().Select(property => property.Name.ToUpperInvariant()).Order(StringComparer.Ordinal).ToList()
            : [];
        var actualKeys = result.Adjusted.Keys.Select(key => key.ToUpperInvariant()).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(expectedKeys, actualKeys);

        if (expected.Values is { } values)
        {
            ParityFixture.AssertValues(values, result.Adjusted, fixture.Compare);
        }

        if (input.Method == EvaluateMethod && result.Adjusted.TryGetValue(FinalDiscPercKey, out var percent))
        {
            var derived = Assert.IsType<decimal>(percent);
            Assert.Equal(decimal.Round(derived, 2, MidpointRounding.AwayFromZero), derived);
        }
    }

    /// <summary>The DR-06 fixture is a derivable domain fixture covering both methods, both modes, the offer bypass and both choices.</summary>
    [Fact]
    [Trait("Rule", RuleId)]
    public void Fixture_DR_06_covers_both_methods_modes_offer_and_choices()
    {
        var fixture = ParityFixture.Load(RuleId);

        Assert.Equal(RuleId, fixture.Id);
        Assert.Equal(ParityFixture.DomainClass, fixture.Class);
        Assert.NotEmpty(fixture.Cases);
        Assert.All(fixture.Cases, fixtureCase => Assert.True(ParityFixture.IsDerivable(fixtureCase), fixtureCase.Name));
        foreach (var locator in new[] { T039Locator, T041Locator, DiscAlertLocator })
        {
            Assert.Contains(fixture.Source, source => string.Equals(source.Locator, locator, StringComparison.Ordinal));
        }

        var cases = fixture.Cases.Select(fixtureCase => (fixtureCase.Name, Input: ReadInput(fixtureCase))).ToList();

        Assert.All(cases, c => Assert.Contains(c.Input.Method, new[] { EvaluateMethod, ApplyChoiceMethod }));
        Assert.Contains(cases, c => c.Input.Method == EvaluateMethod && c.Input.Header.DiscT == PercentMode && c.Input.Header.OferId is null);
        Assert.Contains(cases, c => c.Input.Method == EvaluateMethod && c.Input.Header.DiscT == ValueMode && c.Input.Header.OferId is null);
        Assert.Contains(cases, c => c.Input.Method == EvaluateMethod && c.Input.Header.OferId is not null);
        Assert.Contains(cases, c => c.Input.Method == ApplyChoiceMethod && ParseChoice(c.Name, c.Input.Choice) == DiscountLimitChoice.MaximumDiscount);
        Assert.Contains(cases, c => c.Input.Method == ApplyChoiceMethod && ParseChoice(c.Name, c.Input.Choice) == DiscountLimitChoice.Cancel);
    }

    /// <summary>T039: in percent mode an empty FINALDISC_PERC raises no alert and sets no item.</summary>
    [Fact]
    [Trait("Rule", RuleId)]
    public void Percent_mode_without_a_percent_passes()
    {
        var header = new InvoiceHeaderDraft { DiscT = PercentMode, FinalDiscPerc = null, FinalDisc = null, OferId = null };

        var result = FinalDiscountLimitRule.Evaluate(header, 10m, 200m);

        ParityFixture.AssertExact(PassOutcome, Outcome(result));
        Assert.Empty(result.Messages);
        Assert.Empty(result.Adjusted);
    }

    private static string Outcome(RuleResult result) =>
        result.IsBlocking
            ? BlockingOutcome
            : result.Messages.Any(message => message.Severity == ValidationMessage.Warning) ? WarningOutcome : PassOutcome;

    private static DiscountLimitChoice ParseChoice(string caseName, string? choice) =>
        choice is not null && Enum.GetNames<DiscountLimitChoice>().Contains(choice, StringComparer.Ordinal)
            ? Enum.Parse<DiscountLimitChoice>(choice)
            : throw new InvalidDataException($"Fixture '{RuleId}', case '{caseName}': choice '{choice}' is not a {nameof(DiscountLimitChoice)} name.");

    private static CaseInput ReadInput(FixtureCase fixtureCase)
    {
        CaseInput? input;
        try
        {
            input = fixtureCase.Input.Deserialize<CaseInput>(ParityFixture.JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Fixture '{RuleId}', case '{fixtureCase.Name}': input is malformed: {ex.Message}", ex);
        }

        if (input is null || input.Header is null || string.IsNullOrWhiteSpace(input.Method))
        {
            throw new InvalidDataException($"Fixture '{RuleId}', case '{fixtureCase.Name}': input needs a method and a header.");
        }

        return input;
    }

    /// <summary>DR-06 case input.</summary>
    private sealed record CaseInput
    {
        /// <summary>Rule method to call: Evaluate or ApplyChoice.</summary>
        public required string Method { get; init; }

        /// <summary>Draft header supplying DISC_T, FINALDISC_PERC, FINALDISC and OFERID.</summary>
        public required InvoiceHeaderDraft Header { get; init; }

        /// <summary>The operator's USERS_TABLE.MAX_DISC.</summary>
        public decimal? MaxDisc { get; init; }

        /// <summary>Patient share the value-mode percent is derived from.</summary>
        public decimal? PatPay { get; init; }

        /// <summary>DISC_ALERT button name for ApplyChoice.</summary>
        public string? Choice { get; init; }
    }
}
