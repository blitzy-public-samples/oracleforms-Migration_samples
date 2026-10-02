using System.Text.Json;
using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Domain.Rules;
using Billing.Invoicing.Tests.Parity;

namespace Billing.Invoicing.Tests.Domain;

/// <summary>DR-07, DR-08 and DR-09 unit parity tests of <see cref="PaymentAllocationRules"/> against their fixtures.</summary>
[Trait("Category", "DomainParity")]
public sealed class PaymentAllocationRulesTests
{
    private const string AllocationRuleId = "DR-07";
    private const string RefundRuleId = "DR-08";
    private const string TotalCollectedRuleId = "DR-09";

    private const string Amount1Key = "AMOUNT_1";
    private const string Amount2Key = "AMOUNT_2";
    private const string ReundKey = "REUND";
    private const string TotalCollectedKey = "TOTAL_COLLECTED";

    private const string AllocateSecondAmountMethod = "AllocateSecondAmount";
    private const string ResetAfterDiscountChangeMethod = "ResetAfterDiscountChange";
    private const string DefaultFirstAmountMethod = "DefaultFirstAmount";

    private const string MethodInput = "method";
    private const string AmountDueInput = "amountDue";
    private const string Amount1Input = "amount1";
    private const string Amount2Input = "amount2";
    private const string AllocationInputName = "allocation";
    private const string LegacyInput = "legacy";
    private const string PackageInput = "package";

    private const string PassOutcome = "Pass";
    private const int T039RoundingDecimals = 2;
    private const int CreditPayType = 2;
    private const decimal RoundForCashCreditVatDefault = 2m;

    private const string T042Locator = "05_Complex/Inv_Small_Cash.xml:81";
    private const string Pu30Locator = "05_Complex/Inv_Small_Cash.xml:989";
    private const string CashCollectedFormulaLocator = "05_Complex/Inv_Small_Cash.xml:97";
    private const string RoundForCashLocator = "05_Complex/Inv_Small_Cash.xml:979";
    private const string NetFormulaLocator = "05_Complex/Inv_Small_Cash.xml:416";
    private const string PackageCashCollectedLocator = "05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:3058";

    /// <summary>Each DR-07 case yields its expected AMOUNT_1 / AMOUNT_2 from the method it names, a Pass outcome and no messages.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", AllocationRuleId)]
    [MemberData(nameof(ParityFixture.CaseNames), AllocationRuleId, MemberType = typeof(ParityFixture))]
    public void Allocation_matches_DR_07(string caseName)
    {
        var fixture = ParityFixture.Load(AllocationRuleId);
        var fixtureCase = ParityFixture.Case(AllocationRuleId, caseName);
        RequireInputProperties(AllocationRuleId, fixtureCase, MethodInput, AmountDueInput);
        var input = ReadInput<AllocationInput>(AllocationRuleId, fixtureCase);

        switch (input.Method)
        {
            case AllocateSecondAmountMethod:
            {
                RequireInputProperties(AllocationRuleId, fixtureCase, Amount1Input);
                var amount2 = PaymentAllocationRules.AllocateSecondAmount(input.AmountDue, input.Amount1);
                AssertCase(fixture, fixtureCase, PassOutcome, NoMessages(), SingleValue(Amount2Key, amount2), Amount2Key);
                break;
            }

            case ResetAfterDiscountChangeMethod:
            {
                var result = PaymentAllocationRules.ResetAfterDiscountChange(input.AmountDue);
                AssertCase(fixture, fixtureCase, OutcomeOf(result), MessagesOf(result), result.Adjusted, Amount1Key, Amount2Key);
                if (result.Adjusted.TryGetValue(Amount1Key, out var resetAmount1) && resetAmount1 is decimal amount1)
                {
                    Assert.Equal(decimal.Round(amount1, T039RoundingDecimals, MidpointRounding.AwayFromZero), amount1);
                }

                break;
            }

            case DefaultFirstAmountMethod:
            {
                var amount1 = PaymentAllocationRules.DefaultFirstAmount(input.AmountDue);
                AssertCase(fixture, fixtureCase, PassOutcome, NoMessages(), SingleValue(Amount1Key, amount1), Amount1Key);
                break;
            }

            default:
                throw new InvalidDataException(
                    $"Fixture '{AllocationRuleId}', case '{caseName}': input.{MethodInput} '{input.Method}' names no {nameof(PaymentAllocationRules)} method.");
        }
    }

