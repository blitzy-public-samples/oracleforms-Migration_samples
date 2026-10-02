using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Api.Errors;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Tests.Api;

/// <summary>DR-24 claim preload over port fakes: a new draft inherits a null pay type, and every operation that decides or binds it is refused on PAYTYPE.</summary>
[Trait("Category", "Orchestration")]
public sealed class ClaimPreloadPayTypeTests
{
    private const string ClaimNo = "C-7788";
    private const string PatientNo = "1001";
    private const string PreloadCompany = "205";
    private const string PreloadSubCompany = "305";
    private const int PreloadClass = 4;
    private const int PreloadClinic = 14;
    private const int InsuranceCompanyType = 2;
    private const int NeitherCashNorCredit = 0;
    private const int CreditPayType = 2;
    private const string PayTypeField = "PAYTYPE";
    private const string PayTypeRequiredText = "FRM-40202: Field must be entered.";
    private const long RecordedInvoiceNo = 9001L;

    private static readonly OperatorContext Operator = new()
    {
        UserNo = 101,
        UserName = "TESTER",
        InfoCenterId = "1",
        MachineName = "TEST-PC",
        SessionId = "7D3F2C1B9A8E4F6D8C2B1A0F9E8D7C6B",
    };

    private static readonly InvoiceEntryParameters ClaimParameters = new()
    {
        ClaimNo = ClaimNo,
        CashOrCredit = NeitherCashNorCredit,
    };

    /// <summary>Checks that a new draft preloaded from a claim invoice with a null pay type keeps PAYTYPE null.</summary>
    [Fact]
    public async Task NewDraft_ClaimPreloadWithNullPayType_KeepsPayTypeNull()
    {
        var fakes = Arrange(preloadPayType: null);

        var response = await fakes.CreateService().NewDraft(ClaimParameters, Operator);

        Assert.Equal(PatientNo, response.Draft.Header.PatientNo);
        Assert.Equal(PreloadCompany, response.Draft.Header.CompCode);
        Assert.Null(response.Draft.Header.PayType);
        Assert.Empty(response.Messages);
    }

    /// <summary>Checks that a new draft preloaded from a claim invoice with pay type 2 keeps credit, and that COMP_CODE validation adjusts PAYTYPE to it.</summary>
    [Fact]
    public async Task NewDraft_ClaimPreloadWithCreditPayType_KeepsCredit()
    {
        var fakes = Arrange(preloadPayType: CreditPayType);
        var service = fakes.CreateService();

        var response = await service.NewDraft(ClaimParameters, Operator);
        var validated = await service.Validate(
            new ValidateDraftRequest { Draft = WithLine(response.Draft), Target = "COMP_CODE" }, Operator);

        Assert.Equal(CreditPayType, response.Draft.Header.PayType);
        Assert.DoesNotContain(validated.Messages, message => message.Severity == ValidationMessage.Blocking);
        Assert.Equal(CreditPayType, Assert.IsType<int>(validated.Adjusted[PayTypeField]));
    }

    /// <summary>Checks that preview of a draft whose inherited pay type is null is refused on PAYTYPE before the package preview.</summary>
    [Fact]
    public async Task Preview_InheritedNullPayType_IsRefusedOnPayType()
    {
        var fakes = Arrange(preloadPayType: null);
        var service = fakes.CreateService();
        var draft = WithLine((await service.NewDraft(ClaimParameters, Operator)).Draft);

        var failure = await Assert.ThrowsAsync<ArgumentException>(() => service.Preview(draft, Operator));

        AssertPayTypeRequired(failure);
        AssertNoPackageCall(fakes);
    }

    /// <summary>Checks that create of a draft whose inherited pay type is null is refused on PAYTYPE before the package save.</summary>
    [Fact]
    public async Task Create_InheritedNullPayType_IsRefusedOnPayType()
    {
        var fakes = Arrange(preloadPayType: null);
        var service = fakes.CreateService();
        var draft = WithLine((await service.NewDraft(ClaimParameters, Operator)).Draft);

        var failure = await Assert.ThrowsAsync<ArgumentException>(
            () => service.Create(new CreateInvoiceRequest { Draft = draft }, Operator));

        AssertPayTypeRequired(failure);
        AssertNoPackageCall(fakes);
    }

