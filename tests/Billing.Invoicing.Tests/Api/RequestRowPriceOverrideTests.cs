using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

namespace Billing.Invoicing.Tests.Api;

/// <summary>Price overrides on request-row lines: only a selected request row of the line's service is exempt from the T066 price check.</summary>
[Trait("Category", "Orchestration")]
public sealed class RequestRowPriceOverrideTests
{
    private const string PatientNo = "P100";
    private const string CashCompany = "0";
    private const int CashPayType = 1;
    private const int CreditPayType = 2;
    private const int ClinicId = 5;
    private const int DoctorId = 12;
    private const string VisitUnique = "V1";
    private const string FixedPriceService = "S1";
    private const string EditablePriceService = "S2";
    private const string OtherFixedPriceService = "S3";
    private const string PriceFixed = "Y";
    private const string PriceNotFixed = "N";
    private const long SelectedRowId = 5001L;
    private const long ForgedRowId = 123456L;
    private const decimal ImportedOverride = 42m;
    private const decimal ForgedOverride = 0.01m;
    private const string PriceItem = "PRICE";
    private const string UsePriceOverride = "Y";

    private static readonly OperatorContext Operator = new()
    {
        UserNo = 101,
        UserName = "TESTER",
        InfoCenterId = "1",
        MachineName = "TEST-PC",
        SessionId = "7D3F2C1B9A8E4F6D8C2B1A0F9E8D7C6B",
    };

    private static readonly DateTime DraftDate = new(2026, 9, 29, 10, 0, 0);

    [Fact]
    [Trait("Decision", "D-42")]
    public async Task Create_ForgedRowIdWithOverrideOnFixedPrice_ReturnsBlockingPriceAndNeverCreates()
    {
        var draft = await CashDraft(RowLine(FixedPriceService, ForgedRowId, ForgedOverride));
        var fakes = Arrange();

        var response = await fakes.CreateService().Create(new CreateInvoiceRequest { Draft = draft }, Operator);

        Assert.Null(response.InvNo);
        AssertFixedPriceRefused(response.Messages, FixedPriceService);
        Assert.Empty(fakes.CallsTo(nameof(IBilInvoiceApiGateway.CreateFullInvoice)));
        Assert.All(PreviewedLines(fakes), lines => Assert.All(lines, line => Assert.Null(line.PriceOverride)));
    }

    [Fact]
    [Trait("Decision", "D-42")]
    public async Task Preview_ForgedRowIdWithOverrideOnFixedPrice_ReturnsBlockingPriceAndPreviewsWithoutOverride()
    {
        var draft = await CashDraft(RowLine(FixedPriceService, ForgedRowId, ForgedOverride));
        var fakes = Arrange();

        var response = await fakes.CreateService().Preview(draft, Operator);

        AssertFixedPriceRefused(response.Messages, FixedPriceService);
        Assert.NotEmpty(PreviewedLines(fakes));
        Assert.All(PreviewedLines(fakes), lines => Assert.All(lines, line => Assert.Null(line.PriceOverride)));
        Assert.Empty(response.PriceEditableClientIds);
    }

    [Fact]
    [Trait("Decision", "D-42")]
    public async Task Validate_LineWithForgedRowIdAndOverrideOnFixedPrice_ReturnsBlockingPrice()
    {
        var draft = await CashDraft(RowLine(FixedPriceService, ForgedRowId, ForgedOverride));
        var fakes = Arrange();

        var response = await fakes.CreateService().Validate(
            new ValidateDraftRequest { Draft = draft, Target = "LINE", LineIndex = 0 }, Operator);

        AssertFixedPriceRefused(response.Messages, FixedPriceService);
        Assert.False(response.PriceEditable);
    }

