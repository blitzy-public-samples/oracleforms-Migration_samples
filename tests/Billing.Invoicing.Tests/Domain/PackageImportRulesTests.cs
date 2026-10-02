using System.Text.Json;
using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Domain.Rules;
using Billing.Invoicing.Tests.Parity;

namespace Billing.Invoicing.Tests.Domain;

/// <summary>DR-19 parity: <see cref="PackageImportRules.Evaluate"/> against the DR-19 fixture.</summary>
[Trait("Category", "DomainParity")]
public sealed class PackageImportRulesTests
{
    private const string RuleId = "DR-19";
    private const string BlockingOutcome = "Blocking";
    private const string WarningOutcome = "Warning";
    private const string PassOutcome = "Pass";
    private const string T089Locator = "05_Complex/Inv_Small_Cash.xml:771";

    /// <summary>Compares <see cref="PackageImportRules.Evaluate"/>, including its exact adjusted values, with one DR-19 fixture case.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", "DR-19")]
    [MemberData(nameof(ParityFixture.CaseNames), RuleId, MemberType = typeof(ParityFixture))]
    public void Evaluate_matches_DR_19(string caseName)
    {
        var fixtureCase = ParityFixture.Case(RuleId, caseName);
        Assert.True(ParityFixture.IsDerivable(fixtureCase), $"Fixture '{RuleId}', case '{caseName}' is not derivable.");

        var expectedMessages = fixtureCase.Expected.Messages
            ?? throw new InvalidDataException($"Fixture '{RuleId}', case '{caseName}': expected.messages is missing.");
        var input = ReadInput(fixtureCase);

        var result = PackageImportRules.Evaluate(input.LineCount);

        ParityFixture.AssertExact(fixtureCase.Expected.Outcome, Outcome(result));
        ParityFixture.AssertMessages(
            expectedMessages,
            result.Messages.Select(message => (message.Field, message.Text, message.Severity)));
        Assert.All(result.Messages, message => Assert.Equal(RuleId, message.Rule));
        Assert.False(result.IsBlocking);

        if (fixtureCase.Expected.Values is { } values)
        {
            var expectedKeys = values.EnumerateObject().Select(property => property.Name.ToUpperInvariant()).Order(StringComparer.Ordinal).ToList();
            var actualKeys = result.Adjusted.Keys.Select(key => key.ToUpperInvariant()).Order(StringComparer.Ordinal).ToList();
            Assert.Equal(expectedKeys, actualKeys);
            ParityFixture.AssertValues(values, result.Adjusted, ParityFixture.Load(RuleId).Compare);
        }
        else
        {
            Assert.Empty(result.Adjusted);
        }
    }

    /// <summary>Checks that the DR-19 fixture is a domain fixture traced to T089 holding a zero-line warning case and a pass case.</summary>
    [Fact]
    [Trait("Rule", "DR-19")]
    public void Fixture_DR_19_holds_warning_and_pass_cases()
    {
        var document = ParityFixture.Load(RuleId);

        Assert.Equal(ParityFixture.DomainClass, document.Class);
        Assert.NotEmpty(document.Cases);
        Assert.Contains(document.Source, source => string.Equals(source.Locator, T089Locator, StringComparison.Ordinal));

        var cases = document.Cases
            .Select(fixtureCase => (Input: ReadInput(fixtureCase), fixtureCase.Expected.Outcome))
            .ToList();

        Assert.Contains(cases, c => c.Input.LineCount <= 0 && string.Equals(c.Outcome, WarningOutcome, StringComparison.Ordinal));
        Assert.Contains(cases, c => c.Input.LineCount > 0 && string.Equals(c.Outcome, PassOutcome, StringComparison.Ordinal));
    }

    private static string Outcome(RuleResult r) =>
        r.IsBlocking ? BlockingOutcome
        : r.Messages.Count > 0 ? WarningOutcome
        : PassOutcome;

    private static PackageImportInput ReadInput(FixtureCase fixtureCase) =>
        fixtureCase.Input.Deserialize<PackageImportInput>(ParityFixture.JsonOptions)
        ?? throw new InvalidDataException($"Fixture '{RuleId}', case '{fixtureCase.Name}': input is null.");

    /// <summary>Input of a DR-19 fixture case.</summary>
    private sealed record PackageImportInput
    {
        /// <summary>Number of lines the package import returned.</summary>
        public required int LineCount { get; init; }
    }
}