    /// <summary>Each DR-08 case yields its expected REUND from <see cref="PaymentAllocationRules.Refund"/>, a Pass outcome and no messages.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", RefundRuleId)]
    [MemberData(nameof(ParityFixture.CaseNames), RefundRuleId, MemberType = typeof(ParityFixture))]
    public void Refund_matches_DR_08(string caseName)
    {
        var fixture = ParityFixture.Load(RefundRuleId);
        var fixtureCase = ParityFixture.Case(RefundRuleId, caseName);
        RequireInputProperties(RefundRuleId, fixtureCase, AllocationInputName);
        var allocation = ReadInput<RefundInput>(RefundRuleId, fixtureCase).Allocation
            ?? throw new InvalidDataException($"Fixture '{RefundRuleId}', case '{caseName}': input.{AllocationInputName} is null.");

        var reund = PaymentAllocationRules.Refund(allocation);

        AssertCase(fixture, fixtureCase, PassOutcome, NoMessages(), SingleValue(ReundKey, reund), ReundKey);
    }

    /// <summary>Each DR-09 case yields its expected TOTAL_COLLECTED from <see cref="PaymentAllocationRules.TotalCollected"/>, a Pass outcome and no messages.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", TotalCollectedRuleId)]
    [MemberData(nameof(ParityFixture.CaseNames), TotalCollectedRuleId, MemberType = typeof(ParityFixture))]
    public void TotalCollected_matches_DR_09(string caseName)
    {
        var fixture = ParityFixture.Load(TotalCollectedRuleId);
        var fixtureCase = ParityFixture.Case(TotalCollectedRuleId, caseName);
        RequireInputProperties(TotalCollectedRuleId, fixtureCase, Amount1Input, Amount2Input);
        var input = ReadInput<TotalCollectedInput>(TotalCollectedRuleId, fixtureCase);

        var totalCollected = PaymentAllocationRules.TotalCollected(input.Amount1, input.Amount2);

        AssertCase(fixture, fixtureCase, PassOutcome, NoMessages(), SingleValue(TotalCollectedKey, totalCollected), TotalCollectedKey);
    }

    /// <summary>The DR-07 fixture is a derivable domain fixture traced to T042 that names every allocation method and holds a case carrying legacy inputs.</summary>
    [Fact]
    [Trait("Rule", AllocationRuleId)]
    public void Fixture_DR_07_covers_each_method_and_a_legacy_credit_case()
    {
        var fixture = ParityFixture.Load(AllocationRuleId);
        AssertDomainFixture(AllocationRuleId, fixture, T042Locator);

        var methods = fixture.Cases
            .Select(fixtureCase => ReadInput<AllocationInput>(AllocationRuleId, fixtureCase).Method)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(AllocateSecondAmountMethod, methods);
        Assert.Contains(ResetAfterDiscountChangeMethod, methods);
        Assert.Contains(DefaultFirstAmountMethod, methods);

        Assert.Contains(
            fixture.Cases,
            fixtureCase => fixtureCase.Input.TryGetProperty(LegacyInput, out var legacy) && legacy.ValueKind == JsonValueKind.Object);
    }

