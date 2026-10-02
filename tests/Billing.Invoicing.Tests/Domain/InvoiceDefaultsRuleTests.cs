using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Domain.Rules;
using Billing.Invoicing.Tests.Parity;

namespace Billing.Invoicing.Tests.Domain;

/// <summary>DR-20 unit parity tests of <see cref="InvoiceDefaultsRule.Apply"/> against the T015, T022 and T003 fixture.</summary>
[Trait("Category", "DomainParity")]
public sealed class InvoiceDefaultsRuleTests
{
    private const string RuleId = "DR-20";
    private const string PassOutcome = "Pass";
    private const string DatabaseTimeFormat = "yyyy-MM-dd'T'HH:mm:ss";
    private const string T015Locator = "05_Complex/Inv_Small_Cash.xml:367";
    private const string InvTypeIdItemLocator = "05_Complex/Inv_Small_Cash.xml:11";
    private const string T022Locator = "05_Complex/Inv_Small_Cash.xml:13";
    private const string T003Locator = "05_Complex/Inv_Small_Cash.xml:1096";

    private static readonly TimeSpan LastMinuteBeforeMidnight = new(23, 59, 0);
    private static readonly string[] CardFieldKeys = ["INS_NUMBER", "CARD_END", "PAT_POLICY_NO"];
    private static readonly JsonSerializerOptions InputOptions = CreateInputOptions();

    /// <summary>Compares the draft header built by <see cref="InvoiceDefaultsRule.Apply"/> with one DR-20 fixture case.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", "DR-20")]
    [MemberData(nameof(ParityFixture.CaseNames), RuleId, MemberType = typeof(ParityFixture))]
    public void Apply_matches_DR_20(string caseName)
    {
        var fixture = ParityFixture.Load(RuleId);
        var fixtureCase = ParityFixture.Case(RuleId, caseName);
        Assert.True(ParityFixture.IsDerivable(fixtureCase), $"Fixture '{RuleId}', case '{caseName}' is not derivable.");

        var expected = fixtureCase.Expected;
        var expectedMessages = expected.Messages
            ?? throw new InvalidDataException($"Fixture '{RuleId}', case '{caseName}': expected.messages is missing.");
        if (expected.Values is not { } expectedValues || !expectedValues.EnumerateObject().Any())
        {
            throw new InvalidDataException($"Fixture '{RuleId}', case '{caseName}': expected.values is missing or empty.");
        }

        var input = ReadInput(fixtureCase);

        var draft = InvoiceDefaultsRule.Apply(input.Parameters, input.DatabaseTime, input.ClaimPreload, input.VisitDoctorId);

        ParityFixture.AssertExact(expected.Outcome, PassOutcome);
        ParityFixture.AssertMessages(expectedMessages, []);
        ParityFixture.AssertValues(expectedValues, Project(draft), fixture.Compare);
    }