    /// <summary>Checks that COMP_CODE validation of a draft whose inherited pay type is null is refused on PAYTYPE.</summary>
    [Fact]
    public async Task ValidateCompany_InheritedNullPayType_IsRefusedOnPayType()
    {
        var fakes = Arrange(preloadPayType: null);
        var service = fakes.CreateService();
        var draft = WithLine((await service.NewDraft(ClaimParameters, Operator)).Draft);

        var failure = await Assert.ThrowsAsync<ArgumentException>(
            () => service.Validate(new ValidateDraftRequest { Draft = draft, Target = "COMP_CODE" }, Operator));

        AssertPayTypeRequired(failure);
        AssertNoPackageCall(fakes);
    }

    /// <summary>Checks that the coverage read of the preloaded patient is refused on PAYTYPE when the inherited pay type is null.</summary>
    [Fact]
    public async Task GetCoverage_InheritedNullPayType_IsRefusedOnPayType()
    {
        var fakes = Arrange(preloadPayType: null);
        var service = fakes.CreateService();
        var draft = (await service.NewDraft(ClaimParameters, Operator)).Draft;

        var failure = await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetCoverage(PatientNo, draft.DraftDate, draft.Parameters, Operator));

        AssertPayTypeRequired(failure);
        AssertNoPackageCall(fakes);
    }

    /// <summary>Checks that a create whose request id is already recorded replays through the package without deciding the null inherited pay type.</summary>
    [Fact]
    [Trait("Decision", "D-54")]
    public async Task Create_RecordedRequestId_ReplaysWithoutDecidingPayType()
    {
        var fakes = Arrange(preloadPayType: null);
        var service = fakes.CreateService();
        var draft = WithLine((await service.NewDraft(ClaimParameters, Operator)).Draft);
        fakes.Invoices.CreateRequest = requestId => string.Equals(requestId, draft.RequestId, StringComparison.Ordinal)
            ? (RecordedInvoiceNo, PatientNo, new DateTimeOffset(2026, 9, 29, 10, 5, 0, TimeSpan.Zero), null, null, null, false)
            : null;
        fakes.InvoiceApi.InvoiceNo = RecordedInvoiceNo;

        var response = await service.Create(new CreateInvoiceRequest { Draft = draft }, Operator);

        var create = Assert.Single(fakes.CallsTo(nameof(IBilInvoiceApiGateway.CreateFullInvoice)));
        Assert.Equal(draft.RequestId, create.Arg<string>());
        Assert.NotNull(response.InvNo);
        Assert.DoesNotContain(response.Messages, message => message.Severity == ValidationMessage.Blocking);
    }

    private static FakeDataPorts Arrange(int? preloadPayType)
    {
        var fakes = new FakeDataPorts();
        fakes.Lookups.CompanyType = InsuranceCompanyType;
        fakes.Lookups.PatientCoverage = new PatientCoverageSnapshot
        {
            PatientNo = PatientNo,
            CompCode = PreloadCompany,
            CompanyType = InsuranceCompanyType,
        };
        fakes.Invoices.CreateRequest = _ => null;
        fakes.Invoices.ClaimPreload = claimNo => string.Equals(claimNo, ClaimNo, StringComparison.Ordinal)
            ? (Preload(preloadPayType), 10m, 0m, null)
            : null;
        return fakes;
    }

    private static InvoiceHeaderDraft Preload(int? payType) => new()
    {
        ClaimNo = ClaimNo,
        PatientNo = PatientNo,
        ClinicId = PreloadClinic,
        CompCode = PreloadCompany,
        SubCompCode = PreloadSubCompany,
        ClassCode = PreloadClass,
        PayType = payType,
    };

    private static DraftDto WithLine(DraftDto draft) => draft with
    {
        Lines = new[]
        {
            new InvoiceLineDraft { ServiceId = "S1", Qty = 1m, DiscountType = "R", ClientId = "c1" },
        },
    };

    private static void AssertPayTypeRequired(ArgumentException failure)
    {
        var messages = Assert.IsAssignableFrom<IReadOnlyList<MessageDto>>(failure.Data[ProblemDetailsWriter.MessagesDataKey]);
        var message = Assert.Single(messages);
        Assert.Equal(PayTypeField, message.Field);
        Assert.Equal(PayTypeRequiredText, message.Text);
        Assert.Equal(ValidationMessage.Blocking, message.Severity);
    }

    private static void AssertNoPackageCall(FakeDataPorts fakes)
    {
        Assert.Empty(fakes.CallsTo(nameof(IBilInvoiceApiGateway.CalculatePreview)));
        Assert.Empty(fakes.CallsTo(nameof(IBilInvoiceApiGateway.CreateFullInvoice)));
    }
}
