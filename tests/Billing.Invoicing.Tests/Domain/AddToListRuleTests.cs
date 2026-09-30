using System.Text.Json;
using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Domain.Rules;
using Billing.Invoicing.Tests.Parity;

namespace Billing.Invoicing.Tests.Domain;

/// <summary>DR-23 parity tests of <see cref="AddToListRule.Derive"/> against <c>Parity/fixtures/DR-23.json</c>.</summary>
[Trait("Category", "DomainParity")]
public sealed class AddToListRuleTests
{
    private const string FixtureId = "DR-23";

    private const string AddToListKey = "ADD_TO_LIST";

    private const string ProfilesKey = "profiles";

    private const string FlagSourceKey = "flagSource";

    private const string ExpandedPath = "GetServiceQueueFlags";

    private const string UnexpandedPath = "GetPackageComponentFlags";

    private const string PassOutcome = "Pass";

    /// <summary>Derives <c>ADD_TO_LIST</c> for each fixture case and compares outcome, messages and value.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", "DR-23")]
    [MemberData(nameof(ParityFixture.CaseNames), FixtureId, MemberType = typeof(ParityFixture))]
    public void Derive_matches_DR_23(string caseName)
    {
        var document = ParityFixture.Load(FixtureId);
        var fixtureCase = ParityFixture.Case(FixtureId, caseName);
        Assert.True(ParityFixture.IsDerivable(fixtureCase), $"Case '{caseName}' must be {ParityFixture.Derivable}.");

        var actual = AddToListRule.Derive(Profiles(fixtureCase.Input));

        ParityFixture.AssertExact(fixtureCase.Expected.Outcome, PassOutcome);
        ParityFixture.AssertMessages(
            fixtureCase.Expected.Messages ?? Array.Empty<FixtureMessage>(),
            Array.Empty<(string? Field, string Text, string Severity)>());

        var values = ExpectedValues(fixtureCase);
        ParityFixture.AssertValues(values, new Dictionary<string, object?> { [AddToListKey] = actual }, document.Compare);
    }

    /// <summary>Checks that the DR-23 fixture is a domain fixture expecting both flag values on the direct, expanded and unexpanded paths.</summary>
    [Fact]
    [Trait("Rule", "DR-23")]
    public void Fixture_DR_23_expects_both_values_on_every_path()
    {
        var document = ParityFixture.Load(FixtureId);

        Assert.Equal(ParityFixture.DomainClass, document.Class);
        Assert.NotEmpty(document.Cases);

        var outcomes = document.Cases
            .Select(fixtureCase => (Path: FlagSource(fixtureCase.Input), Value: ExpectedAddToList(fixtureCase)))
            .ToList();

        Assert.All(outcomes, outcome => Assert.True(
            outcome.Path is null or ExpandedPath or UnexpandedPath,
            $"Unknown {FlagSourceKey} '{outcome.Path}'."));
        Assert.All(outcomes, outcome => Assert.True(
            outcome.Value is 0 or 1,
            $"{AddToListKey} must be 0 or 1, not {outcome.Value}."));

        var all = outcomes.Select(outcome => outcome.Value).ToList();
        Assert.Contains(0, all);
        Assert.Contains(1, all);

        Assert.Contains(1, outcomes.Where(outcome => outcome.Path is null).Select(outcome => outcome.Value));

        foreach (var path in new[] { ExpandedPath, UnexpandedPath })
        {
            var onPath = outcomes.Where(outcome => outcome.Path == path).Select(outcome => outcome.Value).ToList();
            Assert.Contains(0, onPath);
            Assert.Contains(1, onPath);
        }
    }

    /// <summary>Counts null <c>ADD_TO_QUE</c> flags and null component entries as not queued, per T066's <c>nvl(ADD_TO_QUE,0)=1</c>.</summary>
    [Fact]
    [Trait("Rule", "DR-23")]
    public void Derive_counts_null_flags_and_null_components_as_not_queued()
    {
        var package = new ServiceProfile
        {
            ServiceId = "P100",
            ServLocId = 14,
            Components = new ServiceProfile[] { null!, new() { ServiceId = "2001" } },
        };

        Assert.Equal(0, AddToListRule.Derive(new[] { package, new ServiceProfile { ServiceId = "1001" } }));
        Assert.Equal(1, AddToListRule.Derive(new[]
        {
            package with { Components = new ServiceProfile[] { null!, new() { ServiceId = "2002", AddToQue = 1 } } },
        }));
    }

    /// <summary>Reads the case's service profiles, with null component lists replaced by empty ones.</summary>
    /// <param name="input">Case input holding the <c>profiles</c> array.</param>
    /// <returns>The profiles passed to <see cref="AddToListRule.Derive"/>.</returns>
    private static List<ServiceProfile> Profiles(JsonElement input)
    {
        Assert.True(input.TryGetProperty(ProfilesKey, out var element), $"input.{ProfilesKey} is missing.");
        Assert.Equal(JsonValueKind.Array, element.ValueKind);

        var profiles = element.Deserialize<List<ServiceProfile?>>(ParityFixture.JsonOptions);
        Assert.NotNull(profiles);

        return profiles.Select(Normalise).ToList();
    }

    /// <summary>Returns the profile with its component list, recursively, never null.</summary>
    /// <param name="profile">Deserialized profile; must not be null.</param>
    /// <returns>The normalised profile.</returns>
    private static ServiceProfile Normalise(ServiceProfile? profile)
    {
        Assert.NotNull(profile);

        var components = profile.Components ?? Array.Empty<ServiceProfile>();
        return profile with { Components = components.Select(Normalise).ToArray() };
    }

    /// <summary>Returns the case's <c>flagSource</c>, or null for a case with no package component path.</summary>
    /// <param name="input">Case input.</param>
    /// <returns>The flag source name, or null.</returns>
    private static string? FlagSource(JsonElement input) =>
        input.TryGetProperty(FlagSourceKey, out var source) && source.ValueKind != JsonValueKind.Null
            ? source.GetString()
            : null;

    /// <summary>Returns the expected values object of a case, which must name <c>ADD_TO_LIST</c>.</summary>
    /// <param name="fixtureCase">Fixture case.</param>
    /// <returns>The expected values.</returns>
    private static JsonElement ExpectedValues(FixtureCase fixtureCase)
    {
        Assert.True(fixtureCase.Expected.Values.HasValue, $"Case '{fixtureCase.Name}' has no expected.values.");

        var values = fixtureCase.Expected.Values.Value;
        Assert.True(values.TryGetProperty(AddToListKey, out _), $"Case '{fixtureCase.Name}' does not expect {AddToListKey}.");
        return values;
    }

    /// <summary>Returns the expected <c>ADD_TO_LIST</c> of a case.</summary>
    /// <param name="fixtureCase">Fixture case.</param>
    /// <returns>The expected flag value.</returns>
    private static int ExpectedAddToList(FixtureCase fixtureCase) =>
        ExpectedValues(fixtureCase).GetProperty(AddToListKey).GetInt32();
}
