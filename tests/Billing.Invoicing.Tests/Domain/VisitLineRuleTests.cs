using System.Text.Json;
using System.Text.Json.Serialization;
using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Domain.Rules;
using Billing.Invoicing.Tests.Parity;

namespace Billing.Invoicing.Tests.Domain;

/// <summary>DR-25 parity: the automatic visit line <see cref="VisitLineRule.Choose"/> picks for each fixture case (T029).</summary>
[Trait("Category", "DomainParity")]
public sealed class VisitLineRuleTests
{
    private const string RuleId = "DR-25";
    private const string PassOutcome = "Pass";
    private const string KindKey = "VISIT_LINE_KIND";
    private const string ServiceIdKey = "VISIT_LINE_SERVICEID";
    private const string FixedServiceId = "2000";

    /// <summary>Checks the chosen kind and service, the outcome and the messages of one DR-25 case.</summary>
    /// <param name="caseName">Name of the fixture case.</param>
    [Theory]
    [Trait("Rule", RuleId)]
    [MemberData(nameof(ParityFixture.CaseNames), RuleId, MemberType = typeof(ParityFixture))]
    public void Choose_matches_DR_25(string caseName)
    {
        var fixture = ParityFixture.Load(RuleId);
        var fixtureCase = ParityFixture.Case(RuleId, caseName);
        Assert.True(ParityFixture.IsDerivable(fixtureCase), $"Case '{caseName}' is not derivable.");
        var input = ReadInput(fixtureCase);

        var choice = VisitLineRule.Choose(input.DoReview, input.ClaimParam, input.CompCode, input.ClinicId);

        ParityFixture.AssertExact(fixtureCase.Expected.Outcome, PassOutcome);
        ParityFixture.AssertMessages(fixtureCase.Expected.Messages!, []);
        Assert.True(fixtureCase.Expected.Values.HasValue, $"Case '{caseName}' has no expected values.");
        ParityFixture.AssertValues(
            fixtureCase.Expected.Values.Value,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [KindKey] = choice.Kind,
                [ServiceIdKey] = choice.ServiceId,
            },
            fixture.Compare);
    }

    /// <summary>Checks that the DR-25 fixture is a domain fixture whose cases expect only known visit-line kinds, each at least once.</summary>
    [Fact]
    [Trait("Rule", RuleId)]
    public void Fixture_DR_25_expects_known_visit_line_kinds()
    {
        var fixture = ParityFixture.Load(RuleId);
        var knownKinds = new[]
            {
                VisitLineChoice.None,
                VisitLineChoice.Consultation,
                VisitLineChoice.Review,
                VisitLineChoice.FixedService(FixedServiceId),
            }
            .Select(choice => choice.Kind)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(ParityFixture.DomainClass, fixture.Class);
        Assert.NotEmpty(fixture.Cases);

        var expectedKinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fixtureCase in fixture.Cases)
        {
            var kind = ExpectedKind(fixtureCase);
            Assert.True(knownKinds.Contains(kind), $"Case '{fixtureCase.Name}' expects unknown {KindKey} '{kind}'.");
            expectedKinds.Add(kind);
        }

        Assert.Equal(knownKinds.Order(StringComparer.Ordinal), expectedKinds.Order(StringComparer.Ordinal));
    }

    private static VisitLineInput ReadInput(FixtureCase fixtureCase)
    {
        try
        {
            return fixtureCase.Input.Deserialize<VisitLineInput>(ParityFixture.JsonOptions)
                ?? throw new InvalidDataException($"Case '{fixtureCase.Name}': input is null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Case '{fixtureCase.Name}': {ex.Message}", ex);
        }
    }

    private static string ExpectedKind(FixtureCase fixtureCase)
    {
        if (fixtureCase.Expected.Values is not { ValueKind: JsonValueKind.Object } values
            || !values.TryGetProperty(KindKey, out var kind)
            || kind.ValueKind != JsonValueKind.String)
        {
            Assert.Fail($"Case '{fixtureCase.Name}' has no string {KindKey}.");
            return string.Empty;
        }

        return kind.GetString()!;
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record VisitLineInput(
        [property: JsonRequired] string? DoReview,
        [property: JsonRequired] string? ClaimParam,
        [property: JsonRequired] string? CompCode,
        [property: JsonRequired] int? ClinicId);
}