    /// <summary>Each DR-07 legacy case records the legacy credit-branch NET and defaults AMOUNT_1 to the package amount due, which differs from it.</summary>
    [Fact]
    [Trait("Rule", AllocationRuleId)]
    public void Credit_null_patient_vat_case_records_the_legacy_net_and_allocates_the_package_amount_due()
    {
        var fixture = ParityFixture.Load(AllocationRuleId);
        foreach (var locator in new[] { RoundForCashLocator, NetFormulaLocator, PackageCashCollectedLocator })
        {
            Assert.Contains(fixture.Source, source => string.Equals(source.Locator, locator, StringComparison.Ordinal));
        }

        var legacyCases = fixture.Cases.Where(fixtureCase => fixtureCase.Input.TryGetProperty(LegacyInput, out _)).ToList();
        Assert.NotEmpty(legacyCases);

        foreach (var fixtureCase in legacyCases)
        {
            RequireInputProperties(AllocationRuleId, fixtureCase, MethodInput, AmountDueInput, PackageInput);
            var input = ReadInput<AllocationInput>(AllocationRuleId, fixtureCase);
            var legacy = ReadInputProperty<LegacyAmountDueTerms>(AllocationRuleId, fixtureCase, LegacyInput);
            var package = ReadInputProperty<PackageAmountDueTerms>(AllocationRuleId, fixtureCase, PackageInput);

            Assert.Equal(CreditPayType, legacy.PayType);
            Assert.Null(legacy.VatTotalPat);
            var sPay = Assert.NotNull(legacy.SPay);
            var legacyPatPayx = Assert.NotNull(legacy.LegacyPatPayx);
            var legacyNet = Assert.NotNull(legacy.LegacyNet);
            Assert.Equal(sPay + RoundForCashCreditVatDefault, legacyPatPayx);
            Assert.Equal(Money.Round2(legacyPatPayx - (legacy.FinalDisc ?? 0m)), legacyNet);

            Assert.Equal(sPay, package.PatPay);
            Assert.Equal(legacy.FinalDisc, package.FinalDisc);
            Assert.NotNull(package.VatTotalPat);
            var cashCollected = Assert.NotNull(package.CashCollected);

            var amountDue = Assert.NotNull(input.AmountDue);
            Assert.Equal(cashCollected, amountDue);
            Assert.NotEqual(legacyNet, amountDue);

            Assert.Equal(DefaultFirstAmountMethod, input.Method);
            var amount1 = PaymentAllocationRules.DefaultFirstAmount(amountDue);
            Assert.Equal(cashCollected, amount1);
            Assert.NotEqual(legacyNet, amount1);

            var expectedValues = fixtureCase.Expected.Values
                ?? throw new InvalidDataException($"Fixture '{AllocationRuleId}', case '{fixtureCase.Name}': expected.values is missing.");
            Assert.True(
                expectedValues.TryGetProperty(Amount1Key, out var expectedAmount1),
                $"Fixture '{AllocationRuleId}', case '{fixtureCase.Name}': expected.values has no {Amount1Key}.");
            Assert.Equal(JsonValueKind.Number, expectedAmount1.ValueKind);
            Assert.Equal(cashCollected, expectedAmount1.GetDecimal());
            Assert.NotEqual(legacyNet, expectedAmount1.GetDecimal());
        }
    }

    /// <summary>The DR-08 fixture is a derivable domain fixture traced to PU30 REMAIN.</summary>
    [Fact]
    [Trait("Rule", RefundRuleId)]
    public void Fixture_DR_08_is_a_domain_fixture_with_derivable_cases()
    {
        AssertDomainFixture(RefundRuleId, ParityFixture.Load(RefundRuleId), Pu30Locator);
    }

    /// <summary>The DR-09 fixture is a derivable domain fixture traced to the CASH_COLLECTED formula.</summary>
    [Fact]
    [Trait("Rule", TotalCollectedRuleId)]
    public void Fixture_DR_09_is_a_domain_fixture_with_derivable_cases()
    {
        AssertDomainFixture(TotalCollectedRuleId, ParityFixture.Load(TotalCollectedRuleId), CashCollectedFormulaLocator);
    }

