using System.Text.Json;
using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Domain.Rules;
using Billing.Invoicing.Tests.Parity;

namespace Billing.Invoicing.Tests.Domain;

/// <summary>DR-01 and DR-22 unit parity tests of <see cref="HeaderRecordRules"/> against the T014 fixtures.</summary>
[Trait("Category", "DomainParity")]
public sealed class HeaderRecordRulesTests
{
    private const string RecordRuleId = "DR-01";
    private const string PaymentTypeRuleId = "DR-22";

    private const string HeaderInput = "header";

    private const string CompCodeField = "COMP_CODE";
    private const string PatientNoField = "PATIENTNO";
    private const string DoctorField = "DOCIDX";
    private const string SubPayTypeKey = "SUB_PAYTYPE";

    private const string PatientRequiredText = "Select Patient No is required ";

    private const string BlockingOutcome = "Blocking";
    private const string WarningOutcome = "Warning";
    private const string PassOutcome = "Pass";

    private const string T014Locator = "05_Complex/Inv_Small_Cash.xml:366";
    private const string T038Locator = "05_Complex/Inv_Small_Cash.xml:73";
    private const string T040Locator = "05_Complex/Inv_Small_Cash.xml:77";

    /// <summary>Each DR-01 case yields its expected outcome and messages from <see cref="HeaderRecordRules.ValidateRecord"/>, with no adjusted values.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", RecordRuleId)]
    [MemberData(nameof(ParityFixture.CaseNames), RecordRuleId, MemberType = typeof(ParityFixture))]
    public void ValidateRecord_matches_DR_01(string caseName)
    {
        var fixture = ParityFixture.Load(RecordRuleId);
        var fixtureCase = ParityFixture.Case(RecordRuleId, caseName);
        Assert.True(ParityFixture.IsDerivable(fixtureCase), $"Fixture '{RecordRuleId}', case '{caseName}' is not derivable.");
        var header = ReadHeader(RecordRuleId, fixtureCase);

        var result = HeaderRecordRules.ValidateRecord(header);

        AssertCase(RecordRuleId, fixture, fixtureCase, result);
    }

    /// <summary>Each DR-22 case yields its expected outcome, messages and SUB_PAYTYPE default from <see cref="HeaderRecordRules.ApplyPaymentTypeDefault"/>, and never blocks.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", PaymentTypeRuleId)]
    [MemberData(nameof(ParityFixture.CaseNames), PaymentTypeRuleId, MemberType = typeof(ParityFixture))]
    public void ApplyPaymentTypeDefault_matches_DR_22(string caseName)
    {
        var fixture = ParityFixture.Load(PaymentTypeRuleId);
        var fixtureCase = ParityFixture.Case(PaymentTypeRuleId, caseName);
        Assert.True(ParityFixture.IsDerivable(fixtureCase), $"Fixture '{PaymentTypeRuleId}', case '{caseName}' is not derivable.");
        var header = ReadHeader(PaymentTypeRuleId, fixtureCase);

        var result = HeaderRecordRules.ApplyPaymentTypeDefault(header);

        AssertCase(PaymentTypeRuleId, fixture, fixtureCase, result);
        Assert.False(result.IsBlocking);
    }

    /// <summary>An empty patient number yields the T014 PATIENTNO blocking message, as a null one does.</summary>
    [Fact]
    [Trait("Rule", RecordRuleId)]
    public void ValidateRecord_treats_an_empty_patient_number_as_missing()
    {
        var header = new InvoiceHeaderDraft { PayType = 1, CompCode = "0", PatientNo = string.Empty, DocId = 10 };

        var result = HeaderRecordRules.ValidateRecord(header);

        var message = Assert.Single(result.Messages);
        ParityFixture.AssertExact(PatientNoField, message.Field);
        ParityFixture.AssertExact(PatientRequiredText, message.Text);
        ParityFixture.AssertExact(ValidationMessage.Blocking, message.Severity);
        ParityFixture.AssertExact(RecordRuleId, message.Rule);
        Assert.True(result.IsBlocking);
        Assert.Empty(result.Adjusted);
    }

