using System.Text.Json;
using System.Text.Json.Serialization;
using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Domain.Workflow;
using Billing.Invoicing.Tests.Parity;

namespace Billing.Invoicing.Tests.Domain;

/// <summary>DR-17 parity: <see cref="InvoiceStatePolicy.CanEdit"/> and <see cref="InvoiceStatePolicy.CanDelete"/> against the DR-17 fixture.</summary>
[Trait("Category", "DomainParity")]
public sealed class InvoiceStatePolicyTests
{
    private const string RuleId = "DR-17";
    private const string CanEditMethod = "CanEdit";
    private const string CanDeleteMethod = "CanDelete";
    private const string CanEditKey = "CAN_EDIT";
    private const string PassOutcome = "Pass";
    private const string WarningOutcome = "Warning";
    private const string BlockingOutcome = "Blocking";

    /// <summary>Compares the policy method a DR-17 case names with that case's expected outcome, messages and values.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", "DR-17")]
    [MemberData(nameof(ParityFixture.CaseNames), RuleId, MemberType = typeof(ParityFixture))]
    public void State_policy_matches_DR_17(string caseName)
    {
        var document = ParityFixture.Load(RuleId);
        var fixtureCase = ParityFixture.Case(RuleId, caseName);
        Assert.True(ParityFixture.IsDerivable(fixtureCase), $"Fixture '{RuleId}', case '{caseName}' is not derivable.");

        var expectedMessages = fixtureCase.Expected.Messages
            ?? throw new InvalidDataException($"Fixture '{RuleId}', case '{caseName}': expected.messages is missing.");
        var input = ReadInput(fixtureCase);

        switch (input.Method)
        {
            case CanEditMethod:
                if (fixtureCase.Expected.Values is not { } expectedValues)
                {
                    throw new InvalidDataException($"Fixture '{RuleId}', case '{caseName}': expected.values is missing.");
                }

                var canEdit = InvoiceStatePolicy.CanEdit(input.InvNo);

                ParityFixture.AssertExact(fixtureCase.Expected.Outcome, PassOutcome);
                ParityFixture.AssertMessages(expectedMessages, []);
                ParityFixture.AssertValues(
                    expectedValues,
                    new Dictionary<string, object?>(StringComparer.Ordinal) { [CanEditKey] = canEdit },
                    document.Compare);
                break;

            case CanDeleteMethod:
                var result = InvoiceStatePolicy.CanDelete(input.InvNo);

                ParityFixture.AssertExact(fixtureCase.Expected.Outcome, Outcome(result));
                ParityFixture.AssertExact(BlockingOutcome, Outcome(result));
                ParityFixture.AssertMessages(
                    expectedMessages,
                    result.Messages.Select(message => (message.Field, message.Text, message.Severity)));
                Assert.All(result.Messages, message => ParityFixture.AssertExact(RuleId, message.Rule));
                if (fixtureCase.Expected.Values is { } deleteValues)
                {
                    var expectedKeys = deleteValues.EnumerateObject().Select(property => property.Name.ToUpperInvariant()).Order(StringComparer.Ordinal).ToList();
                    var actualKeys = result.Adjusted.Keys.Select(key => key.ToUpperInvariant()).Order(StringComparer.Ordinal).ToList();
                    Assert.Equal(expectedKeys, actualKeys);
                    ParityFixture.AssertValues(deleteValues, result.Adjusted, document.Compare);
                }
                else
                {
                    Assert.Empty(result.Adjusted);
                }

                break;

            default:
                Assert.Fail($"Fixture '{RuleId}', case '{caseName}': unknown input.method '{input.Method}'.");
                break;
        }
    }

    /// <summary>Checks that the DR-17 fixture is a domain fixture covering both methods, a refused saved-invoice edit and a new-draft edit.</summary>
    [Fact]
    [Trait("Rule", "DR-17")]
    public void Fixture_DR_17_covers_edit_and_delete_for_drafts_and_saved_invoices()
    {
        var document = ParityFixture.Load(RuleId);

        Assert.Equal(ParityFixture.DomainClass, document.Class);
        Assert.NotEmpty(document.Cases);

        var cases = document.Cases
            .Select(fixtureCase => (Case: fixtureCase, Input: ReadInput(fixtureCase)))
            .ToList();

        Assert.Contains(cases, c => c.Input.Method == CanEditMethod);
        Assert.Contains(cases, c => c.Input.Method == CanDeleteMethod);
        Assert.Contains(cases, c => c.Input.Method == CanEditMethod && c.Input.InvNo is not null && !ReadExpectedCanEdit(c.Case));
        Assert.Contains(cases, c => c.Input.Method == CanEditMethod && c.Input.InvNo is null && ReadExpectedCanEdit(c.Case));
        Assert.Contains(cases, c => c.Input.Method == CanDeleteMethod && c.Input.InvNo is null);
        Assert.Contains(cases, c => c.Input.Method == CanDeleteMethod && c.Input.InvNo is not null);
    }

    /// <summary>Maps a rule result to Blocking, Warning or Pass.</summary>
    /// <param name="r">The rule result.</param>
    /// <returns>The outcome name used by the fixtures.</returns>
    private static string Outcome(RuleResult r)
    {
        if (r.IsBlocking)
        {
            return BlockingOutcome;
        }

        return r.Messages.Any(message => message.Severity == ValidationMessage.Warning) ? WarningOutcome : PassOutcome;
    }

    private static StatePolicyInput ReadInput(FixtureCase fixtureCase)
    {
        try
        {
            return fixtureCase.Input.Deserialize<StatePolicyInput>(ParityFixture.JsonOptions)
                ?? throw new InvalidDataException($"Fixture '{RuleId}', case '{fixtureCase.Name}': input is null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Fixture '{RuleId}', case '{fixtureCase.Name}': {ex.Message}", ex);
        }
    }

    private static bool ReadExpectedCanEdit(FixtureCase fixtureCase)
    {
        if (fixtureCase.Expected.Values is { ValueKind: JsonValueKind.Object } values
            && values.TryGetProperty(CanEditKey, out var canEdit)
            && canEdit.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return canEdit.GetBoolean();
        }

        throw new InvalidDataException(
            $"Fixture '{RuleId}', case '{fixtureCase.Name}': expected.values.{CanEditKey} is not a boolean.");
    }

    /// <summary>Input of a DR-17 fixture case.</summary>
    /// <param name="Method">Policy method under test: <c>CanEdit</c> or <c>CanDelete</c>.</param>
    /// <param name="InvNo">Invoice number; null for an unsaved draft.</param>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record StatePolicyInput(
        [property: JsonRequired] string Method,
        [property: JsonRequired] long? InvNo);
}