    private static void AssertCase(
        FixtureDocument fixture,
        FixtureCase fixtureCase,
        string actualOutcome,
        IEnumerable<(string? Field, string Text, string Severity)> actualMessages,
        IReadOnlyDictionary<string, object?> actualValues,
        params string[] requiredKeys)
    {
        Assert.True(ParityFixture.IsDerivable(fixtureCase), $"Fixture '{fixture.Id}', case '{fixtureCase.Name}' is not derivable.");

        var expected = fixtureCase.Expected;
        var expectedMessages = expected.Messages
            ?? throw new InvalidDataException($"Fixture '{fixture.Id}', case '{fixtureCase.Name}': expected.messages is missing.");
        var expectedValues = expected.Values
            ?? throw new InvalidDataException($"Fixture '{fixture.Id}', case '{fixtureCase.Name}': expected.values is missing.");

        foreach (var key in requiredKeys)
        {
            Assert.True(
                expectedValues.TryGetProperty(key, out _),
                $"Fixture '{fixture.Id}', case '{fixtureCase.Name}': expected.values has no {key}.");
        }

        ParityFixture.AssertExact(expected.Outcome, actualOutcome);
        ParityFixture.AssertMessages(expectedMessages, actualMessages);
        ParityFixture.AssertValues(expectedValues, actualValues, fixture.Compare);
        AssertExactValues(fixture, fixtureCase, expectedValues, actualValues);
    }

