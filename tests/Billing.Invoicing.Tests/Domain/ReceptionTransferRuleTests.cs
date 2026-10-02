using System.Text.Json;
using Billing.Invoicing.Domain.Rules;
using Billing.Invoicing.Tests.Parity;

namespace Billing.Invoicing.Tests.Domain;

/// <summary>DR-21 parity: <see cref="ReceptionTransferRule.ShouldClear"/> against the DR-21 fixture.</summary>
[Trait("Category", "DomainParity")]
public sealed class ReceptionTransferRuleTests
{
    private const string RuleId = "DR-21";
    private const string ShouldClearKey = "SHOULD_CLEAR";
    private const string PassOutcome = "Pass";
    private const string T011Locator = "05_Complex/Inv_Small_Cash.xml:363";

    /// <summary>Compares <see cref="ReceptionTransferRule.ShouldClear"/> with one DR-21 fixture case.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", "DR-21")]
    [MemberData(nameof(ParityFixture.CaseNames), RuleId, MemberType = typeof(ParityFixture))]
    public void ShouldClear_matches_DR_21(string caseName)
    {
        var document = ParityFixture.Load(RuleId);
        var fixtureCase = ParityFixture.Case(RuleId, caseName);
        Assert.True(ParityFixture.IsDerivable(fixtureCase), $"Fixture '{RuleId}', case '{caseName}' is not derivable.");

        var expectedMessages = fixtureCase.Expected.Messages
            ?? throw new InvalidDataException($"Fixture '{RuleId}', case '{caseName}': expected.messages is missing.");
        if (fixtureCase.Expected.Values is not { } expectedValues)
        {
            throw new InvalidDataException($"Fixture '{RuleId}', case '{caseName}': expected.values is missing.");
        }

        var input = ReadInput(fixtureCase);

        var shouldClear = ReceptionTransferRule.ShouldClear(input.IsReplay, input.HasNewInvDocId);

        ParityFixture.AssertExact(fixtureCase.Expected.Outcome, PassOutcome);
        ParityFixture.AssertMessages(expectedMessages, []);
        ParityFixture.AssertValues(
            expectedValues,
            new Dictionary<string, object?> { [ShouldClearKey] = shouldClear },
            document.Compare);
    }

    /// <summary>Checks that the DR-21 fixture is a domain fixture traced to T011 holding the clear, null-doc-id and replay cases.</summary>
    [Fact]
    [Trait("Rule", "DR-21")]
    public void Fixture_DR_21_holds_clear_null_doc_id_and_replay_cases()
    {
        var document = ParityFixture.Load(RuleId);

        Assert.Equal(ParityFixture.DomainClass, document.Class);
        Assert.NotEmpty(document.Cases);
        Assert.Contains(document.Source, source => string.Equals(source.Locator, T011Locator, StringComparison.Ordinal));

        var cases = document.Cases
            .Select(fixtureCase => (Input: ReadInput(fixtureCase), ShouldClear: ReadExpectedShouldClear(fixtureCase)))
            .ToList();

        Assert.Contains(cases, c => !c.Input.IsReplay && c.Input.HasNewInvDocId && c.ShouldClear);
        Assert.Contains(cases, c => !c.Input.IsReplay && !c.Input.HasNewInvDocId && !c.ShouldClear);
        Assert.Contains(cases, c => c.Input.IsReplay && c.Input.HasNewInvDocId && !c.ShouldClear);
    }

    private static ReceptionTransferInput ReadInput(FixtureCase fixtureCase) =>
        fixtureCase.Input.Deserialize<ReceptionTransferInput>(ParityFixture.JsonOptions)
        ?? throw new InvalidDataException($"Fixture '{RuleId}', case '{fixtureCase.Name}': input is null.");

    private static bool ReadExpectedShouldClear(FixtureCase fixtureCase)
    {
        if (fixtureCase.Expected.Values is { } values
            && values.TryGetProperty(ShouldClearKey, out var shouldClear)
            && shouldClear.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return shouldClear.GetBoolean();
        }

        throw new InvalidDataException(
            $"Fixture '{RuleId}', case '{fixtureCase.Name}': expected.values.{ShouldClearKey} is not a boolean.");
    }

    /// <summary>Input of a DR-21 fixture case.</summary>
    private sealed record ReceptionTransferInput
    {
        /// <summary>Whether the create replays an existing request id.</summary>
        public required bool IsReplay { get; init; }

        /// <summary>Whether the patient's <c>NEW_INV_DOCID</c> is set.</summary>
        public required bool HasNewInvDocId { get; init; }
    }
}
