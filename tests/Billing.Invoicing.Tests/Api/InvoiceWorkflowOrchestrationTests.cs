using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Api.Errors;
using Billing.Invoicing.Api.Services;
using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;
using Oracle.ManagedDataAccess.Client;

namespace Billing.Invoicing.Tests.Api;

/// <summary>Create pre-flight, replay-first path and open-item gates of the invoice workflow over hand-written port fakes.</summary>
[Trait("Category", "Orchestration")]
public sealed class InvoiceWorkflowOrchestrationTests
{
    private const string PatientNo = "P100";
    private const int ClinicId = 5;
    private const int DoctorId = 12;
    private const string CashCompany = "0";
    private const string CreditCompany = "1001";
    private const string SubCompany = "SC1";
    private const int ClassCode = 3;
    private const string OrdinaryService = "S1";
    private const string PackageService = "PKG1";
    private const string FirstComponent = "C1";
    private const string SecondComponent = "C2";
    private const string PackageInstance = "PKGI-1";
    private const string ParentRole = "PARENT";
    private const string ComponentRole = "COMPONENT";
    private const string ClaimNumber = "CLM-9";
    private const string OpdCategory = "OPD";
    private const string ErCategory = "ER";
    private const string ForgedPreAuthorization = "PA-FORGED";
    private const string ReplayMessage = "Invoice 9001 was already created for this request.";
    private const string CommitEvent = "Commit";
    private const string RollbackEvent = "Rollback";
    private const string ReceptionTransferSavepointEvent = "Save:dr21";
    private const string InvDateField = "INVDATE";
    private const string LineField = "LINE";
    private const int BundledOfferType = 0;
    private const int BundledOfferId = 7;
    private const string DraftSealText = "Draft date does not match the date issued with this draft; start a new draft.";
    private const string CreateRequestEntry = $"{nameof(IInvoiceQueries)}.{nameof(IInvoiceQueries.GetCreateRequest)}";
    private const string SessionCommitEntry = $"{nameof(IOracleSession)}.{CommitEvent}";
    private const string SessionDisposeEntry = $"{nameof(IOracleSession)}.Dispose";
    private const int OnHold = 2;
    private const int PackageServiceLocation = 14;
    private const string VisitUnique = "V1";
    private const long SelectedRequestRowId = 7001L;
    private const string RequestImportFailedPrefix = "Request import failed: ";
    private const string ReservationList = "RESERV_NO";
    private const string CompCodeItem = "COMP_CODE";
    private const string SubCompCodeItem = "SUB_COMP_CODE";
    private const string DocIdItem = "DOCIDX";
    private const string PatientNoItem = "PATIENTNO";
    private const string PayTypeItem = "PAYTYPE";
    private const string InvDateItem = "INVDATE";
    private const string CashPayType = "1";
    private const string FixedPriceService = "S2";
    private const string RequestService = "S3";
    private const string OfferService = "S4";
    private const string OfferComponentService = "S5";
    private const string PriceNotFixed = "N";
    private const string PriceFixed = "Y";
    private const string PriceItem = "PRICE";
    private const string CoverageRowMissingText = "FRM-40735: WHEN-VALIDATE-ITEM trigger raised unhandled exception ORA-01403.";
    private const int ApprovalPreference = 422;
    private const string SecondService = "S2";
    private const string ThirdService = "S3";
    private const string UnrequestedService = "S9";
    private const string PaddedLowerCaseOrdinaryService = " s1 ";
    private const string SecondPackageService = "PKG2";
    private const string FirstPackageInstance = "PKGI-A";
    private const string SecondPackageInstance = "PKGI-B";
    private const decimal DefaultListId = 10m;
    private const decimal SecondListId = 20m;
    private const decimal OverriddenPrice = 25m;

    private static readonly OperatorContext Operator = new()
    {
        UserNo = 101,
        UserName = "TESTER",
        InfoCenterId = "1",
        MachineName = "TEST-PC",
        SessionId = "7D3F2C1B9A8E4F6D8C2B1A0F9E8D7C6B",
    };

    private static readonly DateTime DraftDate = new(2026, 9, 29, 10, 0, 0);

    private static readonly DateTime RecordedInvDate = new(2026, 9, 28, 16, 45, 0);

