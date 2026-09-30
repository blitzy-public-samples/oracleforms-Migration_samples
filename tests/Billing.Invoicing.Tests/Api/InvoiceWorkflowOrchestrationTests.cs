using System.Text.Json;
using System.Text.Json.Serialization;
using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Api.Errors;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;

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
    private const int OnHold = 2;
    private const int PackageServiceLocation = 14;

    private static readonly OperatorContext Operator = new()
    {
        UserNo = 101,
        UserName = "TESTER",
        InfoCenterId = "1",
        MachineName = "TEST-PC",
        SessionId = "7D3F2C1B9A8E4F6D8C2B1A0F9E8D7C6B",
    };

    private static readonly DateTime DraftDate = new(2026, 9, 29, 10, 0, 0);

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

    private static InvoiceLineDraft PackageLine(string serviceId, string role, string clientId, int? componentOrder) =>
        Line(serviceId, clientId) with
        {
            PackageServiceId = PackageService,
            PackageInstanceId = PackageInstance,
            PackageLineRole = role,
            PackageComponentOrder = componentOrder,
        };

    private static DraftDto CashDraft() => new()
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
    };

    private static DraftDto CreditDraft()
    {
        var draft = CashDraft();
        return draft with
        {
            Header = draft.Header with { CompCode = CreditCompany, PayType = 2, ClassCode = ClassCode },
        };
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

    private static bool AnySessionCommitted(FakeDataPorts fakes) =>
        fakes.SessionFactory.Sessions.Any(session => session.Committed);

    private static bool Called(IEnumerable<FakeCall> calls, string method) =>
        calls.Any(call => call.Method == method);

    private static IEnumerable<IReadOnlyList<InvoiceLineDraft>> PreviewedLines(FakeDataPorts fakes) =>
        fakes.InvoiceApi.Calls
            .Where(call => call.Method == nameof(IBilInvoiceApiGateway.CalculatePreview))
            .Select(call => call.Arg<IReadOnlyList<InvoiceLineDraft>>());

    [Fact]
    public async Task Create_BaselineCashDraft_Succeeds()
    {
        var draft = CashDraft();
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
        var draft = CreditDraft();
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
        var draft = WithHeader(CreditDraft(), header => header with { DiscT = 1, FinalDiscPerc = 30m });
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
        var draft = CreditDraft();
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
        var draft = CreditDraft();
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
        var draft = WithHeader(CreditDraft(), header => header with { DiscT = 1, FinalDiscPerc = 5m });
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
        var request = ForgedRequest(CreditDraft());
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
        var request = ForgedRequest(CreditDraft());
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
        var draft = CashDraft();
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
        var draft = CreditDraft();
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
        var draft = CreditDraft();
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
        var draft = ExpandedPackageDraft();
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
        var draft = ExpandedPackageDraft();
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
        var draft = WithHeader(CashDraft(), header => header with { DeptWise = 1 });
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
        var draft = WithHeader(CashDraft(), header => header with { Call = 1, DeptWise = 0 });
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
        var draft = WithHeader(CashDraft(), header => header with { DeptWise = 1 });
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
        var draft = CashDraft() with { Lines = new[] { Line(PackageService, "p1") } };
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
        var draft = CashDraft() with
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
        var draft = CashDraft() with { Lines = new[] { Line(OrdinaryService, "c1"), Line(PackageService, "p1") } };
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
    [Trait("Decision", "D-54")]
    [Trait("OpenItem", "OI-20")]
    public async Task Create_RecordedRequestId_ReplaysBeforePreflight()
    {
        var draft = CreditDraft();
        var fakes = Arrange(draft);
        fakes.Invoices.CreateRequest = requestId => string.Equals(requestId, draft.RequestId, StringComparison.Ordinal)
            ? (9001L, PatientNo, new DateTimeOffset(2026, 9, 29, 10, 5, 0, TimeSpan.Zero))
            : null;
        fakes.Lookups.PatientCoverage = ValidCoverage(draft) with { CompanyIsActive = OnHold };
        fakes.Lookups.ServiceProfile = serviceId => Profile(serviceId) with { ConsRev = 1 };
        fakes.InvoiceApi.CreateMessage = ReplayMessage;

        var response = await fakes.CreateService().Create(CreateRequest(draft), Operator);

        Assert.DoesNotContain(response.Messages, IsBlocking);
        Assert.Equal(1, CreateCalls(fakes));
        Assert.Equal(draft.RequestId, CreateCall(fakes).Arg<string>());
        Assert.False(Called(fakes.Lookups.Calls, nameof(ILookupQueries.GetPatientCoverage)));
        Assert.Empty(fakes.PatientTransfer.Calls);
        Assert.False(Called(fakes.Legacy.Calls, nameof(ILegacyExternalCalls.ValidateTotalInvoice)));
        Assert.DoesNotContain(
            fakes.SessionFactory.Sessions.SelectMany(session => session.Events),
            sessionEvent => sessionEvent == ReceptionTransferSavepointEvent);
        Assert.Equal(ReplayMessage, response.Message);
        Assert.Contains(OpenItemIds.OI20, response.OpenItems);
    }

    [Fact]
    [Trait("Decision", "D-45")]
    [Trait("OpenItem", "OI-20")]
    public async Task Create_TotalValidationOpenItem_CommitsAfterTheCheck()
    {
        var draft = CreditDraft();
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
        var draft = CreditDraft();
        var fakes = Arrange(draft);
        fakes.Legacy.ValidateTotalInvoiceException = () => new InvalidOperationException("total check failed");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fakes.CreateService().Create(CreateRequest(draft), Operator));

        var events = CreateCall(fakes).Arg<FakeOracleSession>().Events;
        Assert.Contains(RollbackEvent, events);
        Assert.DoesNotContain(CommitEvent, events);
        Assert.True(Called(fakes.Legacy.Calls, nameof(ILegacyExternalCalls.ValidateTotalInvoice)));
    }

    private static DraftDto ExpandedPackageDraft() => CashDraft() with
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

    private static string[] QueueFlagIds(FakeDataPorts fakes) =>
        fakes.Lookups.Calls
            .Where(call => call.Method == nameof(ILookupQueries.GetServiceQueueFlags))
            .SelectMany(call => call.Arg<IReadOnlyCollection<string>>())
            .ToArray();
}