    /// <summary>Asserts that the actual keys equal the expected ones and that each expected number equals its actual decimal unrounded.</summary>
    private static void AssertExactValues(
        FixtureDocument fixture,
        FixtureCase fixtureCase,
        JsonElement expectedValues,
        IReadOnlyDictionary<string, object?> actualValues)
    {
        var label = $"Fixture '{fixture.Id}', case '{fixtureCase.Name}'";
        var expectedKeys = expectedValues.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        Assert.True(
            expectedKeys.SetEquals(actualValues.Keys),
            $"{label}: actual value keys [{string.Join(", ", actualValues.Keys.Order(StringComparer.Ordinal))}] differ from expected.values keys [{string.Join(", ", expectedKeys.Order(StringComparer.Ordinal))}].");

        foreach (var property in expectedValues.EnumerateObject())
        {
            var actual = actualValues[property.Name];
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Number:
                    Assert.True(
                        property.Value.TryGetDecimal(out var expectedAmount),
                        $"{label}: expected.values.{property.Name} {property.Value.GetRawText()} is outside the decimal range.");
                    Assert.True(
                        actual is decimal actualAmount && actualAmount == expectedAmount,
                        $"{label}: {property.Name} expected exactly {expectedAmount}, actual {actual ?? "null"}.");
                    break;
                case JsonValueKind.Null:
                    Assert.True(actual is null, $"{label}: {property.Name} expected null, actual {actual}.");
                    break;
                default:
                    Assert.Fail($"{label}: expected.values.{property.Name} must be a JSON number or null, not {property.Value.ValueKind}.");
                    break;
            }
        }
    }

    private static void AssertDomainFixture(string ruleId, FixtureDocument fixture, string locator)
    {
        Assert.Equal(ruleId, fixture.Id);
        Assert.Equal(ParityFixture.DomainClass, fixture.Class);
        Assert.NotEmpty(fixture.Cases);
        Assert.All(fixture.Cases, fixtureCase => Assert.True(ParityFixture.IsDerivable(fixtureCase), fixtureCase.Name));
        Assert.Contains(fixture.Source, source => string.Equals(source.Locator, locator, StringComparison.Ordinal));
    }

    private static void RequireInputProperties(string ruleId, FixtureCase fixtureCase, params string[] names)
    {
        foreach (var name in names)
        {
            Assert.True(
                fixtureCase.Input.TryGetProperty(name, out _),
                $"Fixture '{ruleId}', case '{fixtureCase.Name}': input.{name} is missing.");
        }
    }

    private static T ReadInput<T>(string ruleId, FixtureCase fixtureCase)
        where T : class =>
        fixtureCase.Input.Deserialize<T>(ParityFixture.JsonOptions)
        ?? throw new InvalidDataException($"Fixture '{ruleId}', case '{fixtureCase.Name}': input is null.");

    private static T ReadInputProperty<T>(string ruleId, FixtureCase fixtureCase, string name)
        where T : class =>
        fixtureCase.Input.GetProperty(name).Deserialize<T>(ParityFixture.JsonOptions)
        ?? throw new InvalidDataException($"Fixture '{ruleId}', case '{fixtureCase.Name}': input.{name} is null.");

    private static string OutcomeOf(RuleResult result) =>
        result.IsBlocking
            ? ValidationMessage.Blocking
            : result.Messages.Count > 0 ? ValidationMessage.Warning : PassOutcome;

    private static IEnumerable<(string? Field, string Text, string Severity)> MessagesOf(RuleResult result) =>
        result.Messages.Select(message => (message.Field, message.Text, message.Severity));

    private static IEnumerable<(string? Field, string Text, string Severity)> NoMessages() =>
        Array.Empty<(string? Field, string Text, string Severity)>();

    private static IReadOnlyDictionary<string, object?> SingleValue(string key, object? value) =>
        new Dictionary<string, object?>(StringComparer.Ordinal) { [key] = value };

    /// <summary>DR-07 case input.</summary>
    /// <param name="Method">Name of the <see cref="PaymentAllocationRules"/> method the case exercises.</param>
    /// <param name="AmountDue">Amount due, the package's <c>cash_collected</c>.</param>
    /// <param name="Amount1">Amount on payment method 1.</param>
    private sealed record AllocationInput(string? Method, decimal? AmountDue, decimal? Amount1);

    /// <summary>DR-07 legacy <c>ROUND_FOR_CASH</c> and <c>NET</c> terms.</summary>
    /// <param name="PayType">Invoice pay type, <c>PAYTYPE</c>; 1 is cash, any other value credit.</param>
    /// <param name="SPay">Patient share, <c>S_PAY</c>.</param>
    /// <param name="VatTotalPat">Patient VAT, <c>VAT_TOTAL_PAT</c>.</param>
    /// <param name="FinalDisc">Final discount, <c>FINALDISC</c>.</param>
    /// <param name="LegacyPatPayx">Legacy <c>PAT_PAYX</c>, the <c>ROUND_FOR_CASH</c> result.</param>
    /// <param name="LegacyNet">Legacy <c>NET</c>.</param>
    private sealed record LegacyAmountDueTerms(
        int? PayType,
        decimal? SPay,
        decimal? VatTotalPat,
        decimal? FinalDisc,
        decimal? LegacyPatPayx,
        decimal? LegacyNet);

    /// <summary>DR-07 package amount-due terms.</summary>
    /// <param name="PatPay">Patient share, <c>pat_pay</c>.</param>
    /// <param name="FinalDisc">Final discount, <c>finaldisc</c>.</param>
    /// <param name="VatTotalPat">Patient VAT, <c>vat_total_pat</c>.</param>
    /// <param name="CashCollected">Amount due, <c>cash_collected</c>.</param>
    private sealed record PackageAmountDueTerms(decimal? PatPay, decimal? FinalDisc, decimal? VatTotalPat, decimal? CashCollected);

    /// <summary>DR-08 case input.</summary>
    /// <param name="Allocation">Payment split with cash tendered and the two payment methods.</param>
    private sealed record RefundInput(PaymentAllocation? Allocation);

    /// <summary>DR-09 case input.</summary>
    /// <param name="Amount1">Amount on payment method 1.</param>
    /// <param name="Amount2">Amount on payment method 2.</param>
    private sealed record TotalCollectedInput(decimal? Amount1, decimal? Amount2);
}