    /// <summary>Checks that the DR-20 fixture is a traced domain fixture covering each default and preload branch.</summary>
    [Fact]
    [Trait("Rule", "DR-20")]
    public void Fixture_DR_20_is_a_domain_fixture_covering_each_default_branch()
    {
        var fixture = ParityFixture.Load(RuleId);

        Assert.Equal(RuleId, fixture.Id);
        Assert.Equal(ParityFixture.DomainClass, fixture.Class);
        Assert.NotEmpty(fixture.Cases);
        Assert.All(fixture.Cases, fixtureCase => Assert.True(ParityFixture.IsDerivable(fixtureCase), fixtureCase.Name));
        foreach (var locator in new[] { T015Locator, InvTypeIdItemLocator, T022Locator, T003Locator })
        {
            Assert.Contains(fixture.Source, source => string.Equals(source.Locator, locator, StringComparison.Ordinal));
        }

        var inputs = fixture.Cases.Select(ReadInput).ToList();

        Assert.Contains(inputs, input => input.Parameters.IsHomeCare == "Y");
        Assert.Contains(inputs, input => input.Parameters.IsHomeCare != "Y");
        Assert.Contains(inputs, input => input.Parameters.ClaimFlag is not null);
        Assert.Contains(inputs, input => input.DatabaseTime.TimeOfDay >= LastMinuteBeforeMidnight);
        Assert.Contains(inputs, input => input.Parameters.ClaimNo == "1" && input.Parameters.NewDoc is not null);
        Assert.Contains(inputs, input => input.Parameters.ClaimNo == "0");
        Assert.Contains(inputs, input => input.Parameters.ClaimNo == "2");
        Assert.Contains(inputs, input => input.ClaimPreload is not null && input.Parameters.CashOrCredit == 1);
        Assert.Contains(inputs, input => input.ClaimPreload is not null && input.Parameters.CashOrCredit != 1);
        Assert.Contains(inputs, input => !string.IsNullOrEmpty(input.Parameters.VisitUnique) && input.VisitDoctorId is not null);

        var cardCases = fixture.Cases
            .Select(fixtureCase => (Input: ReadInput(fixtureCase), Values: fixtureCase.Expected.Values))
            .Where(entry => entry.Input.ClaimPreload is { InsNumber: not null, CardEnd: not null, PatPolicyNo: not null }
                && entry.Values is { ValueKind: JsonValueKind.Object } values
                && CardFieldKeys.All(key => values.TryGetProperty(key, out _)))
            .Select(entry => (entry.Input, Values: entry.Values!.Value))
            .ToList();

        Assert.Contains(cardCases, entry => entry.Input.Parameters.CashOrCredit != 1
            && entry.Values.GetProperty("INS_NUMBER").GetString() == entry.Input.ClaimPreload!.InsNumber
            && entry.Values.GetProperty("CARD_END").GetString() == entry.Input.ClaimPreload.CardEnd!.Value.ToString("s", CultureInfo.InvariantCulture)
            && entry.Values.GetProperty("PAT_POLICY_NO").GetString() == entry.Input.ClaimPreload.PatPolicyNo);
        Assert.Contains(cardCases, entry => entry.Input.Parameters.CashOrCredit == 1
            && CardFieldKeys.All(key => entry.Values.GetProperty(key).ValueKind == JsonValueKind.Null));
        Assert.Contains(cardCases, entry => entry.Input.Parameters.ClaimNo == "1"
            && entry.Input.Parameters.CashOrCredit != 1
            && CardFieldKeys.All(key => entry.Values.GetProperty(key).ValueKind == JsonValueKind.Null));
        Assert.Contains(cardCases, entry => entry.Input.Parameters.ClaimNo == "2"
            && entry.Input.Parameters.CashOrCredit != 1
            && CardFieldKeys.All(key => entry.Values.GetProperty(key).ValueKind == JsonValueKind.Null));
    }

    /// <summary>Checks that a claim preload supplied with claim parameter '1' or '2' is not applied (T015).</summary>
    /// <param name="claimParameter">PARAMETER.CLAIM_NO.</param>
    /// <param name="expectedDocId">Expected DOCIDX: NEW_DOC for '1', none for '2'.</param>
    [Theory]
    [Trait("Rule", "DR-20")]
    [InlineData("1", 10)]
    [InlineData("2", null)]
    public void Apply_ignores_claim_preload_for_claim_parameters_1_and_2(string claimParameter, int? expectedDocId)
    {
        var parameters = new InvoiceEntryParameters { ClaimNo = claimParameter, NewDoc = 10, CashOrCredit = 2 };
        var claimPreload = new InvoiceHeaderDraft
        {
            ClaimNo = "C-7788",
            PatientNo = "1001",
            ClinicId = 14,
            DocId = 33,
            CompCode = "205",
            SubCompCode = "305",
            ClassCode = 4,
            PayType = 2,
            InsNumber = "INS-55",
            CardEnd = new DateTime(2027, 1, 31),
            PatPolicyNo = "POL-9",
        };
        var databaseTime = new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Unspecified);

        var draft = InvoiceDefaultsRule.Apply(parameters, databaseTime, claimPreload, null);

