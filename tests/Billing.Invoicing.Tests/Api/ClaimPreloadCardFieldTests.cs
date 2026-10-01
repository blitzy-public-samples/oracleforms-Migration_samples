using System.Text.Json;
using System.Text.Json.Serialization;
using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Tests.Api;

/// <summary>DR-20 insurance card fields of a new draft's claim preload (T015) and their DraftDto JSON round trip.</summary>
[Trait("Category", "Orchestration")]
public sealed class ClaimPreloadCardFieldTests
{
    private const string ClaimNo = "C-7788";
    private const string InsNumber = "INS-55";
    private const string CardEndText = "2027-01-31T00:00:00";
    private const string PatPolicyNo = "POL-9";

    private static readonly DateTime CardEnd = new(2027, 1, 31, 0, 0, 0);

    private static readonly OperatorContext Operator = new()
    {
        UserNo = 101,
        UserName = "TESTER",
        InfoCenterId = "1",
        MachineName = "TEST-PC",
        SessionId = "7D3F2C1B9A8E4F6D8C2B1A0F9E8D7C6B",
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Checks that a CASH_OR_CREDIT other than 1 copies the first claim invoice's INS_NUMBER, CARD_END and PAT_POLICY_NO into the new draft.</summary>
    /// <param name="cashOrCredit">PARAMETER.CASH_OR_CREDIT.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task NewDraft_CreditClaimPreload_CarriesTheCardFields(int cashOrCredit)
    {
        var fakes = ArrangePreload();

        var response = await fakes.CreateService().NewDraft(Parameters(cashOrCredit), Operator);

        var preloadCall = Assert.Single(fakes.Invoices.Calls, call => call.Method == nameof(IInvoiceQueries.GetClaimPreload));
        Assert.Equal(ClaimNo, preloadCall.Arg<string>());
        var header = response.Draft.Header;
        Assert.Equal(InsNumber, header.InsNumber);
        Assert.Equal(CardEnd, header.CardEnd);
        Assert.Equal(PatPolicyNo, header.PatPolicyNo);
    }

    /// <summary>Checks that CASH_OR_CREDIT 1 leaves INS_NUMBER, CARD_END and PAT_POLICY_NO null although the claim preload carries them.</summary>
    [Fact]
    public async Task NewDraft_CashClaimPreload_NullsTheCardFields()
    {
        var fakes = ArrangePreload();

        var response = await fakes.CreateService().NewDraft(Parameters(1), Operator);

        Assert.Single(fakes.Invoices.Calls, call => call.Method == nameof(IInvoiceQueries.GetClaimPreload));
        var header = response.Draft.Header;
        Assert.Null(header.InsNumber);
        Assert.Null(header.CardEnd);
        Assert.Null(header.PatPolicyNo);
    }

    /// <summary>Checks that the DraftDto header JSON writes insNumber, cardEnd and patPolicyNo and reads them back.</summary>
    [Fact]
    public void DraftDtoJson_KeepsTheCardFieldsOnWriteAndRead()
    {
        var draft = new DraftDto
        {
            Header = new InvoiceHeaderDraft
            {
                ClaimNo = ClaimNo,
                PatientNo = "1001",
                InsNumber = InsNumber,
                CardEnd = CardEnd,
                PatPolicyNo = PatPolicyNo,
            },
        };

        var json = JsonSerializer.Serialize(new NewDraftResponse { Draft = draft }, Json);

        using (var document = JsonDocument.Parse(json))
        {
            var header = document.RootElement.GetProperty("draft").GetProperty("header");
            Assert.Equal(InsNumber, header.GetProperty("insNumber").GetString());
            Assert.Equal(CardEndText, header.GetProperty("cardEnd").GetString());
            Assert.Equal(PatPolicyNo, header.GetProperty("patPolicyNo").GetString());
        }

        var back = JsonSerializer.Deserialize<NewDraftResponse>(json, Json);
        Assert.NotNull(back);
        Assert.Equal(draft.Header, back.Draft.Header);

        var request = JsonSerializer.Deserialize<DraftDto>(
            """{"header":{"claimNo":"C-7788","insNumber":"INS-55","cardEnd":"2027-01-31T00:00:00","patPolicyNo":"POL-9"}}""",
            Json);
        Assert.NotNull(request);
        Assert.Equal(InsNumber, request.Header.InsNumber);
        Assert.Equal(CardEnd, request.Header.CardEnd);
        Assert.Equal(PatPolicyNo, request.Header.PatPolicyNo);
    }

    private static InvoiceEntryParameters Parameters(int cashOrCredit) => new() { ClaimNo = ClaimNo, CashOrCredit = cashOrCredit };

    private static FakeDataPorts ArrangePreload()
    {
        var fakes = new FakeDataPorts();
        fakes.Invoices.ClaimPreload = claimNo => string.Equals(claimNo, ClaimNo, StringComparison.Ordinal)
            ? (new InvoiceHeaderDraft
            {
                ClaimNo = ClaimNo,
                PatientNo = "1001",
                ClinicId = 14,
                CompCode = "205",
                SubCompCode = "305",
                ClassCode = 4,
                PayType = 2,
                InsNumber = InsNumber,
                CardEnd = CardEnd,
                PatPolicyNo = PatPolicyNo,
            }, 55m, null, null)
            : null;
        return fakes;
    }
}