    [Fact]
    [Trait("Decision", "D-42")]
    public async Task Create_SelectedRowOverride_BindsTheImportedOverride()
    {
        var draft = await CashDraft(RowLine(FixedPriceService, SelectedRowId, ImportedOverride));
        var fakes = Arrange();

        var response = await fakes.CreateService().Create(new CreateInvoiceRequest { Draft = draft }, Operator);

        Assert.NotNull(response.InvNo);
        Assert.DoesNotContain(response.Messages, message => IsBlocking(message) && message.Field == PriceItem);
        var created = Assert.Single(fakes.CallsTo(nameof(IBilInvoiceApiGateway.CreateFullInvoice)));
        var lines = created.Arg<IReadOnlyList<InvoiceLineDraft>>();
        Assert.Equal(ImportedOverride, Assert.Single(lines).PriceOverride);

        var parameters = LineInputBinder.Bind(lines);
        Assert.Equal(new object?[] { ImportedOverride }, Plain(Find(parameters, "l_price_override")));
        Assert.Equal(new object?[] { UsePriceOverride }, Plain(Find(parameters, "l_use_price_override")));
        Assert.Equal(new object?[] { (decimal)SelectedRowId }, Plain(Find(parameters, "l_pat_serv_req_row_id")));
    }

    [Fact]
    [Trait("Decision", "D-42")]
    public async Task Preview_SelectedRowOverride_PreviewsWithTheOverrideAndListsNoEditableLine()
    {
        var draft = await CashDraft(RowLine(FixedPriceService, SelectedRowId, ImportedOverride));
        var fakes = Arrange();

        var response = await fakes.CreateService().Preview(draft, Operator);

        Assert.DoesNotContain(response.Messages, message => IsBlocking(message) && message.Field == PriceItem);
        Assert.NotEmpty(PreviewedLines(fakes));
        Assert.All(PreviewedLines(fakes), lines => Assert.Equal(ImportedOverride, Assert.Single(lines).PriceOverride));
        Assert.Empty(response.PriceEditableClientIds);
    }

    [Fact]
    [Trait("Decision", "D-42")]
    public async Task Create_SelectedRowIdOnAnotherService_ReturnsBlockingPrice()
    {
        var draft = await CashDraft(RowLine(OtherFixedPriceService, SelectedRowId, ForgedOverride));
        var fakes = Arrange();

        var response = await fakes.CreateService().Create(new CreateInvoiceRequest { Draft = draft }, Operator);

        Assert.Null(response.InvNo);
        AssertFixedPriceRefused(response.Messages, OtherFixedPriceService);
        Assert.Empty(fakes.CallsTo(nameof(IBilInvoiceApiGateway.CreateFullInvoice)));
    }

    [Fact]
    [Trait("Decision", "D-42")]
    public async Task Create_ForgedRowIdWithOverrideOnEditablePrice_BindsTheOverride()
    {
        var draft = await CashDraft(RowLine(EditablePriceService, ForgedRowId, ForgedOverride));
        var fakes = Arrange();

        var response = await fakes.CreateService().Create(new CreateInvoiceRequest { Draft = draft }, Operator);

        Assert.NotNull(response.InvNo);
        Assert.DoesNotContain(response.Messages, message => IsBlocking(message) && message.Field == PriceItem);
        var created = Assert.Single(fakes.CallsTo(nameof(IBilInvoiceApiGateway.CreateFullInvoice)));
        Assert.Equal(ForgedOverride, Assert.Single(created.Arg<IReadOnlyList<InvoiceLineDraft>>()).PriceOverride);
    }

    [Fact]
    [Trait("Decision", "D-42")]
    public async Task Create_RowIdWithOverride_ReadsTheSelectedRowsOnceWithThePatientVisitAndServerPayType()
    {
        var cash = await CashDraft(RowLine(FixedPriceService, SelectedRowId, ImportedOverride));
        var draft = cash with { Header = cash.Header with { PayType = CreditPayType } };
        var fakes = Arrange();

        await fakes.CreateService().Create(new CreateInvoiceRequest { Draft = draft }, Operator);

        var read = Assert.Single(fakes.CallsTo(nameof(IInvoiceQueries.GetSelectedRequestRows)));
        Assert.Equal(new object?[] { PatientNo, VisitUnique, CashPayType }, read.Args);
    }