    /// <summary>The DR-01 fixture is a derivable domain fixture from T014 holding a blocking case per checked item and a pass case.</summary>
    [Fact]
    [Trait("Rule", RecordRuleId)]
    public void Fixture_DR_01_holds_a_blocking_case_per_item_and_pass_cases()
    {
        var fixture = ParityFixture.Load(RecordRuleId);
        AssertDomainFixture(RecordRuleId, fixture, T014Locator);

        foreach (var field in new[] { CompCodeField, PatientNoField, DoctorField })
        {
            Assert.Contains(
                fixture.Cases,
                fixtureCase => fixtureCase.Expected.Outcome == BlockingOutcome
                    && fixtureCase.Expected.Messages is { Count: 1 } messages
                    && string.Equals(messages[0].Field, field, StringComparison.Ordinal));
        }

        Assert.DoesNotContain(fixture.Cases, fixtureCase => fixtureCase.Expected.Outcome == WarningOutcome);
        Assert.Contains(fixture.Cases, fixtureCase => fixtureCase.Expected.Outcome == PassOutcome);
    }

    /// <summary>The DR-22 fixture is a derivable domain fixture from T014, T038 and T040 holding warning and pass cases and no blocking case.</summary>
    [Fact]
    [Trait("Rule", PaymentTypeRuleId)]
    public void Fixture_DR_22_holds_warning_and_pass_cases_only()
    {
        var fixture = ParityFixture.Load(PaymentTypeRuleId);
        AssertDomainFixture(PaymentTypeRuleId, fixture, T014Locator);
        Assert.Contains(fixture.Source, source => string.Equals(source.Locator, T038Locator, StringComparison.Ordinal));
        Assert.Contains(fixture.Source, source => string.Equals(source.Locator, T040Locator, StringComparison.Ordinal));

        Assert.DoesNotContain(fixture.Cases, fixtureCase => fixtureCase.Expected.Outcome == BlockingOutcome);
        Assert.Contains(
            fixture.Cases,
            fixtureCase => fixtureCase.Expected.Outcome == WarningOutcome
                && fixtureCase.Expected.Values is { } values
                && values.TryGetProperty(SubPayTypeKey, out _));
        Assert.Contains(fixture.Cases, fixtureCase => fixtureCase.Expected.Outcome == PassOutcome);
    }

    private static void AssertCase(string ruleId, FixtureDocument fixture, FixtureCase fixtureCase, RuleResult result)
    {
        var expected = fixtureCase.Expected;
        Assert.NotNull(expected.Messages);

        ParityFixture.AssertExact(expected.Outcome, Outcome(result));
        ParityFixture.AssertMessages(
            expected.Messages,
            result.Messages.Select(message => (message.Field, message.Text, message.Severity)));
        Assert.All(result.Messages, message => ParityFixture.AssertExact(ruleId, message.Rule));
        AssertAdjusted(fixture, expected, result);
    }

    private static void AssertAdjusted(FixtureDocument fixture, FixtureExpected expected, RuleResult result)
    {
        if (expected.Values is not { } values)
        {
            Assert.Empty(result.Adjusted);
            return;
        }

        var projected = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in values.EnumerateObject())
        {
            var adjusted = Assert.Single(
                result.Adjusted,
                pair => string.Equals(pair.Key, property.Name, StringComparison.OrdinalIgnoreCase));
            projected[property.Name] = adjusted.Value;
        }

        Assert.True(
            projected.Count == result.Adjusted.Count,
            $"Adjusted keys {string.Join(", ", result.Adjusted.Keys)} differ from the expected keys {string.Join(", ", projected.Keys)}.");
        ParityFixture.AssertValues(values, projected, fixture.Compare);
    }

    private static void AssertDomainFixture(string ruleId, FixtureDocument fixture, string locator)
    {
        Assert.Equal(ruleId, fixture.Id);
        Assert.Equal(ParityFixture.DomainClass, fixture.Class);
        Assert.NotEmpty(fixture.Cases);
        Assert.All(fixture.Cases, fixtureCase => Assert.True(ParityFixture.IsDerivable(fixtureCase), fixtureCase.Name));
        Assert.Contains(fixture.Source, source => string.Equals(source.Locator, locator, StringComparison.Ordinal));
    }

    private static InvoiceHeaderDraft ReadHeader(string ruleId, FixtureCase fixtureCase)
    {
        Assert.True(
            fixtureCase.Input.TryGetProperty(HeaderInput, out var header) && header.ValueKind == JsonValueKind.Object,
            $"Fixture '{ruleId}', case '{fixtureCase.Name}' has no {HeaderInput} object input.");

        var draft = header.Deserialize<InvoiceHeaderDraft>(ParityFixture.JsonOptions);
        Assert.NotNull(draft);
        return draft;
    }

    private static string Outcome(RuleResult result)
    {
        if (result.IsBlocking)
        {
            return BlockingOutcome;
        }

        return result.Messages.Count > 0 ? WarningOutcome : PassOutcome;
    }
}