        Assert.Equal(expectedDocId, draft.DocId);
        Assert.Null(draft.ClaimNo);
        Assert.Null(draft.PatientNo);
        Assert.Null(draft.ClinicId);
        Assert.Null(draft.CompCode);
        Assert.Null(draft.SubCompCode);
        Assert.Null(draft.ClassCode);
        Assert.Null(draft.PayType);
        Assert.Null(draft.InsNumber);
        Assert.Null(draft.CardEnd);
        Assert.Null(draft.PatPolicyNo);
        Assert.Equal(1, draft.SubPayType);
        Assert.Equal(databaseTime, draft.DraftDate);
    }

    private static IReadOnlyDictionary<string, object?> Project(InvoiceHeaderDraft d) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["INVTYPEID"] = d.InvTypeId,
            ["SUB_PAYTYPE"] = d.SubPayType,
            ["DRAFT_DATE"] = d.DraftDate,
            ["INV_DATE"] = d.InvDate,
            ["CLAIM_FLAG"] = d.ClaimFlag,
            ["DOCIDX"] = d.DocId,
            ["CLAIM_NO"] = d.ClaimNo,
            ["PATIENTNO"] = d.PatientNo,
            ["CLINICID"] = d.ClinicId,
            ["COMP_CODE"] = d.CompCode,
            ["SUB_COMP_CODE"] = d.SubCompCode,
            ["CLASS_CODE"] = d.ClassCode,
            ["PAYTYPE"] = d.PayType,
            ["INS_NUMBER"] = d.InsNumber,
            ["CARD_END"] = d.CardEnd,
            ["PAT_POLICY_NO"] = d.PatPolicyNo,
        };

    private static DefaultsInput ReadInput(FixtureCase fixtureCase)
    {
        CaseInput? input;
        try
        {
            input = fixtureCase.Input.Deserialize<CaseInput>(InputOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Fixture '{RuleId}', case '{fixtureCase.Name}': {ex.Message}", ex);
        }

        if (input is null)
        {
            throw new InvalidDataException($"Fixture '{RuleId}', case '{fixtureCase.Name}': input is null.");
        }

        if (!DateTime.TryParseExact(
                input.DatabaseTime,
                DatabaseTimeFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var databaseTime))
        {
            throw new InvalidDataException(
                $"Fixture '{RuleId}', case '{fixtureCase.Name}': databaseTime '{input.DatabaseTime}' is not of the form {DatabaseTimeFormat}.");
        }

        return new DefaultsInput(
            input.Parameters ?? new InvoiceEntryParameters(),
            databaseTime,
            input.ClaimPreload,
            input.VisitDoctorId);
    }

    private static JsonSerializerOptions CreateInputOptions()
    {
        var options = new JsonSerializerOptions(ParityFixture.JsonOptions)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>DR-20 case input as stored in the fixture.</summary>
    /// <param name="Parameters">Entry parameters; the Form defaults when absent.</param>
    /// <param name="DatabaseTime">Database <c>SYSDATE</c> at draft creation, as <c>yyyy-MM-ddTHH:mm:ss</c>.</param>
    /// <param name="ClaimPreload">Header of the claim's first invoice, or null.</param>
    /// <param name="VisitDoctorId">Doctor of the <c>PAT_VISIT_M</c> visit, or null.</param>
    private sealed record CaseInput(
        InvoiceEntryParameters? Parameters,
        string? DatabaseTime,
        InvoiceHeaderDraft? ClaimPreload,
        int? VisitDoctorId);

    /// <summary>DR-20 case input converted to the argument types of <see cref="InvoiceDefaultsRule.Apply"/>.</summary>
    /// <param name="Parameters">Entry parameters.</param>
    /// <param name="DatabaseTime">Database time at draft creation.</param>
    /// <param name="ClaimPreload">Header of the claim's first invoice, or null.</param>
    /// <param name="VisitDoctorId">Doctor of the <c>PAT_VISIT_M</c> visit, or null.</param>
    private sealed record DefaultsInput(
        InvoiceEntryParameters Parameters,
        DateTime DatabaseTime,
        InvoiceHeaderDraft? ClaimPreload,
        int? VisitDoctorId);
}