    [Fact]
    [Trait("Decision", "D-42")]
    public async Task CreateAndPreview_NoLineWithRowIdAndOverride_DoNotReadTheSelectedRows()
    {
        var draft = await CashDraft(
            Line(FixedPriceService, "c1") with { PatServReqRowId = SelectedRowId },
            Line(EditablePriceService, "c2") with { PriceOverride = ForgedOverride, UsePriceOverride = UsePriceOverride });
        var createFakes = Arrange();
        var previewFakes = Arrange();

        var created = await createFakes.CreateService().Create(new CreateInvoiceRequest { Draft = draft }, Operator);
        await previewFakes.CreateService().Preview(draft, Operator);

        Assert.NotNull(created.InvNo);
        Assert.Empty(createFakes.CallsTo(nameof(IInvoiceQueries.GetSelectedRequestRows)));
        Assert.Empty(previewFakes.CallsTo(nameof(IInvoiceQueries.GetSelectedRequestRows)));
    }

    [Fact]
    [Trait("Decision", "D-42")]
    public async Task Create_UnreadableSelectedRow_ReturnsBlockingPrice()
    {
        var draft = await CashDraft(RowLine(FixedPriceService, SelectedRowId, ImportedOverride));
        var fakes = Arrange();
        fakes.Invoices.SelectedRequestRows = (_, _, _) => throw new InvalidCastException("Specified cast is not valid.");

        var response = await fakes.CreateService().Create(new CreateInvoiceRequest { Draft = draft }, Operator);

        Assert.Null(response.InvNo);
        AssertFixedPriceRefused(response.Messages, FixedPriceService);
        Assert.Empty(fakes.CallsTo(nameof(IBilInvoiceApiGateway.CreateFullInvoice)));
    }

    [Fact]
    [Trait("Decision", "D-42")]
    public async Task Create_RowIdWithOverrideWithoutVisit_ReturnsBlockingPriceWithoutReadingTheSelectedRows()
    {
        var cash = await CashDraft(RowLine(FixedPriceService, SelectedRowId, ImportedOverride));
        var draft = cash with { Parameters = new InvoiceEntryParameters() };
        var fakes = Arrange();

        var response = await fakes.CreateService().Create(new CreateInvoiceRequest { Draft = draft }, Operator);

        Assert.Null(response.InvNo);
        AssertFixedPriceRefused(response.Messages, FixedPriceService);
        Assert.Empty(fakes.CallsTo(nameof(IInvoiceQueries.GetSelectedRequestRows)));
    }

    private static InvoiceLineDraft Line(string serviceId, string clientId) => new()
    {
        ServiceId = serviceId,
        Qty = 1m,
        DiscountType = "R",
        ClientId = clientId,
    };

    private static InvoiceLineDraft RowLine(string serviceId, long rowId, decimal priceOverride) =>
        Line(serviceId, "r1") with
        {
            PatServReqRowId = rowId,
            PriceOverride = priceOverride,
            UsePriceOverride = UsePriceOverride,
        };

    private static Task<DraftDto> CashDraft(params InvoiceLineDraft[] lines) => Issued(new DraftDto
    {
        RequestId = Guid.NewGuid().ToString("N").ToUpperInvariant(),
        DraftDate = DraftDate,
        Header = new InvoiceHeaderDraft
        {
            PatientNo = PatientNo,
            InvDate = DraftDate,
            DraftDate = DraftDate,
            PayType = CashPayType,
            SubPayType = 1,
            ClinicId = ClinicId,
            DocId = DoctorId,
            CompCode = CashCompany,
            DeptWise = 0,
            Call = 0,
            DiscT = 0,
            InvNo = null,
        },
        Lines = lines,
        Parameters = new InvoiceEntryParameters { VisitUnique = VisitUnique },
        DiscountLimitChoice = null,
    });