    private static readonly DateTimeOffset RecordedCompletedAt = new(2026, 9, 28, 16, 45, 30, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static string NewRequestId() => Guid.NewGuid().ToString("N").ToUpperInvariant();

    private static InvoiceLineDraft Line(string serviceId, string clientId) => new()
    {
        ServiceId = serviceId,
        Qty = 1m,
        DiscountType = "R",
        ClientId = clientId,
    };

    private static InvoiceLineDraft BundleLine(string serviceId, string clientId, string offerInstanceId) =>
        Line(serviceId, clientId) with
        {
            OfferId = BundledOfferId,
            OfferType = BundledOfferType,
            OfferInstanceId = offerInstanceId,
            OfferLineRole = ComponentRole,
        };

    private static InvoiceLineDraft PackageLine(string serviceId, string role, string clientId, int? componentOrder) =>
        Line(serviceId, clientId) with
        {
            PackageServiceId = PackageService,
            PackageInstanceId = PackageInstance,
            PackageLineRole = role,
            PackageComponentOrder = componentOrder,
        };

    private static Task<DraftDto> CashDraft() => Issued(new DraftDto
    {
        RequestId = NewRequestId(),
        DraftDate = DraftDate,
        Header = new InvoiceHeaderDraft
        {
            PatientNo = PatientNo,
            InvDate = DraftDate,
            DraftDate = DraftDate,
            PayType = 1,
            SubPayType = 1,
            ClinicId = ClinicId,
            DocId = DoctorId,
            CompCode = CashCompany,
            DeptWise = 0,
            Call = 0,
            DiscT = 0,
            InvNo = null,
        },
        Lines = new[] { Line(OrdinaryService, "c1") },
        Parameters = new InvoiceEntryParameters(),
        DiscountLimitChoice = null,
    });

    private static async Task<DraftDto> CreditDraft()
    {
        var draft = await CashDraft();
        return draft with
        {
            Header = draft.Header with { CompCode = CreditCompany, PayType = 2, ClassCode = ClassCode },
        };
    }

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

    private static DraftDto WithHeader(DraftDto draft, Func<InvoiceHeaderDraft, InvoiceHeaderDraft> change) =>
        draft with { Header = change(draft.Header) };

    private static PatientCoverageSnapshot ValidCoverage(DraftDto draft) =>
        string.Equals(draft.Header.CompCode, CashCompany, StringComparison.Ordinal)
            ? new PatientCoverageSnapshot { PatientNo = PatientNo, CompCode = CashCompany }
            : new PatientCoverageSnapshot
            {
                PatientNo = PatientNo,
                CompCode = CreditCompany,
                ClassCode = ClassCode,
                MyClass = ClassCode,
                ContractEnd = DraftDate.AddYears(1),
                CardEnd = DraftDate.AddYears(1),
                SubCompanyContractEnd = DraftDate.AddYears(1),
                CompanyIsActive = 1,
                CompanyType = 1,
                ClassIsActive = 1,
                ClassWithRef = 0,
                SubCompanyIsActive = 1,
                MaxDeductable = 0m,
            };

    /// <summary>Credit coverage carrying the sub-company that the workflow reads server-side for a credit draft.</summary>
    private static PatientCoverageSnapshot InsuredCoverage(DraftDto draft) =>
        ValidCoverage(draft) with { SubCompCode = SubCompany };

    private static ServiceProfile Profile(string serviceId) => new()
    {
        ServiceId = serviceId,
        ShowQty = 0,
        BeginOfClaim = 1,
        AddToQue = 0,
        ServLocId = 1,
        ConsRev = 0,
        IsPackage = 0,
        PkgType = null,
        PriceIsFixed = "Y",
        ReqNeedA = 0,
    };

    private static ServiceProfile PackageAwareProfile(string serviceId) =>
        string.Equals(serviceId, PackageService, StringComparison.Ordinal)
            ? Profile(serviceId) with { ServLocId = PackageServiceLocation, IsPackage = 1 }
            : Profile(serviceId);

    private static FakeDataPorts Arrange(DraftDto draft)
    {
        var fakes = new FakeDataPorts();
        fakes.Lookups.PatientCoverage = ValidCoverage(draft);
        fakes.Lookups.ClinicProfile = (clinicId, _) => new ClinicProfile { ClinicId = clinicId, SysCatType = OpdCategory };
        fakes.Lookups.ServiceProfile = Profile;
        fakes.Lookups.UserMaxDiscount = 100m;
        fakes.Lookups.ClassAdvancedMode = null;
        fakes.Lookups.PatientCardId = null;
        fakes.Lookups.RequestedServices = Array.Empty<string>();
        fakes.Invoices.CreateRequest = _ => null;
        fakes.Invoices.ClaimPreload = _ => null;
        return fakes;
    }

    private static CreateInvoiceRequest CreateRequest(DraftDto draft) => new() { Draft = draft };

    private static CreateInvoiceRequest ForgedRequest(DraftDto draft)
    {
        var json = JsonSerializer.SerializeToNode(CreateRequest(draft), Json)!.AsObject();
        var header = json["draft"]!["header"]!.AsObject();
        header["preAuthorization"] = ForgedPreAuthorization;
        header["subCompCode"] = SubCompany;

        var text = json.ToJsonString(Json);
        Assert.Contains(ForgedPreAuthorization, text, StringComparison.Ordinal);
        return JsonSerializer.Deserialize<CreateInvoiceRequest>(text, Json)!;
    }

    private static bool IsBlocking(MessageDto message) =>
        string.Equals(message.Severity, ValidationMessage.Blocking, StringComparison.Ordinal);

    private static bool HasBlocking(IEnumerable<MessageDto> messages, string rule) =>
        messages.Any(message => IsBlocking(message) && string.Equals(message.Rule, rule, StringComparison.Ordinal));

    private static bool HasWarning(IEnumerable<MessageDto> messages, string rule) =>
        messages.Any(message =>
            string.Equals(message.Severity, ValidationMessage.Warning, StringComparison.Ordinal)
            && string.Equals(message.Rule, rule, StringComparison.Ordinal));

    private static async Task<NotImplementedException> GateAsync(Func<Task> act, string openItemId)
    {
        var failure = await Assert.ThrowsAsync<NotImplementedException>(act);
        Assert.StartsWith(openItemId + ":", failure.Message, StringComparison.Ordinal);
        return failure;
    }

    private static IReadOnlyList<string> GateIds(Exception failure) =>
        Assert.IsAssignableFrom<IReadOnlyList<string>>(failure.Data[ProblemDetailsWriter.OpenItemsDataKey]);

    private static int CreateCalls(FakeDataPorts fakes) =>
        fakes.InvoiceApi.Calls.Count(call => call.Method == nameof(IBilInvoiceApiGateway.CreateFullInvoice));

    private static FakeCall CreateCall(FakeDataPorts fakes) =>
        Assert.Single(fakes.InvoiceApi.Calls, call => call.Method == nameof(IBilInvoiceApiGateway.CreateFullInvoice));

    /// <summary>Asserts the single blocking INVDATE message of a refused draft date, and that only the replay-first read ran.</summary>
    private static void AssertDraftDateRejected(CreateInvoiceOutcome response, FakeDataPorts fakes)
    {
        Assert.Null(response.InvNo);
        var message = Assert.Single(response.Messages);
        Assert.True(IsBlocking(message));
        Assert.Equal(InvDateField, message.Field);
        Assert.Equal(DraftSealText, message.Text);
        Assert.Null(message.Rule);
        Assert.Empty(response.OpenItems);
        Assert.Equal(0, CreateCalls(fakes));
        Assert.False(Called(fakes.Lookups.Calls, nameof(ILookupQueries.GetPatientCoverage)));
        Assert.Empty(fakes.SessionFactory.Calls);
        Assert.Equal(new[] { $"{nameof(IInvoiceQueries)}.{nameof(IInvoiceQueries.GetCreateRequest)}" }, fakes.Journal);
    }

    /// <summary>Asserts the single blocking LINE message of a draft over the line cap, and that only the replay-first read ran.</summary>
    private static void AssertLinesRejected(CreateInvoiceOutcome response, FakeDataPorts fakes, string expectedText)
    {
        Assert.Null(response.InvNo);
        var message = Assert.Single(response.Messages);
        Assert.True(IsBlocking(message));
        Assert.Equal(LineField, message.Field);
        Assert.Equal(expectedText, message.Text);
        Assert.Null(message.Rule);
        Assert.Empty(response.OpenItems);
        Assert.Empty(fakes.Lookups.Calls);
        Assert.Empty(fakes.SessionFactory.Calls);
        Assert.Equal(new[] { CreateRequestEntry }, fakes.Journal);
    }

    /// <summary>Create-request row recorded for <paramref name="invNo"/>; with no invoice the completion time and the invoice fields are null.</summary>
    private static (long? InvNo, string? PatientNo, DateTimeOffset? CompletedAt, DateTime? InvDate, string? CompCode, string? SubCompCode, bool ClinicHasAgeLimit) Recorded(
        long? invNo, string? compCode, string? subCompCode, bool clinicHasAgeLimit) =>
        invNo is null
            ? (null, PatientNo, null, null, null, null, false)
            : (invNo, PatientNo, RecordedCompletedAt, RecordedInvDate, compCode, subCompCode, clinicHasAgeLimit);

    /// <summary>Create-request fake answering the draft's request id with <paramref name="recorded"/> and any other id with null.</summary>
    private static Func<string, (long? InvNo, string? PatientNo, DateTimeOffset? CompletedAt, DateTime? InvDate, string? CompCode, string? SubCompCode, bool ClinicHasAgeLimit)?> RecordedFor(
        DraftDto draft,
        (long? InvNo, string? PatientNo, DateTimeOffset? CompletedAt, DateTime? InvDate, string? CompCode, string? SubCompCode, bool ClinicHasAgeLimit) recorded) =>
        RecordedFor(draft, () => recorded);

    /// <summary>Create-request fake answering each read of the draft's request id from <paramref name="recorded"/> and any other id with null.</summary>
    private static Func<string, (long? InvNo, string? PatientNo, DateTimeOffset? CompletedAt, DateTime? InvDate, string? CompCode, string? SubCompCode, bool ClinicHasAgeLimit)?> RecordedFor(
        DraftDto draft,
        Func<(long? InvNo, string? PatientNo, DateTimeOffset? CompletedAt, DateTime? InvDate, string? CompCode, string? SubCompCode, bool ClinicHasAgeLimit)?> recorded) =>
        requestId => string.Equals(requestId, draft.RequestId, StringComparison.Ordinal) ? recorded() : null;

    private static bool AnySessionCommitted(FakeDataPorts fakes) =>
        fakes.SessionFactory.Sessions.Any(session => session.Committed);

    private static bool Called(IEnumerable<FakeCall> calls, string method) =>
        calls.Any(call => call.Method == method);

    private static ValidateDraftRequest PatientValidation(DraftDto draft) => new() { Draft = draft, Target = "PATIENTNO" };

    private static int CoverageReads(FakeDataPorts fakes) =>
        fakes.Lookups.Calls.Count(call => call.Method == nameof(ILookupQueries.GetPatientCoverage));

    private static IEnumerable<IReadOnlyList<InvoiceLineDraft>> PreviewedLines(FakeDataPorts fakes) =>
        fakes.InvoiceApi.Calls
            .Where(call => call.Method == nameof(IBilInvoiceApiGateway.CalculatePreview))
            .Select(call => call.Arg<IReadOnlyList<InvoiceLineDraft>>());

    [Fact]
    public async Task Create_BaselineCashDraft_Succeeds()
    {
        var draft = await CashDraft();
        var fakes = Arrange(draft);

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.DoesNotContain(response.Messages, IsBlocking);
        Assert.Equal(1, CreateCalls(fakes));
        Assert.Contains(CommitEvent, CreateCall(fakes).Arg<FakeOracleSession>().Events);
        Assert.Contains(OpenItemIds.OI20, response.OpenItems);
        Assert.Contains(OpenItemIds.OI12, response.OpenItems);
        Assert.Contains(OpenItemIds.OI45, response.OpenItems);
    }

    [Fact]
    public async Task Create_BaselineCreditDraft_Succeeds()
    {
        var draft = await CreditDraft();
        var fakes = Arrange(draft);

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.DoesNotContain(response.Messages, IsBlocking);
        Assert.Equal(1, CreateCalls(fakes));
        Assert.Contains(CommitEvent, CreateCall(fakes).Arg<FakeOracleSession>().Events);
        Assert.Contains(OpenItemIds.OI20, response.OpenItems);
        Assert.Equal(2, CreateCall(fakes).Arg<InvoiceHeaderDraft>().PayType);
    }

    [Fact]
    [Trait("Decision", "D-55")]
    public async Task Create_WithoutPriorValidation_ReturnsBlockingAndWarningMessagesTogether()
    {
        var draft = WithHeader(await CreditDraft(), header => header with { DiscT = 1, FinalDiscPerc = 30m });
        var fakes = Arrange(draft);
        fakes.Lookups.PatientCoverage = InsuredCoverage(draft) with { CompanyIsActive = OnHold };
        fakes.Lookups.UserMaxDiscount = 10m;
        fakes.Lookups.ServiceProfile = serviceId => Profile(serviceId) with { BeginOfClaim = 0 };

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.True(HasBlocking(response.Messages, "DR-03"));
        Assert.True(HasBlocking(response.Messages, "DR-06"));
        Assert.True(HasWarning(response.Messages, "DR-16"));
        Assert.Null(response.InvNo);
        Assert.Equal(0, CreateCalls(fakes));
        Assert.False(AnySessionCommitted(fakes));
    }

    [Fact]
    [Trait("Decision", "D-55")]
    public async Task Create_AfterPatientValidation_RereadsChangedCoverage()
    {
        var draft = await CreditDraft();
        var fakes = Arrange(draft);
        var service = fakes.CreateService();

        var validated = await service.Validate(new ValidateDraftRequest { Draft = draft, Target = "PATIENTNO" }, Operator);
        Assert.DoesNotContain(validated.Messages, IsBlocking);

        fakes.Lookups.PatientCoverage = ValidCoverage(draft) with { CompanyIsActive = OnHold };
        var response = await service.Create(CreateRequest(draft), Operator);

        Assert.True(HasBlocking(response.Messages, "DR-03"));
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Fact]
    [Trait("Decision", "D-55")]
    [Trait("OpenItem", "OI-32")]
    public async Task Create_AfterLineValidation_RereadsChangedServiceProfile()
    {
        var draft = await CreditDraft();
        var fakes = Arrange(draft);
        var service = fakes.CreateService();

        var validated = await service.Validate(
            new ValidateDraftRequest { Draft = draft, Target = "SERVICEID", LineIndex = 0 }, Operator);
        Assert.DoesNotContain(validated.Messages, IsBlocking);

        fakes.Lookups.ServiceProfile = serviceId => Profile(serviceId) with { ConsRev = 1 };

        await GateAsync(() => service.Create(CreateRequest(draft), Operator), OpenItemIds.OI32);
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Fact]
    [Trait("Decision", "D-55")]
    public async Task Create_AfterDiscountValidation_RereadsLoweredMaximumDiscount()
    {
        var draft = WithHeader(await CreditDraft(), header => header with { DiscT = 1, FinalDiscPerc = 5m });
        var fakes = Arrange(draft);
        fakes.Lookups.UserMaxDiscount = 10m;
        var service = fakes.CreateService();

        var validated = await service.Validate(
            new ValidateDraftRequest { Draft = draft, Target = "FINALDISC_PERC" }, Operator);
        Assert.DoesNotContain(validated.Messages, IsBlocking);

        fakes.Lookups.UserMaxDiscount = 2m;
        var response = await service.Create(CreateRequest(draft), Operator);

        Assert.True(HasBlocking(response.Messages, "DR-06"));
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Fact]
    [Trait("Decision", "D-52")]
    [Trait("OpenItem", "OI-32")]
    public async Task Create_ForgedPreAuthorizationWithQueuedService_GatesOi32()
    {
        var request = ForgedRequest(await CreditDraft());
        var fakes = Arrange(request.Draft);
        fakes.Lookups.PatientCoverage = InsuredCoverage(request.Draft);
        fakes.Lookups.ServiceProfile = serviceId => Profile(serviceId) with { AddToQue = 1 };

        var failure = await GateAsync(() => fakes.CreateService().Create(request, Operator), OpenItemIds.OI32);

        Assert.Contains(OpenItemIds.OI32, GateIds(failure));
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Fact]
    [Trait("Decision", "D-52")]
    [Trait("OpenItem", "OI-21")]
    public async Task Create_ForgedPreAuthorizationWithUnqueuedService_BindsNoPreAuthorization()
    {
        var request = ForgedRequest(await CreditDraft());
        var fakes = Arrange(request.Draft);
        fakes.Lookups.PatientCoverage = InsuredCoverage(request.Draft);

        var response = await fakes.CreateService().Create(request, Operator);

        Assert.DoesNotContain(response.Messages, IsBlocking);
        Assert.Null(CreateCall(fakes).Arg<InvoiceHeaderDraft>().PreAuthorization);
        Assert.Contains(OpenItemIds.OI21, response.OpenItems);
    }

    [Fact]
    [Trait("Decision", "D-52")]
    [Trait("OpenItem", "OI-32")]
    public async Task Create_CashDraftWithServerReadCard_GatesOi32()
    {
        var draft = await CashDraft();
        var fakes = Arrange(draft);
        fakes.Lookups.PatientCardId = 42;

        var failure = await GateAsync(() => fakes.CreateService().Create(CreateRequest(draft), Operator), OpenItemIds.OI32);

        Assert.Contains(OpenItemIds.OI32, GateIds(failure));
        Assert.True(Called(fakes.Lookups.Calls, nameof(ILookupQueries.GetPatientCardId)));
        Assert.Equal(0, CreateCalls(fakes));
    }


    [Fact]
    [Trait("Decision", "D-51")]
    [Trait("OpenItem", "OI-23")]
    public async Task PreviewAndCreate_CreditWithDeductible_GateOi23()
    {
        var draft = await CreditDraft();
        var fakes = Arrange(draft);
        fakes.Lookups.PatientCoverage = ValidCoverage(draft) with { MaxDeductable = 50m };
        var service = fakes.CreateService();

        await GateAsync(() => service.Preview(draft, Operator), OpenItemIds.OI23);
        var failure = await GateAsync(() => service.Create(CreateRequest(draft), Operator), OpenItemIds.OI23);

        Assert.Contains(OpenItemIds.OI23, GateIds(failure));
        Assert.True(Called(fakes.Legacy.Calls, nameof(ILegacyExternalCalls.GetPaidBefore)));
        Assert.False(Called(fakes.InvoiceApi.Calls, nameof(IBilInvoiceApiGateway.CalculatePreview)));
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Fact]
    [Trait("Decision", "D-51")]
    [Trait("OpenItem", "OI-23")]
    public async Task PreviewAndCreate_CreditWithAdvancedClass_GateOi23()
    {
        var draft = await CreditDraft();
        var fakes = Arrange(draft);
        fakes.Lookups.PatientCoverage = InsuredCoverage(draft) with { MaxDeductable = 0m };
        fakes.Lookups.ClassAdvancedMode = 2;
        var service = fakes.CreateService();

        await GateAsync(() => service.Preview(draft, Operator), OpenItemIds.OI23);
        var failure = await GateAsync(() => service.Create(CreateRequest(draft), Operator), OpenItemIds.OI23);

        Assert.Contains(OpenItemIds.OI23, GateIds(failure));
        Assert.True(Called(fakes.Lookups.Calls, nameof(ILookupQueries.GetClassAdvancedMode)));
        Assert.False(Called(fakes.InvoiceApi.Calls, nameof(IBilInvoiceApiGateway.CalculatePreview)));
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Fact]
    [Trait("Decision", "D-37")]
    public async Task Create_ExpandedPackageWithQueuedComponent_SendsAddToListOne()
    {
        var draft = await ExpandedPackageDraft();
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = PackageAwareProfile;
        fakes.Lookups.ServiceQueueFlags = serviceIds => serviceIds.ToDictionary(
            serviceId => serviceId,
            serviceId => string.Equals(serviceId, FirstComponent, StringComparison.Ordinal) ? 1 : 0,
            StringComparer.Ordinal);

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.DoesNotContain(response.Messages, IsBlocking);
        var queueFlagIds = QueueFlagIds(fakes);
        Assert.Contains(FirstComponent, queueFlagIds);
        Assert.Contains(SecondComponent, queueFlagIds);
        Assert.Equal((int?)1, CreateCall(fakes).Arg<InvoiceHeaderDraft>().AddToList);
    }

    [Fact]
    [Trait("Decision", "D-37")]
    public async Task Create_ExpandedPackageWithoutQueuedComponent_SendsAddToListZero()
    {
        var draft = await ExpandedPackageDraft();
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = PackageAwareProfile;
        fakes.Lookups.ServiceQueueFlags = serviceIds => serviceIds.ToDictionary(
            serviceId => serviceId,
            _ => 0,
            StringComparer.Ordinal);

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.DoesNotContain(response.Messages, IsBlocking);
        var queueFlagIds = QueueFlagIds(fakes);
        Assert.Contains(FirstComponent, queueFlagIds);
        Assert.Contains(SecondComponent, queueFlagIds);
        Assert.Equal((int?)0, CreateCall(fakes).Arg<InvoiceHeaderDraft>().AddToList);
    }

    [Fact]
    [Trait("Decision", "D-53")]
    [Trait("OpenItem", "OI-33")]
    public async Task Create_DeptWiseAtErClinic_GatesOi33()
    {
        var draft = WithHeader(await CashDraft(), header => header with { DeptWise = 1 });
        var fakes = Arrange(draft);
        fakes.Lookups.ClinicProfile = (clinicId, _) => new ClinicProfile { ClinicId = clinicId, SysCatType = ErCategory };

        var failure = await GateAsync(() => fakes.CreateService().Create(CreateRequest(draft), Operator), OpenItemIds.OI33);

        Assert.Contains(OpenItemIds.OI33, GateIds(failure));
        Assert.True(Called(fakes.Lookups.Calls, nameof(ILookupQueries.GetClinicProfile)));
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Fact]
    [Trait("Decision", "D-53")]
    [Trait("OpenItem", "OI-33")]
    public async Task Create_CallFlagWithoutDeptWise_GatesOi33()
    {
        var draft = WithHeader(await CashDraft(), header => header with { Call = 1, DeptWise = 0 });
        var fakes = Arrange(draft);

        var failure = await GateAsync(() => fakes.CreateService().Create(CreateRequest(draft), Operator), OpenItemIds.OI33);

        Assert.Contains(OpenItemIds.OI33, GateIds(failure));
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Fact]
    [Trait("Decision", "D-53")]
    [Trait("OpenItem", "OI-33")]
    public async Task Create_DeptWiseAtNonErClinic_ReturnsBlockingDr05BeforeGate()
    {
        var draft = WithHeader(await CashDraft(), header => header with { DeptWise = 1 });
        var fakes = Arrange(draft);

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.True(HasBlocking(response.Messages, "DR-05"));
        Assert.Contains(OpenItemIds.OI33, response.OpenItems);
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Fact]
    [Trait("Decision", "D-38")]
    [Trait("OpenItem", "OI-24")]
    public async Task ImportPackage_OnlyPackageParentWithoutClaimPreload_GatesOi24()
    {
        var draft = (await CashDraft()) with { Lines = new[] { Line(PackageService, "p1") } };
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = PackageAwareProfile;

        await GateAsync(() => fakes.CreateService().ImportPackage(PackageRequest(draft), Operator), OpenItemIds.OI24);

        Assert.True(Called(fakes.Legacy.Calls, nameof(ILegacyExternalCalls.ResolvePricePlan)));
        Assert.False(Called(fakes.InvoiceApi.Calls, nameof(IBilInvoiceApiGateway.GetPackageLines)));
        Assert.DoesNotContain(PreviewedLines(fakes), lines => lines.Any(IsPackageParent));
    }

    [Fact]
    [Trait("Decision", "D-38")]
    public async Task ImportPackage_OnlyPackageParentWithClaimPreload_UsesPreloadList()
    {
        var draft = (await CashDraft()) with
        {
            Lines = new[] { Line(PackageService, "p1") },
            Parameters = new InvoiceEntryParameters { ClaimNo = ClaimNumber },
        };
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = PackageAwareProfile;
        fakes.Invoices.ClaimPreload = claimNo => string.Equals(claimNo, ClaimNumber, StringComparison.Ordinal)
            ? (new InvoiceHeaderDraft { PatientNo = PatientNo, CompCode = CashCompany, PayType = 1 }, 55m, null, null)
            : null;

        await fakes.CreateService().ImportPackage(PackageRequest(draft), Operator);

        var packageCall = Assert.Single(
            fakes.InvoiceApi.Calls, call => call.Method == nameof(IBilInvoiceApiGateway.GetPackageLines));
        Assert.Equal(55m, packageCall.Arg<decimal>());
        Assert.Equal(PackageService, packageCall.Arg<string>());
        Assert.DoesNotContain(PreviewedLines(fakes), lines => lines.Any(IsPackageParent));
    }

    [Fact]
    [Trait("Decision", "D-38")]
    public async Task ImportPackage_AfterOrdinaryLine_UsesThatLinePreviewList()
    {
        var draft = (await CashDraft()) with { Lines = new[] { Line(OrdinaryService, "c1"), Line(PackageService, "p1") } };
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = PackageAwareProfile;
        fakes.InvoiceApi.PreviewListId = 77m;

        await fakes.CreateService().ImportPackage(PackageRequest(draft), Operator);

        var packageCall = Assert.Single(
            fakes.InvoiceApi.Calls, call => call.Method == nameof(IBilInvoiceApiGateway.GetPackageLines));
        Assert.Equal(77m, packageCall.Arg<decimal>());
        Assert.Contains(PreviewedLines(fakes), lines => lines.Any(line => line.ServiceId == OrdinaryService));
        Assert.DoesNotContain(PreviewedLines(fakes), lines => lines.Any(IsPackageParent));
    }

    [Fact]
    [Trait("Decision", "D-38")]
    [Trait("OpenItem", "OI-24")]
    public async Task ValidateLine_LonePlainPackageParentWithoutList_GatesOi24AfterOnePreview()
    {
        var draft = (await CashDraft()) with { Lines = new[] { Line(PackageService, "p1") } };
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = PackageAwareProfile;
        fakes.InvoiceApi.PreviewFailure = RefuseUnexpandedParent;

        await GateAsync(
            () => fakes.CreateService().Validate(LineTarget(draft, 0), Operator),
            OpenItemIds.OI24);

        Assert.True(Called(fakes.Legacy.Calls, nameof(ILegacyExternalCalls.ResolvePricePlan)));
        Assert.Single(PreviewedLines(fakes));
    }

    [Fact]
    [Trait("Decision", "D-38")]
    [Trait("OpenItem", "OI-24")]
    public async Task Create_LonePlainPackageParentWithoutList_GatesOi24AfterOnePreview()
    {
        var draft = (await CashDraft()) with { Lines = new[] { Line(PackageService, "p1") } };
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = PackageAwareProfile;
        fakes.InvoiceApi.PreviewFailure = RefuseUnexpandedParent;

        await GateAsync(() => fakes.CreateService().Create(CreateRequest(draft), Operator), OpenItemIds.OI24);

        Assert.True(Called(fakes.Legacy.Calls, nameof(ILegacyExternalCalls.ResolvePricePlan)));
        Assert.Single(PreviewedLines(fakes));
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Fact]
    [Trait("Decision", "D-38")]
    public async Task Create_PackageRefusalOfPlainLines_PropagatesWithoutProbing()
    {
        var draft = await PlainLinesDraft(OrdinaryService, "S2", "S3");
        var fakes = Arrange(draft);
        fakes.InvoiceApi.PreviewFailure = (_, _) => FakeOracleFailures.DiscountRefused();

        var failure = await Assert.ThrowsAsync<OracleException>(
            () => fakes.CreateService().Create(CreateRequest(draft), Operator));

        Assert.Equal(20906, failure.Number);
        Assert.Equal(3, Assert.Single(PreviewedLines(fakes)).Count);
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Fact]
    [Trait("Decision", "D-38")]
    [Trait("OpenItem", "OI-24")]
    public async Task Create_UnexpandedParentAmongPlainLines_GatesOi24AfterOnePreview()
    {
        var draft = await PlainLinesDraft(OrdinaryService, PackageService, "S2");
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = PackageAwareProfile;
        fakes.InvoiceApi.PreviewFailure = RefuseUnexpandedParent;

        await GateAsync(() => fakes.CreateService().Create(CreateRequest(draft), Operator), OpenItemIds.OI24);

        Assert.True(Called(fakes.Legacy.Calls, nameof(ILegacyExternalCalls.ResolvePricePlan)));
        Assert.Single(PreviewedLines(fakes));
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Fact]
    [Trait("Decision", "D-38")]
    public async Task Create_ServiceProfileConnectivityFailure_PropagatesDespiteBlockingField()
    {
        var draft = WithHeader(await CashDraft(), header => header with { DocId = null });
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = _ => throw FakeOracleFailures.NoListener();

        var failure = await Assert.ThrowsAsync<OracleException>(
            () => fakes.CreateService().Create(CreateRequest(draft), Operator));

        Assert.Equal(12541, failure.Number);
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Fact]
    [Trait("Decision", "D-38")]
    public async Task ValidateLine_ServiceProfileConnectivityFailure_PropagatesDespiteBlockingLine()
    {
        var draft = (await CashDraft()) with
        {
            Lines = new[] { Line(OrdinaryService, "c1") with { Qty = 0m }, Line("S2", "c2") },
        };
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = _ => throw FakeOracleFailures.NoListener();

        var failure = await Assert.ThrowsAsync<OracleException>(
            () => fakes.CreateService().Validate(LineTarget(draft, 0), Operator));

        Assert.Equal(12541, failure.Number);
    }

    [Fact]
    [Trait("Decision", "D-38")]
    public async Task Create_FallbackProfileReadFailure_PropagatesAfterPackageRefusal()
    {
        var draft = WithHeader(await CashDraft(), header => header with { DocId = null }) with
        {
            Parameters = new InvoiceEntryParameters { ClaimNo = ClaimNumber },
        };
        var fakes = Arrange(draft);
        fakes.Invoices.ClaimPreload = claimNo => string.Equals(claimNo, ClaimNumber, StringComparison.Ordinal)
            ? (new InvoiceHeaderDraft { PatientNo = PatientNo, CompCode = CashCompany, PayType = 1 }, 55m, null, null)
            : null;
        var profileReads = 0;
        fakes.Lookups.ServiceProfile = serviceId =>
            ++profileReads == 1 ? Profile(serviceId) : throw FakeOracleFailures.NoListener();
        fakes.InvoiceApi.PreviewFailure = (_, _) => FakeOracleFailures.DiscountRefused();

        var failure = await Assert.ThrowsAsync<OracleException>(
            () => fakes.CreateService().Create(CreateRequest(draft), Operator));

        Assert.Equal(12541, failure.Number);
        Assert.Single(PreviewedLines(fakes));
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Fact]
    [Trait("Decision", "D-38")]
    public async Task Create_PackageRefusalWithBlockingField_ReturnsBlockingMessages()
    {
        var draft = WithHeader(await CashDraft(), header => header with { DocId = null });
        var fakes = Arrange(draft);
        fakes.InvoiceApi.PreviewFailure = (_, _) => FakeOracleFailures.DiscountRefused();

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.True(HasBlocking(response.Messages, "DR-01"));
        Assert.Null(response.InvNo);
        Assert.Single(PreviewedLines(fakes));
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Fact]
    [Trait("Decision", "D-38")]
    [Trait("OpenItem", "OI-24")]
    public async Task Create_UnexpandedParentWithBlockingField_ListsOi24WithBlockingMessages()
    {
        var draft = WithHeader(await CashDraft(), header => header with { DocId = null }) with
        {
            Lines = new[] { Line(PackageService, "p1") },
        };
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = PackageAwareProfile;
        fakes.InvoiceApi.PreviewFailure = RefuseUnexpandedParent;

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.True(HasBlocking(response.Messages, "DR-01"));
        Assert.Contains(OpenItemIds.OI24, response.OpenItems);
        Assert.Null(response.InvNo);
        Assert.Single(PreviewedLines(fakes));
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Fact]
    [Trait("Decision", "D-38")]
    public async Task ValidateLine_LonePlainPackageParentWithClaimPreload_IsNotPreviewed()
    {
        var draft = (await CashDraft()) with
        {
            Lines = new[] { Line(PackageService, "p1") },
            Parameters = new InvoiceEntryParameters { ClaimNo = ClaimNumber },
        };
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = PackageAwareProfile;
        fakes.Invoices.ClaimPreload = PreloadWithList;
        fakes.InvoiceApi.PreviewFailure = RefuseUnexpandedParent;

        await fakes.CreateService().Validate(LineTarget(draft, 0), Operator);

        Assert.Empty(PreviewedLines(fakes));
        Assert.False(Called(fakes.Legacy.Calls, nameof(ILegacyExternalCalls.ResolvePricePlan)));
    }

    [Fact]
    [Trait("Decision", "D-38")]
    public async Task Create_PlainPackageParentWithClaimPreload_IsExcludedFromEveryPreview()
    {
        var draft = (await CashDraft()) with
        {
            Lines = new[] { Line(OrdinaryService, "c1"), Line(PackageService, "p1") },
            Parameters = new InvoiceEntryParameters { ClaimNo = ClaimNumber },
        };
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = PackageAwareProfile;
        fakes.Invoices.ClaimPreload = PreloadWithList;
        fakes.InvoiceApi.PreviewFailure = RefuseUnexpandedParent;

        await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.NotEmpty(PreviewedLines(fakes));
        Assert.DoesNotContain(PreviewedLines(fakes), lines => lines.Any(IsPackageParent));
        Assert.False(Called(fakes.Legacy.Calls, nameof(ILegacyExternalCalls.ResolvePricePlan)));
    }

    [Fact]
    [Trait("Decision", "D-38")]
    public async Task Create_ServiceProfileApplicationError_PropagatesDespiteBlockingField()
    {
        var draft = WithHeader(await CashDraft(), header => header with { DocId = null });
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = _ => throw FakeOracleFailures.LookupApplicationError();

        var failure = await Assert.ThrowsAsync<OracleException>(
            () => fakes.CreateService().Create(CreateRequest(draft), Operator));

        Assert.Equal(20001, failure.Number);
        Assert.Single(PreviewedLines(fakes));
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Fact]
    [Trait("Decision", "D-38")]
    public async Task ValidateLine_ServiceProfileApplicationError_PropagatesDespiteBlockingLine()
    {
        var draft = (await CashDraft()) with
        {
            Lines = new[] { Line(OrdinaryService, "c1") with { Qty = 0m }, Line("S2", "c2") },
        };
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = _ => throw FakeOracleFailures.LookupApplicationError();

        var failure = await Assert.ThrowsAsync<OracleException>(
            () => fakes.CreateService().Validate(LineTarget(draft, 0), Operator));

        Assert.Equal(20001, failure.Number);
    }

    [Fact]
    [Trait("Decision", "D-38")]
    public async Task Create_PriceOverrideProfileApplicationError_PropagatesDespiteBlockingField()
    {
        var draft = WithHeader(await CashDraft(), header => header with { DocId = null }) with
        {
            Lines = new[] { Line(OrdinaryService, "c1") with { PriceOverride = 25m } },
        };
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = _ => throw FakeOracleFailures.LookupApplicationError();

        var failure = await Assert.ThrowsAsync<OracleException>(
            () => fakes.CreateService().Create(CreateRequest(draft), Operator));

        Assert.Equal(20001, failure.Number);
        Assert.Single(PreviewedLines(fakes));
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Fact]
    [Trait("Decision", "D-38")]
    public async Task Create_QueueFlagsApplicationError_PropagatesDespiteBlockingField()
    {
        var draft = WithHeader(await CashDraft(), header => header with { DocId = null });
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceQueueFlags = _ => throw FakeOracleFailures.LookupApplicationError();

        var failure = await Assert.ThrowsAsync<OracleException>(
            () => fakes.CreateService().Create(CreateRequest(draft), Operator));

        Assert.Equal(20001, failure.Number);
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Fact]
    [Trait("Decision", "D-38")]
    public async Task Create_RequestedServicesApplicationError_PropagatesDespiteBlockingField()
    {
        var draft = WithHeader(await CashDraft(), header => header with { DocId = null });
        var fakes = Arrange(draft);
        fakes.Lookups.RequestedServicesFailure = FakeOracleFailures.LookupApplicationError;

        var failure = await Assert.ThrowsAsync<OracleException>(
            () => fakes.CreateService().Create(CreateRequest(draft), Operator));

        Assert.Equal(20001, failure.Number);
        Assert.True(Called(fakes.Lookups.Calls, nameof(ILookupQueries.GetRequestedServices)));
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Fact]
    [Trait("Decision", "D-38")]
    public async Task ValidateLine_RequestedServicesApplicationError_PropagatesDespiteBlockingLine()
    {
        var draft = (await CashDraft()) with
        {
            Lines = new[] { Line(OrdinaryService, "c1") with { Qty = 0m }, Line("S2", "c2") },
        };
        var fakes = Arrange(draft);
        fakes.Lookups.RequestedServicesFailure = FakeOracleFailures.LookupApplicationError;

        var failure = await Assert.ThrowsAsync<OracleException>(
            () => fakes.CreateService().Validate(LineTarget(draft, 0), Operator));

        Assert.Equal(20001, failure.Number);
        Assert.True(Called(fakes.Lookups.Calls, nameof(ILookupQueries.GetRequestedServices)));
    }

    [Fact]
    [Trait("Decision", "D-54")]
    [Trait("OpenItem", "OI-20")]
    public async Task Create_RecordedRequestId_ReplaysBeforePreflight()
    {
        var draft = await CreditDraft();
        var fakes = Arrange(draft);
        fakes.Invoices.CreateRequest = RecordedFor(draft, Recorded(9001L, CreditCompany, null, false));
        fakes.Lookups.PatientCoverage = ValidCoverage(draft) with { CompanyIsActive = OnHold };
        fakes.Lookups.ServiceProfile = serviceId => Profile(serviceId) with { ConsRev = 1 };
        fakes.InvoiceApi.CreateMessage = ReplayMessage;

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.DoesNotContain(response.Messages, IsBlocking);
        Assert.Equal(1, CreateCalls(fakes));
        Assert.Equal(draft.RequestId, CreateCall(fakes).Arg<string>());
        Assert.False(Called(fakes.Lookups.Calls, nameof(ILookupQueries.GetPatientCoverage)));
        Assert.False(Called(fakes.Invoices.Calls, nameof(IInvoiceQueries.GetInvoice)));
        Assert.False(Called(fakes.Lookups.Calls, nameof(ILookupQueries.GetClinicProfile)));
        Assert.Empty(fakes.PatientTransfer.Calls);
        Assert.False(Called(fakes.Legacy.Calls, nameof(ILegacyExternalCalls.ValidateTotalInvoice)));
        Assert.DoesNotContain(
            fakes.SessionFactory.Sessions.SelectMany(session => session.Events),
            sessionEvent => sessionEvent == ReceptionTransferSavepointEvent);
        Assert.Equal(ReplayMessage, response.Message);
        Assert.Contains(OpenItemIds.OI20, response.OpenItems);
    }

    [Theory]
    [Trait("Decision", "D-39")]
    [InlineData(-400 * TimeSpan.TicksPerDay)]
    [InlineData(-1L)]
    [InlineData(1L)]
    [InlineData(30 * TimeSpan.TicksPerDay)]
    public async Task Create_DraftDateChangedAfterIssue_ReturnsBlockingInvDateBeforeAnyRead(long shiftTicks)
    {
        var issued = await CreditDraft();
        var draft = issued with { DraftDate = issued.DraftDate.AddTicks(shiftTicks) };
        var fakes = Arrange(draft);

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        AssertDraftDateRejected(response, fakes);
    }

    [Theory]
    [Trait("Decision", "D-39")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("A1B2")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task Create_MissingOrUnissuedSeal_ReturnsBlockingInvDateBeforeAnyRead(string? seal)
    {
        var draft = (await CreditDraft()) with { DraftSeal = seal };
        var fakes = Arrange(draft);

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        AssertDraftDateRejected(response, fakes);
    }

    [Fact]
    [Trait("Decision", "D-39")]
    public async Task Create_SealIssuedForAnotherRequestId_ReturnsBlockingInvDateBeforeAnyRead()
    {
        var other = await CreditDraft();
        var draft = (await CreditDraft()) with { DraftSeal = other.DraftSeal };
        var fakes = Arrange(draft);

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.NotEqual(other.RequestId, draft.RequestId);
        AssertDraftDateRejected(response, fakes);
    }

    [Fact]
    public async Task Create_Refused_ReturnsNoInvoice()
    {
        var draft = WithHeader(await CashDraft(), header => header with { DeptWise = 1 });
        var fakes = Arrange(draft);

        var outcome = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.Null(outcome.Invoice);
        Assert.Null(outcome.InvNo);
        Assert.Contains(outcome.Messages, IsBlocking);
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Fact]
    public async Task Create_Saved_ReturnsTheInvoiceNumberThePackageAssigned()
    {
        var draft = await CashDraft();
        var fakes = Arrange(draft);
        fakes.InvoiceApi.InvoiceNo = 4321;

        var outcome = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        var invoice = Assert.IsType<CreateInvoiceResponse>(outcome.Invoice);
        Assert.Equal(4321L, invoice.InvNo);
        Assert.Equal(invoice.Messages, outcome.Messages);
        Assert.Equal(invoice.OpenItems, outcome.OpenItems);
    }

    [Fact]
    [Trait("Decision", "D-54")]
    public async Task Create_Replay_ReturnsTheInvoiceNumberThePackageReturned()
    {
        var draft = await CreditDraft();
        var fakes = Arrange(draft);
        fakes.Invoices.CreateRequest = RecordedFor(draft, Recorded(7777L, CreditCompany, null, false));
        fakes.InvoiceApi.InvoiceNo = 7777;

        var outcome = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.Equal(7777L, Assert.IsType<CreateInvoiceResponse>(outcome.Invoice).InvNo);
    }

    [Fact]
    public async Task Create_PackageReturnsNoInvoiceNumber_RollsBackWithoutAnInvoice()
    {
        var draft = await CashDraft();
        var fakes = Arrange(draft);
        fakes.InvoiceApi.Create = (_, _, _) => new Billing.Invoicing.Data.Plsql.FullInvoiceResultRow { InvNo = null, Message = "No number." };

        await Assert.ThrowsAsync<InvalidOperationException>(() => fakes.CreateService().Create(CreateRequest(draft), Operator));

        var events = CreateCall(fakes).Arg<FakeOracleSession>().Events;
        Assert.Contains(RollbackEvent, events);
        Assert.DoesNotContain(CommitEvent, events);
    }

    [Fact]
    [Trait("Decision", "D-39")]
    public async Task Create_DraftIssuedBeforeMidnight_BindsTheIssuedDateAfterMidnight()
    {
        var issuedAt = new DateTime(2026, 9, 29, 23, 59, 59);
        var cash = await CashDraft();
        var draft = await Issued(cash with
        {
            DraftDate = issuedAt,
            Header = cash.Header with { InvDate = issuedAt, DraftDate = issuedAt },
        });
        var fakes = Arrange(draft);
        fakes.Lookups.DatabaseTime = issuedAt.AddMinutes(6);

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.DoesNotContain(response.Messages, IsBlocking);
        var header = CreateCall(fakes).Arg<InvoiceHeaderDraft>();
        Assert.Equal(issuedAt, header.DraftDate);
        Assert.Equal(issuedAt, header.InvDate);
        Assert.Equal(9001L, response.InvNo);
    }

    [Fact]
    [Trait("Decision", "D-39")]
    [Trait("Decision", "D-43")]
    public async Task Create_SameIdRetryAfterAnUncommittedCreate_SavesInAFreshProcessSharingTheKey()
    {
        var draft = await CreditDraft();
        var firstProcess = Arrange(draft);
        firstProcess.InvoiceApi.Create = (_, _, _) => throw new TimeoutException("create timed out");

        await Assert.ThrowsAsync<TimeoutException>(() => firstProcess.CreateService().Create(CreateRequest(draft), Operator));
        Assert.DoesNotContain(CommitEvent, CreateCall(firstProcess).Arg<FakeOracleSession>().Events);

        var restarted = Arrange(draft);
        var response = await restarted.CreateService().Create(CreateRequest(draft), Operator);

        Assert.DoesNotContain(response.Messages, IsBlocking);
        Assert.Equal(9001L, response.InvNo);
        Assert.Equal(draft.RequestId, CreateCall(restarted).Arg<string>());
        Assert.Equal(draft.DraftDate, CreateCall(restarted).Arg<InvoiceHeaderDraft>().InvDate);
    }

    [Fact]
    [Trait("Decision", "D-39")]
    public async Task Create_InAProcessConfiguredWithAnotherKey_ReturnsBlockingInvDateBeforeAnyRead()
    {
        var draft = await CreditDraft();
        var fakes = Arrange(draft);
        fakes.InvoiceApi.DraftSealKey = Convert.ToBase64String(new byte[BilInvoiceApiGateway.MinDraftSealKeyBytes]);

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        AssertDraftDateRejected(response, fakes);
    }

    [Theory]
    [Trait("Decision", "D-54")]
    [Trait("Decision", "D-39")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_RecordedRequestIdWithoutIssuedSeal_StillReplays(bool alterDate)
    {
        var issued = await CreditDraft();
        var draft = alterDate
            ? issued with { DraftDate = issued.DraftDate.AddDays(-1) }
            : issued with { DraftSeal = null };
        var fakes = Arrange(draft);
        fakes.Invoices.CreateRequest = RecordedFor(draft, Recorded(9001L, CreditCompany, null, false));
        fakes.InvoiceApi.CreateMessage = ReplayMessage;

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.Empty(response.Messages);
        Assert.Equal(9001L, response.InvNo);
        Assert.Equal(ReplayMessage, response.Message);
        Assert.Equal(1, CreateCalls(fakes));
        Assert.Empty(CreateCall(fakes).Arg<IReadOnlyList<InvoiceLineDraft>>());
    }

    [Fact]
    [Trait("Decision", "D-54")]
    [Trait("OpenItem", "OI-22")]
    public async Task Create_ReplayOfCashInvoiceAtAgeLimitedClinic_DerivesAdvisoriesWithoutReadsAfterCommit()
    {
        var draft = await CashDraft();
        var fakes = Arrange(draft);
        fakes.Invoices.CreateRequest = RecordedFor(draft, Recorded(9001L, CashCompany, null, true));
        fakes.InvoiceApi.CreateMessage = ReplayMessage;

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.Empty(response.Messages);
        Assert.Equal(9001L, response.InvNo);
        Assert.Equal(new[] { OpenItemIds.OI12, OpenItemIds.OI20, OpenItemIds.OI22, OpenItemIds.OI45 }, response.OpenItems);
        var commit = fakes.Journal.IndexOf(SessionCommitEntry);
        Assert.True(commit >= 0);
        Assert.Equal(new[] { SessionDisposeEntry }, fakes.Journal.Skip(commit + 1));
        var header = CreateCall(fakes).Arg<InvoiceHeaderDraft>();
        Assert.Equal(RecordedInvDate, header.DraftDate);
        Assert.Equal(RecordedInvDate, header.InvDate);
        Assert.Equal(PatientNo, header.PatientNo);
    }

    [Fact]
    [Trait("Decision", "D-54")]
    [Trait("OpenItem", "OI-21")]
    public async Task Create_ReplayOfInvoiceWithSubCompany_ReturnsOi21()
    {
        var draft = await CreditDraft();
        var fakes = Arrange(draft);
        fakes.Invoices.CreateRequest = RecordedFor(draft, Recorded(9001L, CreditCompany, SubCompany, false));

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.Empty(response.Messages);
        Assert.Equal(new[] { OpenItemIds.OI20, OpenItemIds.OI21 }, response.OpenItems);
        Assert.Equal(new[] { SessionDisposeEntry }, fakes.Journal.Skip(fakes.Journal.IndexOf(SessionCommitEntry) + 1));
    }

    [Fact]
    [Trait("Decision", "D-54")]
    public async Task Create_ReplayWhileInvoiceAndClinicReadsFail_StillReturnsTheReplayWithItsAdvisories()
    {
        var draft = await CashDraft();
        var fakes = Arrange(draft);
        fakes.Invoices.CreateRequest = RecordedFor(draft, Recorded(9001L, CashCompany, null, true));
        fakes.Invoices.Invoice = (_, _) => throw new TimeoutException("invoice read timed out");
        fakes.Lookups.ClinicProfile = (_, _) => throw new TimeoutException("clinic read timed out");
        fakes.InvoiceApi.CreateMessage = ReplayMessage;

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.Empty(response.Messages);
        Assert.Equal(9001L, response.InvNo);
        Assert.Equal(ReplayMessage, response.Message);
        Assert.Equal(new[] { OpenItemIds.OI12, OpenItemIds.OI20, OpenItemIds.OI22, OpenItemIds.OI45 }, response.OpenItems);
        Assert.True(AnySessionCommitted(fakes));
        Assert.False(Called(fakes.Invoices.Calls, nameof(IInvoiceQueries.GetInvoice)));
        Assert.False(Called(fakes.Lookups.Calls, nameof(ILookupQueries.GetClinicProfile)));
    }

    [Fact]
    [Trait("Decision", "D-54")]
    public async Task Create_ReplayOfRequestRecordedBeforeItsInvoice_RereadsTheRequestBeforeTheCommit()
    {
        var draft = await CashDraft();
        var fakes = Arrange(draft);
        var reads = 0;
        fakes.Invoices.CreateRequest = RecordedFor(
            draft,
            () => ++reads == 1 ? Recorded(null, null, null, false) : Recorded(9001L, CashCompany, null, true));

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.Equal(9001L, response.InvNo);
        Assert.Equal(new[] { OpenItemIds.OI12, OpenItemIds.OI20, OpenItemIds.OI22, OpenItemIds.OI45 }, response.OpenItems);
        Assert.Equal(
            new[]
            {
                CreateRequestEntry,
                $"{nameof(IOracleSessionFactory)}.{nameof(IOracleSessionFactory.Open)}",
                $"{nameof(IBilInvoiceApiGateway)}.{nameof(IBilInvoiceApiGateway.CreateFullInvoice)}",
                CreateRequestEntry,
                SessionCommitEntry,
                SessionDisposeEntry,
            },
            fakes.Journal);
        Assert.Equal(draft.DraftDate, CreateCall(fakes).Arg<InvoiceHeaderDraft>().DraftDate);
    }

    [Theory]
    [Trait("Decision", "D-54")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_ReplayOfAnotherInvoiceThanRecorded_RollsBackWithoutCommit(bool rereadFindsNothing)
    {
        var draft = await CashDraft();
        var fakes = Arrange(draft);
        var reads = 0;
        fakes.Invoices.CreateRequest = RecordedFor(
            draft,
            () => ++reads > 1 && rereadFindsNothing ? null : Recorded(8001L, CashCompany, null, false));

        await Assert.ThrowsAsync<InvalidOperationException>(() => fakes.CreateService().Create(CreateRequest(draft), Operator));

        Assert.Equal(2, reads);
        var events = CreateCall(fakes).Arg<FakeOracleSession>().Events;
        Assert.Contains(RollbackEvent, events);
        Assert.DoesNotContain(CommitEvent, events);
    }

    [Theory]
    [Trait("Decision", "D-68")]
    [Trait("Weakness", "CWE-400")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_LinesOverTheCap_ReturnsBlockingLineBeforeAnyLookup(bool withNullLine)
    {
        var cash = await CashDraft();
        var draft = cash with
        {
            Lines = new[]
            {
                Line(OrdinaryService, "c1"),
                Line(OrdinaryService, "c2"),
                Line(OrdinaryService, "c3"),
                withNullLine ? null! : Line(OrdinaryService, "c4"),
            },
        };
        var fakes = Arrange(draft);
        fakes.InvoiceApi.MaxDraftLines = 3;

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        AssertLinesRejected(response, fakes, "The draft expands to 4 invoice lines; an invoice can be created with at most 3 lines.");
    }

    [Fact]
    [Trait("Decision", "D-68")]
    [Trait("Weakness", "CWE-400")]
    public async Task Create_BundleParentsPushingOverTheCap_ReturnsBlockingLineBeforeAnyLookup()
    {
        var cash = await CashDraft();
        var draft = cash with
        {
            Lines = new[]
            {
                Line(OrdinaryService, "c1"),
                BundleLine(FirstComponent, "b1", " OFR-1"),
                BundleLine(SecondComponent, "b2", "OFR-1 "),
            },
        };
        var fakes = Arrange(draft);
        fakes.InvoiceApi.MaxDraftLines = 3;

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        AssertLinesRejected(response, fakes, "The draft expands to 4 invoice lines; an invoice can be created with at most 3 lines.");
    }

    [Fact]
    [Trait("Decision", "D-77")]
    [Trait("Weakness", "CWE-20")]
    public async Task Create_PatientNoOverTwelveBytes_RefusesPatientNoBeforeAnyLookup()
    {
        var draft = WithHeader(await CashDraft(), header => header with { PatientNo = "P123456789012" });
        var fakes = Arrange(draft);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => fakes.CreateService().Create(CreateRequest(draft), Operator));

        var message = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<MessageDto>>(error.Data[ProblemDetailsWriter.MessagesDataKey]));
        Assert.True(IsBlocking(message));
        Assert.Equal("PATIENTNO", message.Field);
        Assert.Equal("PATIENTNO has 13 characters; at most 12 can be bound.", message.Text);
        Assert.Empty(fakes.Lookups.Calls);
        Assert.Empty(fakes.SessionFactory.Calls);
        Assert.Equal(new[] { CreateRequestEntry }, fakes.Journal);
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Fact]
    [Trait("Decision", "D-68")]
    public async Task Create_LinesAtTheCap_ProceedsToTheSave()
    {
        var cash = await CashDraft();
        var draft = cash with { Lines = new[] { Line(OrdinaryService, "c1"), Line(OrdinaryService, "c2") } };
        var fakes = Arrange(draft);
        fakes.InvoiceApi.MaxDraftLines = 2;

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.DoesNotContain(response.Messages, IsBlocking);
        Assert.Equal(9001L, response.InvNo);
        Assert.Equal(2, CreateCall(fakes).Arg<IReadOnlyList<InvoiceLineDraft>>().Count);
    }

    [Fact]
    [Trait("Decision", "D-45")]
    [Trait("OpenItem", "OI-20")]
    public async Task Create_TotalValidationOpenItem_CommitsAfterTheCheck()
    {
        var draft = await CreditDraft();
        var fakes = Arrange(draft);

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.Contains(CommitEvent, CreateCall(fakes).Arg<FakeOracleSession>().Events);
        var check = fakes.Journal.IndexOf($"{nameof(ILegacyExternalCalls)}.{nameof(ILegacyExternalCalls.ValidateTotalInvoice)}");
        var commit = fakes.Journal.IndexOf($"{nameof(IOracleSession)}.{CommitEvent}");
        Assert.True(check >= 0);
        Assert.True(check < commit);
        Assert.Contains(OpenItemIds.OI20, response.OpenItems);
    }

    [Fact]
    [Trait("Decision", "D-45")]
    [Trait("OpenItem", "OI-20")]
    public async Task Create_TotalValidationFailure_RollsBackWithoutCommit()
    {
        var draft = await CreditDraft();
        var fakes = Arrange(draft);
        fakes.Legacy.ValidateTotalInvoiceException = () => new InvalidOperationException("total check failed");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fakes.CreateService().Create(CreateRequest(draft), Operator));

        var events = CreateCall(fakes).Arg<FakeOracleSession>().Events;
        Assert.Contains(RollbackEvent, events);
        Assert.DoesNotContain(CommitEvent, events);
        Assert.True(Called(fakes.Legacy.Calls, nameof(ILegacyExternalCalls.ValidateTotalInvoice)));
    }

    [Theory]
    [Trait("Decision", "D-12")]
    [InlineData("SERVICEID")]
    [InlineData("PAT_SERV_REQ_ROW_ID")]
    public async Task ImportRequests_SelectedRowWithNullColumn_ReturnsBlockingFormMessageBeforeImport(string column)
    {
        var draft = await RequestImportDraft();
        var fakes = Arrange(draft);
        fakes.Invoices.SelectedRequestRows = (_, _, _) =>
            throw new InvalidCastException($"Column {column} of V_SERVICES_REQ holds null.");

        var response = await fakes.CreateService().ImportRequests(new ImportRequestsRequest { Draft = draft }, Operator);

        var message = Assert.Single(response.Messages);
        Assert.True(IsBlocking(message));
        Assert.Null(message.Field);
        Assert.Null(message.Rule);
        Assert.StartsWith(RequestImportFailedPrefix, message.Text, StringComparison.Ordinal);
        Assert.Contains(column, message.Text, StringComparison.Ordinal);
        Assert.Empty(response.Lines);
        Assert.Null(response.Result);
        Assert.True(Called(fakes.Invoices.Calls, nameof(IInvoiceQueries.GetSelectedRequestRows)));
        Assert.False(Called(fakes.Import.Calls, nameof(IBilImportGateway.ImportRequestLines)));
        Assert.False(Called(fakes.Import.Calls, nameof(IBilImportGateway.ApprovalCheckMode)));
        Assert.Empty(fakes.SessionFactory.Sessions);
    }

    [Fact]
    [Trait("Decision", "D-12")]
    public async Task ImportRequests_SelectedRowWithOutOfRangeNumber_ReturnsBlockingFormMessageBeforeImport()
    {
        var draft = await RequestImportDraft();
        var fakes = Arrange(draft);
        fakes.Invoices.SelectedRequestRows = (_, _, _) =>
            throw new OverflowException("Value was either too large or too small for an Int32.");

        var response = await fakes.CreateService().ImportRequests(new ImportRequestsRequest { Draft = draft }, Operator);

        var message = Assert.Single(response.Messages);
        Assert.True(IsBlocking(message));
        Assert.Null(message.Field);
        Assert.StartsWith(RequestImportFailedPrefix, message.Text, StringComparison.Ordinal);
        Assert.False(Called(fakes.Import.Calls, nameof(IBilImportGateway.ImportRequestLines)));
        Assert.Empty(fakes.SessionFactory.Sessions);
    }

    [Fact]
    [Trait("Decision", "D-12")]
    public async Task ImportRequests_CompleteSelectedRow_ImportsThatRow()
    {
        var draft = await RequestImportDraft();
        var fakes = Arrange(draft);
        fakes.Invoices.SelectedRequestRows = (_, _, _) =>
            new[] { (SelectedRequestRowId, OrdinaryService, (int?)null, (int?)0, (string?)null) };

        var response = await fakes.CreateService().ImportRequests(new ImportRequestsRequest { Draft = draft }, Operator);

        Assert.DoesNotContain(response.Messages, IsBlocking);
        var importCall = Assert.Single(
            fakes.Import.Calls, call => call.Method == nameof(IBilImportGateway.ImportRequestLines));
        Assert.Equal(new[] { SelectedRequestRowId }, importCall.Arg<IReadOnlyList<long>>());
        Assert.Equal(VisitUnique, importCall.Arg<string>());
        Assert.Single(fakes.SessionFactory.Sessions);
    }

    private static async Task<DraftDto> RequestImportDraft() => (await CashDraft()) with
    {
        Parameters = new InvoiceEntryParameters { VisitUnique = VisitUnique },
    };

    [Fact]
    [Trait("OpenItem", "OI-42")]
    public async Task GetLov_ReservNoWithEveryBind_ListsOi42AlongsideViewOnlyRows()
    {
        var fakes = new FakeDataPorts();
        var rows = ReservationRows();
        fakes.Lovs.Rows = rows;

        var response = await fakes.CreateService().GetLov(ReservationList, LovBinds(), DraftDate, Operator);

        Assert.NotNull(response);
        Assert.Equal(new[] { OpenItemIds.OI42 }, response.OpenItems);
        Assert.True(response.ViewOnly);
        Assert.Empty(response.Messages);
        Assert.Same(rows, response.Rows);
        var call = Assert.Single(fakes.Lovs.Calls);
        Assert.Equal(nameof(ILovQueries.ReservNo), call.Method);
        Assert.Equal(DraftDate, call.Arg<DateTime>());
        Assert.Equal(DoctorId, call.Arg<int>());
        Assert.Equal(PatientNo, call.Arg<string>());
    }

    [Theory]
    [Trait("OpenItem", "OI-42")]
    [InlineData("COMPANY1_2")]
    [InlineData("SUB_COMPANY")]
    [InlineData("THE_CLASS")]
    [InlineData("PAY_TYPE1")]
    [InlineData("PAY_TYPE2")]
    [InlineData("DOC")]
    [InlineData("OFFERS")]
    [InlineData("CAT")]
    public async Task GetLov_OtherServedList_ListsNoOpenItem(string name)
    {
        var fakes = new FakeDataPorts();

        var response = await fakes.CreateService().GetLov(name, LovBinds(), DraftDate, Operator);

        Assert.NotNull(response);
        Assert.Empty(response.OpenItems);
        Assert.False(response.ViewOnly);
        Assert.Empty(response.Messages);
        Assert.Single(fakes.Lovs.Calls);
    }

    [Theory]
    [Trait("OpenItem", "OI-42")]
    [InlineData(DocIdItem)]
    [InlineData(PatientNoItem)]
    [InlineData(InvDateItem)]
    public async Task GetLov_ReservNoMissingBind_ReturnsBlockingMessageWithoutOi42(string missingItem)
    {
        var fakes = new FakeDataPorts();
        var binds = LovBinds();
        binds.Remove(missingItem);
        DateTime? draftDate = string.Equals(missingItem, InvDateItem, StringComparison.Ordinal) ? null : DraftDate;

        var response = await fakes.CreateService().GetLov(ReservationList, binds, draftDate, Operator);

        Assert.NotNull(response);
        var message = Assert.Single(response.Messages);
        Assert.True(IsBlocking(message));
        Assert.Equal(missingItem, message.Field);
        Assert.Empty(response.OpenItems);
        Assert.Empty(response.Rows);
        Assert.Empty(fakes.Lovs.Calls);
    }

    private static Dictionary<string, string?> LovBinds() => new(StringComparer.Ordinal)
    {
        [CompCodeItem] = CreditCompany,
        [SubCompCodeItem] = SubCompany,
        [DocIdItem] = DoctorId.ToString(CultureInfo.InvariantCulture),
        [PatientNoItem] = PatientNo,
        [PayTypeItem] = CashPayType,
    };

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ReservationRows() =>
        new IReadOnlyDictionary<string, object?>[]
        {
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["RESERV_NO"] = 7,
                ["THE_TIME"] = "10:30",
                ["PATAINTNO"] = PatientNo,
            },
        };

    [Fact]
    [Trait("Decision", "D-42")]
    public async Task Preview_CashDraft_ListsOnlyTheLineWhosePriceIsNotFixed()
    {
        var draft = (await CashDraft()) with { Lines = new[] { Line(OrdinaryService, "c1"), Line(FixedPriceService, "c2") } };
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = serviceId => Profile(serviceId) with
        {
            PriceIsFixed = string.Equals(serviceId, OrdinaryService, StringComparison.Ordinal) ? PriceNotFixed : PriceFixed,
        };

        var response = await fakes.CreateService().Preview(draft, Operator);

        Assert.Equal(new[] { "c1" }, response.PriceEditableClientIds);
    }

    [Fact]
    [Trait("Decision", "D-42")]
    public async Task Preview_CashDraftWithUnfixedPrices_ExcludesRequestComponentAndOfferLines()
    {
        var draft = (await CashDraft()) with
        {
            Lines = new[]
            {
                Line(OrdinaryService, "c1"),
                Line(RequestService, "r1") with { PatServReqRowId = 7L },
                PackageLine(FirstComponent, ComponentRole, "k1", 1),
                Line(OfferService, "o1") with { OfferId = 3 },
                Line(OfferComponentService, "o2") with { OfferLineRole = ComponentRole },
            },
        };
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = serviceId => Profile(serviceId) with { PriceIsFixed = PriceNotFixed };

        var response = await fakes.CreateService().Preview(draft, Operator);

        Assert.Equal(new[] { "c1" }, response.PriceEditableClientIds);
    }

    [Fact]
    [Trait("Decision", "D-42")]
    public async Task Preview_CreditDraftWithNonDirectCompany_ListsNoLine()
    {
        var draft = (await CreditDraft()) with { Lines = new[] { Line(OrdinaryService, "c1"), Line(FixedPriceService, "c2") } };
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = serviceId => Profile(serviceId) with { PriceIsFixed = PriceNotFixed };
        fakes.Lookups.CompanyIsDirect = 0;

        var response = await fakes.CreateService().Preview(draft, Operator);

        Assert.Empty(response.PriceEditableClientIds);
    }

    [Fact]
    [Trait("Decision", "D-42")]
    public async Task Preview_CreditDraftWithDirectCompany_ListsTheOrdinaryLines()
    {
        var draft = (await CreditDraft()) with
        {
            Lines = new[]
            {
                Line(OrdinaryService, "c1"),
                Line(FixedPriceService, "c2"),
                Line(OfferService, "o1") with { OfferId = 3 },
            },
        };
        var fakes = Arrange(draft);
        fakes.Lookups.CompanyIsDirect = 1;

        var response = await fakes.CreateService().Preview(draft, Operator);

        Assert.Equal(new[] { "c1", "c2" }, response.PriceEditableClientIds);
    }

    [Fact]
    [Trait("Decision", "D-42")]
    public async Task Preview_RefusedManualOverride_IsNotListed()
    {
        var draft = (await CashDraft()) with
        {
            Lines = new[]
            {
                Line(FixedPriceService, "c1") with { PriceOverride = 50m, UsePriceOverride = "Y" },
                Line(OrdinaryService, "c2") with { PriceOverride = 60m, UsePriceOverride = "Y" },
            },
        };
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = serviceId => Profile(serviceId) with
        {
            PriceIsFixed = string.Equals(serviceId, OrdinaryService, StringComparison.Ordinal) ? PriceNotFixed : PriceFixed,
        };

        var response = await fakes.CreateService().Preview(draft, Operator);

        Assert.Equal(new[] { "c2" }, response.PriceEditableClientIds);
        Assert.Contains(response.Messages, message => IsBlocking(message) && message.Field == PriceItem);
    }

    [Fact]
    [Trait("Decision", "D-42")]
    public async Task Validate_ServiceIdOnCashLineWithUnfixedPrice_ReturnsPriceEditableTrue()
    {
        var draft = await CashDraft();
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = serviceId => Profile(serviceId) with { PriceIsFixed = PriceNotFixed };

        var response = await fakes.CreateService().Validate(
            new ValidateDraftRequest { Draft = draft, Target = "SERVICEID", LineIndex = 0 }, Operator);

        Assert.True(response.PriceEditable);
    }

    [Fact]
    [Trait("Decision", "D-42")]
    public async Task Validate_ServiceIdOnCashLineWithFixedPrice_ReturnsPriceEditableFalse()
    {
        var draft = await CashDraft();
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = serviceId => Profile(serviceId) with { PriceIsFixed = PriceFixed };

        var response = await fakes.CreateService().Validate(
            new ValidateDraftRequest { Draft = draft, Target = "SERVICEID", LineIndex = 0 }, Operator);

        Assert.False(response.PriceEditable);
    }

    [Fact]
    [Trait("Decision", "D-42")]
    public async Task Validate_PatientNo_ReturnsNoPriceEditability()
    {
        var draft = await CashDraft();
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = serviceId => Profile(serviceId) with { PriceIsFixed = PriceNotFixed };

        var response = await fakes.CreateService().Validate(
            new ValidateDraftRequest { Draft = draft, Target = "PATIENTNO" }, Operator);

        Assert.Null(response.PriceEditable);
        Assert.Null(response.PriceJudgedServiceId);
        Assert.Null(response.PriceJudgedPatientNo);
        Assert.Null(response.PriceJudgedCompCode);
    }

    [Fact]
    [Trait("Decision", "D-42")]
    public async Task Preview_EchoesThePatientAndCompanyThePriceEditabilityWasJudgedOn()
    {
        var draft = await CreditDraft();
        var fakes = Arrange(draft);
        fakes.Lookups.CompanyIsDirect = 1;

        var response = await fakes.CreateService().Preview(draft, Operator);

        Assert.Equal(new[] { "c1" }, response.PriceEditableClientIds);
        Assert.Equal(PatientNo, response.PriceJudgedPatientNo);
        Assert.Equal(CreditCompany, response.PriceJudgedCompCode);
    }

    [Fact]
    [Trait("Decision", "D-42")]
    public async Task Validate_LineTarget_EchoesTheServicePatientAndCompanyThePriceEditabilityWasJudgedOn()
    {
        var draft = await CreditDraft();
        var fakes = Arrange(draft);
        fakes.Lookups.CompanyIsDirect = 1;

        var response = await fakes.CreateService().Validate(
            new ValidateDraftRequest { Draft = draft, Target = "QTY", LineIndex = 0 }, Operator);

        Assert.True(response.PriceEditable);
        Assert.Equal(OrdinaryService, response.PriceJudgedServiceId);
        Assert.Equal(PatientNo, response.PriceJudgedPatientNo);
        Assert.Equal(CreditCompany, response.PriceJudgedCompCode);
    }

    [Fact]
    [Trait("Rule", "DR-03")]
    [Trait("Decision", "D-72")]
    public async Task ValidatePatient_NoCoverageRow_BlocksDr03BeforePayTypeSelection()
    {
        var draft = (await CreditDraft()) with { Parameters = new InvoiceEntryParameters { ClaimNo = ClaimNumber } };
        var fakes = Arrange(draft);
        fakes.Lookups.PatientCoverage = null;
        fakes.Invoices.ClaimPreload = SubCompanyClaimPreload;

        var response = await fakes.CreateService().Validate(
            new ValidateDraftRequest { Draft = draft, Target = "PATIENTNO" }, Operator);

        AssertOnlyMissingCoverageRow(response.Messages);
        Assert.False(response.Adjusted.ContainsKey(PayTypeItem));
        Assert.Empty(response.OpenItems);
        Assert.False(Called(fakes.Lookups.Calls, nameof(ILookupQueries.GetCompanyType)));
    }

    [Fact]
    [Trait("Rule", "DR-03")]
    [Trait("Decision", "D-72")]
    public async Task ValidatePatient_BlankPatient_AdjustsPayTypeWithoutDr03()
    {
        var draft = WithHeader(await CreditDraft(), header => header with { PatientNo = "   " });
        var fakes = Arrange(draft);
        fakes.Lookups.PatientCoverage = null;

        var response = await fakes.CreateService().Validate(
            new ValidateDraftRequest { Draft = draft, Target = "PATIENTNO" }, Operator);

        Assert.DoesNotContain(response.Messages, message => message.Rule == "DR-03");
        Assert.Equal(2, response.Adjusted[PayTypeItem]);
        Assert.False(Called(fakes.Lookups.Calls, nameof(ILookupQueries.GetPatientCoverage)));
    }

    [Fact]
    [Trait("Rule", "DR-03")]
    [Trait("Decision", "D-72")]
    public async Task GetCoverage_NoCoverageRow_BlocksDr03WithoutFurtherReads()
    {
        var parameters = new InvoiceEntryParameters { ClaimNo = ClaimNumber };
        var fakes = Arrange(await CreditDraft());
        fakes.Lookups.PatientCoverage = null;
        fakes.Invoices.ClaimPreload = SubCompanyClaimPreload;

        var response = await fakes.CreateService().GetCoverage(PatientNo, DraftDate, parameters, Operator);

        AssertOnlyMissingCoverageRow(response.Messages);
        Assert.Null(response.Coverage);
        Assert.Equal(0, response.PayType);
        Assert.Empty(response.OpenItems);
        Assert.Equal(new[] { nameof(ILookupQueries.GetPatientCoverage) }, fakes.Lookups.Calls.Select(call => call.Method));
        Assert.Empty(fakes.Invoices.Calls);
    }

    [Theory]
    [Trait("Rule", "DR-03")]
    [Trait("Decision", "D-72")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_NoCoverageRow_BlocksDr03BeforePayTypeSelection(bool credit)
    {
        var draft = credit ? await CreditDraft() : await CashDraft();
        var fakes = Arrange(draft);
        fakes.Lookups.PatientCoverage = null;

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        AssertOnlyMissingCoverageRow(response.Messages);
        Assert.Empty(response.OpenItems);
        Assert.Null(response.InvNo);
        Assert.Equal(
            new[] { nameof(ILookupQueries.GetPatientCoverage) },
            fakes.Lookups.Calls.Select(call => call.Method));
        Assert.Equal(0, CreateCalls(fakes));
        Assert.False(Called(fakes.InvoiceApi.Calls, nameof(IBilInvoiceApiGateway.CalculatePreview)));
        Assert.Empty(fakes.SessionFactory.Calls);
    }

    [Theory]
    [Trait("Rule", "DR-03")]
    [Trait("Decision", "D-72")]
    [InlineData(null)]
    [InlineData("")]
    public async Task Create_BlankPatient_BlocksDr01WithoutDr03(string? patientNo)
    {
        var draft = WithHeader(await CashDraft(), header => header with { PatientNo = patientNo });
        var fakes = Arrange(draft);
        fakes.Lookups.PatientCoverage = null;

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.True(HasBlocking(response.Messages, "DR-01"));
        Assert.DoesNotContain(response.Messages, message => message.Rule == "DR-03");
        Assert.Null(response.InvNo);
        Assert.Equal(0, CreateCalls(fakes));
    }

    [Theory]
    [Trait("Rule", "DR-14")]
    [Trait("Decision", "D-73")]
    [InlineData(false, null, 2, false)]
    [InlineData(false, null, 1, true)]
    [InlineData(true, "1", 2, true)]
    [InlineData(true, " 1 ", 2, true)]
    [InlineData(true, "2", 1, false)]
    [InlineData(true, "x", 2, false)]
    [InlineData(true, "x", 1, true)]
    [InlineData(true, null, 1, false)]
    public async Task ValidateLineAndCreate_Pref422_DecidesApprovalCheck(
        bool prefPresent, string? prefValue, int? x422ApprovCheck, bool blocks)
    {
        var draft = (await CreditDraft()) with { Parameters = new InvoiceEntryParameters { X422ApprovCheck = x422ApprovCheck } };
        var fakes = ArrangeApproval(draft, prefPresent, prefValue);
        var service = fakes.CreateService();

        var validated = await service.Validate(
            new ValidateDraftRequest { Draft = draft, Target = "SERVICEID", LineIndex = 0 }, Operator);
        var created = await service.Create(CreateRequest(draft), Operator);

        Assert.Equal(blocks, HasBlocking(validated.Messages, "DR-14"));
        Assert.Equal(blocks, HasBlocking(created.Messages, "DR-14"));
        Assert.Equal(blocks ? 0 : 1, CreateCalls(fakes));
        Assert.Equal(blocks, created.InvNo is null);
    }

    [Theory]
    [Trait("Rule", "DR-18")]
    [Trait("Decision", "D-73")]
    [InlineData(false, null, 2, 2, 0)]
    [InlineData(false, null, 1, 1, 1)]
    [InlineData(true, "1", 2, 1, 1)]
    [InlineData(true, "x", 2, 2, 0)]
    [InlineData(true, null, 2, null, 1)]
    public async Task ImportRequests_Pref422_DecidesApprovalMode(
        bool prefPresent, string? prefValue, int? x422ApprovCheck, int? expectedX422, int expectedMode)
    {
        var draft = (await CreditDraft()) with
        {
            Parameters = new InvoiceEntryParameters { X422ApprovCheck = x422ApprovCheck, VisitUnique = "V1" },
        };
        var fakes = ArrangeApproval(draft, prefPresent, prefValue);
        fakes.Invoices.SelectedRequestRows = (_, _, _) => new[] { (7L, OrdinaryService, (int?)1, (int?)1, (string?)null) };

        var response = await fakes.CreateService().ImportRequests(new ImportRequestsRequest { Draft = draft }, Operator);

        var mode = Assert.Single(fakes.Import.Calls, call => call.Method == nameof(IBilImportGateway.ApprovalCheckMode));
        Assert.Equal(expectedX422, (int?)mode.Args[0]);
        var import = Assert.Single(fakes.Import.Calls, call => call.Method == nameof(IBilImportGateway.ImportRequestLines));
        Assert.Equal(expectedMode, import.Arg<int>());
        Assert.Equal(
            expectedX422 == 1,
            response.Messages.Any(message => message.Text == OrdinaryService + " Need Approval"));
    }

    private static FakeDataPorts ArrangeApproval(DraftDto draft, bool prefPresent, string? prefValue)
    {
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = serviceId => Profile(serviceId) with { ReqNeedA = 1 };
        fakes.Lookups.Preferences = prefPresent
            ? new Dictionary<int, string?> { [ApprovalPreference] = prefValue }
            : new Dictionary<int, string?>();
        return fakes;
    }

    private static (InvoiceHeaderDraft Header, decimal? ListId, decimal? MaxDeductable, int? CardId)? SubCompanyClaimPreload(string claimNo) =>
        string.Equals(claimNo, ClaimNumber, StringComparison.Ordinal)
            ? (new InvoiceHeaderDraft
            {
                PatientNo = PatientNo,
                CompCode = CreditCompany,
                PayType = 2,
                SubCompCode = SubCompany,
                ClassCode = ClassCode,
            }, 55m, null, null)
            : null;

    private static void AssertOnlyMissingCoverageRow(IReadOnlyList<MessageDto> messages)
    {
        var message = Assert.Single(messages);
        Assert.Equal(PatientNoItem, message.Field);
        Assert.Equal(CoverageRowMissingText, message.Text);
        Assert.Equal(ValidationMessage.Blocking, message.Severity);
        Assert.Equal("DR-03", message.Rule);
    }

    [Fact]
    [Trait("Decision", "D-55")]
    public async Task Create_PlainLinesOnOneList_ReadsTheirProfilesInOneBatchedLookup()
    {
        var draft = await ThreeLineDraft();
        var fakes = Arrange(draft);

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.DoesNotContain(response.Messages, IsBlocking);
        Assert.Equal(1, CreateCalls(fakes));
        Assert.False(Called(fakes.Lookups.Calls, nameof(ILookupQueries.GetServiceProfile)));
        var read = Assert.Single(ProfileReadCalls(fakes));
        Assert.Equal(DefaultListId, read.Arg<decimal>());
        Assert.Equal(
            new[] { OrdinaryService, SecondService, ThirdService },
            read.Arg<IReadOnlyCollection<string>>().Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Decision", "D-42")]
    public async Task Preview_ManualPriceLine_ReadsEachServiceProfileOnce()
    {
        var draft = (await CashDraft()) with
        {
            Lines = new[] { Line(OrdinaryService, "c1") with { PriceOverride = OverriddenPrice }, Line(SecondService, "c2") },
        };
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = serviceId => Profile(serviceId) with { PriceIsFixed = PriceNotFixed };

        var response = await fakes.CreateService().Preview(draft, Operator);

        Assert.DoesNotContain(response.Messages, IsBlocking);
        Assert.Contains(PreviewedLines(fakes), lines => lines.Any(line => line.PriceOverride == OverriddenPrice));
        Assert.False(Called(fakes.Lookups.Calls, nameof(ILookupQueries.GetServiceProfile)));
        var reads = ProfileReadKeys(fakes);
        Assert.Equal(reads.Distinct().ToArray(), reads);
        Assert.Equal(new[] { OrdinaryService, SecondService }, ServicesReadOn(reads, DefaultListId));
    }

    [Fact]
    [Trait("Decision", "D-38")]
    public async Task Create_LinesOnDifferentPreviewLists_ReadsEachProfileOnItsOwnList()
    {
        var draft = await ThreeLineDraft();
        var fakes = Arrange(draft);
        fakes.InvoiceApi.Preview = (_, lines) => (
            lines.Select((line, index) => new EditablePreviewLine
            {
                ClientId = line.ClientId,
                LineNo = index + 1,
                ServiceId = line.ServiceId,
                Qty = line.Qty,
                ListId = line.ClientId switch { "c1" => DefaultListId, "c2" => SecondListId, _ => null },
            }).ToArray(),
            new PreviewTotalsRow { LineCount = lines.Count, PatPay = 0m, CashCollected = 0m, Amount1 = 0m, Amount2 = 0m });

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.DoesNotContain(response.Messages, IsBlocking);
        var reads = ProfileReadKeys(fakes);
        Assert.Equal(2, ProfileReadCalls(fakes).Length);
        Assert.Equal(new[] { OrdinaryService, ThirdService }, ServicesReadOn(reads, DefaultListId));
        Assert.Equal(new[] { SecondService }, ServicesReadOn(reads, SecondListId));
    }

    [Theory]
    [Trait("Rule", "DR-16")]
    [InlineData(PaddedLowerCaseOrdinaryService, false)]
    [InlineData(UnrequestedService, true)]
    public async Task Create_RequestedServiceMatchedTrimmedIgnoringCase_DecidesDr16Warning(string requestedServiceId, bool warned)
    {
        var draft = await CreditDraft();
        var fakes = Arrange(draft);
        fakes.Lookups.PatientCoverage = InsuredCoverage(draft);
        fakes.Lookups.ServiceProfile = serviceId => Profile(serviceId) with { BeginOfClaim = 0 };
        fakes.Lookups.RequestedServices = new[] { requestedServiceId };

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.DoesNotContain(response.Messages, IsBlocking);
        Assert.True(Called(fakes.Lookups.Calls, nameof(ILookupQueries.GetRequestedServices)));
        Assert.Equal(warned, HasWarning(response.Messages, "DR-16"));
    }

    [Theory]
    [Trait("Rule", "DR-23")]
    [Trait("Decision", "D-37")]
    [InlineData(SecondComponent, 0)]
    [InlineData(FirstComponent, 1)]
    public async Task Create_InterleavedPackageInstances_NestEachComponentUnderItsOwnParent(string queuedComponent, int addToList)
    {
        var draft = await InterleavedPackagesDraft();
        var fakes = Arrange(draft);
        fakes.Lookups.ServiceProfile = serviceId => serviceId switch
        {
            PackageService => Profile(serviceId) with { ServLocId = PackageServiceLocation, IsPackage = 1 },
            SecondPackageService => Profile(serviceId) with { IsPackage = 1 },
            _ => Profile(serviceId),
        };
        fakes.Lookups.ServiceQueueFlags = serviceIds => serviceIds.ToDictionary(
            serviceId => serviceId,
            serviceId => string.Equals(serviceId, queuedComponent, StringComparison.Ordinal) ? 1 : 0,
            StringComparer.Ordinal);

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.DoesNotContain(response.Messages, IsBlocking);
        Assert.Equal((int?)addToList, CreateCall(fakes).Arg<InvoiceHeaderDraft>().AddToList);
    }

    private static async Task<DraftDto> ThreeLineDraft() => (await CashDraft()) with
    {
        Lines = new[] { Line(OrdinaryService, "c1"), Line(SecondService, "c2"), Line(ThirdService, "c3") },
    };

    private static async Task<DraftDto> InterleavedPackagesDraft() => (await CashDraft()) with
    {
        Lines = new[]
        {
            PackageLine(PackageService, ParentRole, "p1", null) with { PackageInstanceId = FirstPackageInstance },
            PackageLine(SecondPackageService, ParentRole, "p2", null) with
            {
                PackageServiceId = SecondPackageService,
                PackageInstanceId = SecondPackageInstance,
            },
            PackageLine(SecondComponent, ComponentRole, "k2", 1) with
            {
                PackageServiceId = SecondPackageService,
                PackageInstanceId = SecondPackageInstance,
            },
            PackageLine(FirstComponent, ComponentRole, "k1", 1) with { PackageInstanceId = FirstPackageInstance },
        },
    };

    [Fact]
    [Trait("Decision", "D-52")]
    [Trait("OpenItem", "OI-21")]
    public async Task ValidatePatient_InsuredCreditDraft_EmbedsCoverageFromOneRead()
    {
        var draft = await CreditDraft();
        var fakes = Arrange(draft);
        fakes.Lookups.PatientCoverage = InsuredCoverage(draft);

        var response = await fakes.CreateService().Validate(PatientValidation(draft), Operator);

        var coverage = Assert.IsType<CoverageResponse>(response.Coverage);
        Assert.Equal(fakes.Lookups.PatientCoverage, coverage.Coverage);
        Assert.Equal(2, coverage.PayType);
        Assert.Contains(OpenItemIds.OI24, coverage.OpenItems);
        Assert.Contains(OpenItemIds.OI21, coverage.OpenItems);
        Assert.Contains(OpenItemIds.OI24, response.OpenItems);
        Assert.Contains(OpenItemIds.OI21, response.OpenItems);
        Assert.Equal(1, CoverageReads(fakes));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidatePatient_BlankPatientNo_ReturnsNoCoverageWithoutCoverageRead(string? patientNo)
    {
        var draft = WithHeader(await CreditDraft(), header => header with { PatientNo = patientNo });
        var fakes = Arrange(draft);

        var response = await fakes.CreateService().Validate(PatientValidation(draft), Operator);

        Assert.Null(response.Coverage);
        Assert.Empty(response.OpenItems);
        Assert.Equal(0, CoverageReads(fakes));
    }

    [Theory]
    [InlineData("cash")]
    [InlineData("insured-credit")]
    [InlineData("referral-credit")]
    [InlineData("on-hold-credit")]
    public async Task ValidatePatient_EmbeddedCoverage_EqualsGetCoverage(string coverageCase)
    {
        var draft = coverageCase == "cash" ? await CashDraft() : await CreditDraft();
        var fakes = Arrange(draft);
        fakes.Lookups.PatientCoverage = coverageCase switch
        {
            "insured-credit" => InsuredCoverage(draft),
            "referral-credit" => InsuredCoverage(draft) with { CompanyType = 2, ClassWithRef = 1 },
            "on-hold-credit" => InsuredCoverage(draft) with { CompanyIsActive = OnHold },
            _ => ValidCoverage(draft),
        };
        var service = fakes.CreateService();

        var validated = await service.Validate(PatientValidation(draft), Operator);
        var expected = await service.GetCoverage(PatientNo, draft.DraftDate, draft.Parameters, Operator);

        var embedded = Assert.IsType<CoverageResponse>(validated.Coverage);
        Assert.Equal(expected.Coverage, embedded.Coverage);
        Assert.Equal(expected.PayType, embedded.PayType);
        Assert.Equal(expected.OpenItems, embedded.OpenItems);
        Assert.Equal(expected.Messages, embedded.Messages);
        Assert.Equal(coverageCase is "referral-credit" or "on-hold-credit", expected.Messages.Count > 0);
        Assert.All(expected.OpenItems, openItem => Assert.Contains(openItem, validated.OpenItems));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetCoverage_ReturnsSnapshotPayTypeMessagesAndOrderedOpenItems(bool credit)
    {
        var draft = credit ? await CreditDraft() : await CashDraft();
        var fakes = Arrange(draft);
        fakes.Lookups.PatientCoverage = credit
            ? InsuredCoverage(draft) with { MaxDeductable = 50m, CompanyIsActive = OnHold }
            : ValidCoverage(draft);

        var response = await fakes.CreateService().GetCoverage(PatientNo, draft.DraftDate, draft.Parameters, Operator);

        Assert.Equal(fakes.Lookups.PatientCoverage, response.Coverage);
        Assert.Equal(credit ? 2 : 1, response.PayType);
        Assert.Equal(
            credit ? new[] { OpenItemIds.OI21, OpenItemIds.OI23, OpenItemIds.OI24 } : new[] { OpenItemIds.OI24 },
            response.OpenItems);
        Assert.Equal(credit ? new[] { "Company Is Holed" } : Array.Empty<string>(), response.Messages.Select(message => message.Text));
        Assert.Equal(credit, HasBlocking(response.Messages, "DR-03"));
        Assert.Equal(1, CoverageReads(fakes));
    }

    [Fact]
    [Trait("Decision", "D-51")]
    [Trait("OpenItem", "OI-23")]
    public async Task ValidatePatient_CreditWithDeductible_ListsOi23()
    {
        var draft = await CreditDraft();
        var fakes = Arrange(draft);
        fakes.Lookups.PatientCoverage = ValidCoverage(draft) with { MaxDeductable = 50m };

        var response = await fakes.CreateService().Validate(PatientValidation(draft), Operator);

        Assert.Contains(OpenItemIds.OI23, response.OpenItems);
        Assert.Contains(OpenItemIds.OI23, Assert.IsType<CoverageResponse>(response.Coverage).OpenItems);
        Assert.Equal(1, CoverageReads(fakes));
    }

    private static async Task<DraftDto> ExpandedPackageDraft() => (await CashDraft()) with
    {
        Lines = new[]
        {
            PackageLine(PackageService, ParentRole, "p1", null),
            PackageLine(FirstComponent, ComponentRole, "k1", 1),
            PackageLine(SecondComponent, ComponentRole, "k2", 2),
        },
    };

    private static PackageImportRequest PackageRequest(DraftDto draft) => new()
    {
        Draft = draft,
        PackageServiceId = PackageService,
        ParentSourceId = null,
    };

    private static bool IsPackageParent(InvoiceLineDraft line) =>
        string.Equals(line.ServiceId, PackageService, StringComparison.Ordinal);

    private static Exception? RefuseUnexpandedParent(InvoiceHeaderDraft header, IReadOnlyList<InvoiceLineDraft> lines) =>
        lines.Any(IsPackageParent) ? FakeOracleFailures.UnexpandedParent() : null;

    private static (InvoiceHeaderDraft Header, decimal? ListId, decimal? MaxDeductable, int? CardId)? PreloadWithList(string claimNo) =>
        string.Equals(claimNo, ClaimNumber, StringComparison.Ordinal)
            ? (new InvoiceHeaderDraft { PatientNo = PatientNo, CompCode = CashCompany, PayType = 1 }, 55m, null, null)
            : null;

    private static async Task<DraftDto> PlainLinesDraft(params string[] serviceIds) => (await CashDraft()) with
    {
        Lines = serviceIds.Select((serviceId, index) => Line(serviceId, $"c{index + 1}")).ToArray(),
    };

    private static ValidateDraftRequest LineTarget(DraftDto draft, int lineIndex) =>
        new() { Draft = draft, Target = "LINE", LineIndex = lineIndex };

    private static string[] QueueFlagIds(FakeDataPorts fakes) =>
        fakes.Lookups.Calls
            .Where(call => call.Method == nameof(ILookupQueries.GetServiceQueueFlags))
            .SelectMany(call => call.Arg<IReadOnlyCollection<string>>())
            .ToArray();

    private static FakeCall[] ProfileReadCalls(FakeDataPorts fakes) =>
        fakes.Lookups.Calls
            .Where(call => call.Method == nameof(ILookupQueries.GetServiceProfiles))
            .ToArray();

    private static (decimal ListId, string ServiceId)[] ProfileReadKeys(FakeDataPorts fakes) =>
        ProfileReadCalls(fakes)
            .SelectMany(call => call.Arg<IReadOnlyCollection<string>>().Select(serviceId => (call.Arg<decimal>(), serviceId)))
            .ToArray();

    private static string[] ServicesReadOn(IEnumerable<(decimal ListId, string ServiceId)> reads, decimal listId) =>
        reads.Where(read => read.ListId == listId).Select(read => read.ServiceId).Order(StringComparer.Ordinal).ToArray();
}

