using System.Text.Json;
using System.Text.Json.Serialization;
using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Domain.Rules;
using Billing.Invoicing.Tests.Parity;

namespace Billing.Invoicing.Tests.Domain;

/// <summary>DR-24 parity: the pay type <see cref="PayTypeSelectionRule.Decide"/> returns for each fixture case (T015, T023, T026, MAKE_CASH).</summary>
[Trait("Category", "DomainParity")]
public sealed class PayTypeSelectionRuleTests
{
    private const string RuleId = "DR-24";
    private const string PassOutcome = "Pass";
    private const string PayTypeKey = "PAYTYPE";
    private const int CashPayType = 1;
    private const int CreditPayType = 2;

    private static readonly string[] SourceLocators =
    [
        "05_Complex/Inv_Small_Cash.xml:18",
        "05_Complex/Inv_Small_Cash.xml:27",
        "05_Complex/Inv_Small_Cash.xml:367",
    ];

    private static readonly JsonSerializerOptions InputOptions = new(ParityFixture.JsonOptions)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Checks the decided pay type, the outcome and the messages of one DR-24 case.</summary>
    /// <param name="caseName">Name of the fixture case.</param>
    [Theory]
    [Trait("Rule", RuleId)]
    [MemberData(nameof(ParityFixture.CaseNames), RuleId, MemberType = typeof(ParityFixture))]
    public void Decide_matches_DR_24(string caseName)
    {
        var fixture = ParityFixture.Load(RuleId);
        var fixtureCase = ParityFixture.Case(RuleId, caseName);
        Assert.True(ParityFixture.IsDerivable(fixtureCase), $"Case '{caseName}' is not derivable.");
        var input = ReadInput(fixtureCase);

        var payType = PayTypeSelectionRule.Decide(
            input.CompCode,
            input.CompanyType,
            input.Parameters ?? new InvoiceEntryParameters(),
            input.ClaimPreload);

        ParityFixture.AssertExact(fixtureCase.Expected.Outcome, PassOutcome);
        ParityFixture.AssertMessages(fixtureCase.Expected.Messages!, []);
        Assert.True(fixtureCase.Expected.Values.HasValue, $"Case '{caseName}' has no expected values.");
        ParityFixture.AssertValues(
            fixtureCase.Expected.Values.Value,
            new Dictionary<string, object?>(StringComparer.Ordinal) { [PayTypeKey] = payType },
            fixture.Compare);
    }

    /// <summary>Checks that the DR-24 fixture is a domain fixture traced to T015, T023 and T026 whose cases expect both cash and credit.</summary>
    [Fact]
    [Trait("Rule", RuleId)]
    public void Fixture_DR_24_expects_cash_and_credit()
    {
        var fixture = ParityFixture.Load(RuleId);

        Assert.Equal(ParityFixture.DomainClass, fixture.Class);
        Assert.NotEmpty(fixture.Cases);
        foreach (var locator in SourceLocators)
        {
            Assert.Contains(fixture.Source, source => string.Equals(source.Locator, locator, StringComparison.Ordinal));
        }

        var expectedPayTypes = new HashSet<int>();
        foreach (var fixtureCase in fixture.Cases)
        {
            var payType = ExpectedPayType(fixtureCase);
            Assert.True(
                payType is CashPayType or CreditPayType,
                $"Case '{fixtureCase.Name}' expects {PayTypeKey} {payType}, not {CashPayType} or {CreditPayType}.");
            expectedPayTypes.Add(payType);
        }

        Assert.Equal(new[] { CashPayType, CreditPayType }, expectedPayTypes.Order());
    }

    private static PayTypeInput ReadInput(FixtureCase fixtureCase)
    {
        try
        {
            return fixtureCase.Input.Deserialize<PayTypeInput>(InputOptions)
                ?? throw new InvalidDataException($"Case '{fixtureCase.Name}': input is null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Case '{fixtureCase.Name}': {ex.Message}", ex);
        }
    }

    private static int ExpectedPayType(FixtureCase fixtureCase)
    {
        if (fixtureCase.Expected.Values is not { ValueKind: JsonValueKind.Object } values
            || !values.TryGetProperty(PayTypeKey, out var payType)
            || payType.ValueKind != JsonValueKind.Number
            || !payType.TryGetInt32(out var value))
        {
            Assert.Fail($"Case '{fixtureCase.Name}' has no integer {PayTypeKey}.");
            return 0;
        }

        return value;
    }

    private sealed record PayTypeInput(
        [property: JsonRequired] string? CompCode,
        [property: JsonRequired] int? CompanyType,
        InvoiceEntryParameters? Parameters,
        InvoiceHeaderDraft? ClaimPreload);
}