    /// <summary>Returns the draft with the request id, draft date and seal that NewDraft issues when the database time is the draft's date.</summary>
    private static async Task<DraftDto> Issued(DraftDto draft)
    {
        var fakes = new FakeDataPorts();
        fakes.Lookups.DatabaseTime = draft.DraftDate;

        var issued = (await fakes.CreateService().NewDraft(new InvoiceEntryParameters(), Operator)).Draft;

        Assert.Equal(draft.DraftDate, issued.DraftDate);
        Assert.NotNull(issued.DraftSeal);
        return draft with { RequestId = issued.RequestId, DraftDate = issued.DraftDate, DraftSeal = issued.DraftSeal };
    }

    /// <summary>Cash patient whose only selected request row is row 5001 on S1 for visit V1 and pay type 1; S2 alone has an editable price.</summary>
    private static FakeDataPorts Arrange()
    {
        var fakes = new FakeDataPorts();
        fakes.Lookups.PatientCoverage = new PatientCoverageSnapshot { PatientNo = PatientNo, CompCode = CashCompany };
        fakes.Lookups.ClinicProfile = (clinicId, _) => new ClinicProfile { ClinicId = clinicId, SysCatType = "OPD" };
        fakes.Lookups.ServiceProfile = serviceId => new ServiceProfile
        {
            ServiceId = serviceId,
            ShowQty = 0,
            BeginOfClaim = 1,
            AddToQue = 0,
            ServLocId = 1,
            ConsRev = 0,
            IsPackage = 0,
            PriceIsFixed = string.Equals(serviceId, EditablePriceService, StringComparison.Ordinal) ? PriceNotFixed : PriceFixed,
            ReqNeedA = 0,
        };
        fakes.Invoices.CreateRequest = _ => null;
        fakes.Invoices.SelectedRequestRows = (patientNo, visitUnique, payType) =>
            patientNo == PatientNo && visitUnique == VisitUnique && payType == CashPayType
                ? new (long PatServReqRowId, string ServiceId, int? ReqAStatus, int? ReqNeedA, string? ApprovRefNo)[]
                {
                    (SelectedRowId, FixedPriceService, null, 0, null),
                }
                : Array.Empty<(long PatServReqRowId, string ServiceId, int? ReqAStatus, int? ReqNeedA, string? ApprovRefNo)>();
        return fakes;
    }

    private static void AssertFixedPriceRefused(IEnumerable<MessageDto> messages, string serviceId) =>
        Assert.Contains(messages, message =>
            IsBlocking(message)
            && message.Field == PriceItem
            && message.Text == $"Price cannot be changed for service {serviceId}.");

    private static bool IsBlocking(MessageDto message) =>
        string.Equals(message.Severity, ValidationMessage.Blocking, StringComparison.Ordinal);

    private static IReadOnlyList<IReadOnlyList<InvoiceLineDraft>> PreviewedLines(FakeDataPorts fakes) =>
        fakes.CallsTo(nameof(IBilInvoiceApiGateway.CalculatePreview))
            .Select(call => call.Arg<IReadOnlyList<InvoiceLineDraft>>())
            .ToArray();

    private static OracleParameter Find(IReadOnlyList<OracleParameter> parameters, string name) =>
        Assert.Single(parameters, parameter => parameter.ParameterName.TrimStart(':') == name);

    private static object?[] Plain(OracleParameter parameter) =>
        Assert.IsAssignableFrom<Array>(parameter.Value)
            .Cast<object?>()
            .Select(element => element switch
            {
                OracleDecimal { IsNull: false } number => number.Value,
                OracleString { IsNull: false } text => text.Value,
                OracleDecimal or OracleString => null,
                _ => element,
            })
            .ToArray();
}
