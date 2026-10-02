using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Api.Errors;
using Billing.Invoicing.Api.Services;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Tests.Api;

/// <summary>OI-32 gate and DR-23 ADD_TO_LIST on server-read package flags, whatever package roles and instance ids the request lines declare.</summary>
[Trait("Category", "Orchestration")]
public sealed class PackageRoleIntegrityTests
{
    private const string PatientNo = "P100";
    private const int ClinicId = 5;
    private const int DoctorId = 12;
    private const string CashCompany = "0";
    private const string CreditCompany = "1001";
    private const string SubCompany = "SC1";
    private const int ClassCode = 3;
    private const string QueuedPackage = "PKG1";
    private const string UnqueuedPackage = "PKG2";
    private const string FirstComponent = "C1";
    private const string QueuedComponent = "C2";
    private const string ThirdComponent = "C3";
    private const string QueuedService = "Q1";
    private const string PlainService = "S1";
    private const string ParentRole = "PARENT";
    private const string ComponentRole = "COMPONENT";
    private const string UnknownRole = "X";
    private const string Instance = "Z9";
    private const int PackageServiceLocation = 14;
    private const decimal PreviewListId = 10m;
    private const string AddToListItem = "ADD_TO_LIST";
    private const string LineTarget = "LINE";

    private static readonly OperatorContext Operator = new()
    {
        UserNo = 101,
        UserName = "TESTER",
        InfoCenterId = "1",
        MachineName = "TEST-PC",
        SessionId = "7D3F2C1B9A8E4F6D8C2B1A0F9E8D7C6B",
    };

    private static readonly DateTime DraftDate = new(2026, 9, 29, 10, 0, 0);

    /// <summary>Checks that an insured create with a sub-company gates OI-32 on PKG1's queued PACKAGE_DTL component under every declared role.</summary>
    [Theory]
    [Trait("Decision", "D-52")]
    [Trait("OpenItem", "OI-32")]
    [InlineData(null, null)]
    [InlineData(UnknownRole, null)]
    [InlineData(ParentRole, Instance)]
    [InlineData(ComponentRole, Instance)]
    public async Task Create_InsuredPackageUnderAnyDeclaredRole_GatesOi32(string? role, string? instanceId)
    {
        var draft = await InsuredDraft(RoleLine(QueuedPackage, "p1", role, instanceId));
        var fakes = Arrange(insured: true);

        var failure = await GateAsync(() => fakes.CreateService().Create(new CreateInvoiceRequest { Draft = draft }, Operator));

        Assert.Contains(OpenItemIds.OI32, GateIds(failure));
        AssertPackageDetailRead(fakes, QueuedPackage);
        Assert.Equal(0, CreateCalls(fakes));
    }

    /// <summary>Checks that line validation of an insured draft with a sub-company gates OI-32 on PKG1's queued PACKAGE_DTL component under every declared role.</summary>
    [Theory]
    [Trait("Decision", "D-52")]
    [Trait("OpenItem", "OI-32")]
    [InlineData(null, null)]
    [InlineData(UnknownRole, null)]
    [InlineData(ParentRole, Instance)]
    [InlineData(ComponentRole, Instance)]
    public async Task ValidateLine_InsuredPackageUnderAnyDeclaredRole_GatesOi32(string? role, string? instanceId)
    {
        var draft = await InsuredDraft(RoleLine(QueuedPackage, "p1", role, instanceId));
        var fakes = Arrange(insured: true);

        var failure = await GateAsync(() => fakes.CreateService().Validate(
            new ValidateDraftRequest { Draft = draft, Target = LineTarget, LineIndex = 0 }, Operator));

        Assert.Contains(OpenItemIds.OI32, GateIds(failure));
        AssertPackageDetailRead(fakes, QueuedPackage);
    }

    /// <summary>Checks that the preview of an insured draft with a sub-company lists OI-32 for PKG1 under every declared role.</summary>
    [Theory]
    [Trait("Decision", "D-52")]
    [Trait("OpenItem", "OI-32")]
    [InlineData(null, null)]
    [InlineData(UnknownRole, null)]
    [InlineData(ParentRole, Instance)]
    [InlineData(ComponentRole, Instance)]
    public async Task Preview_InsuredPackageUnderAnyDeclaredRole_ListsOi32(string? role, string? instanceId)
    {
        var draft = await InsuredDraft(RoleLine(QueuedPackage, "p1", role, instanceId));
        var fakes = Arrange(insured: true);

        var response = await fakes.CreateService().Preview(draft, Operator);

        Assert.Contains(OpenItemIds.OI32, response.OpenItems);
        AssertPackageDetailRead(fakes, QueuedPackage);
    }

    /// <summary>Checks that a cash create sends ADD_TO_LIST 1 for PKG1, at SERV_LOC_ID 14 with a queued PACKAGE_DTL component, under every declared role.</summary>
    [Theory]
    [Trait("Decision", "D-37")]
    [Trait("Decision", "D-52")]
    [InlineData(null, null)]
    [InlineData(UnknownRole, null)]
    [InlineData(ParentRole, Instance)]
    [InlineData(ComponentRole, Instance)]
    public async Task Create_CashPackageUnderAnyDeclaredRole_SendsAddToListOne(string? role, string? instanceId)
    {
        var draft = await CashDraft(RoleLine(QueuedPackage, "p1", role, instanceId));
        var fakes = Arrange(insured: false);

        var response = await fakes.CreateService().Create(new CreateInvoiceRequest { Draft = draft }, Operator);

        Assert.NotNull(response.InvNo);
        AssertPackageDetailRead(fakes, QueuedPackage);
        Assert.Equal((int?)1, SentAddToList(fakes));
    }

    /// <summary>Checks that line validation of a cash draft adjusts ADD_TO_LIST to 1 for PKG1 under every declared role.</summary>
    [Theory]
    [Trait("Decision", "D-37")]
    [Trait("Decision", "D-52")]
    [InlineData(null, null)]
    [InlineData(UnknownRole, null)]
    [InlineData(ParentRole, Instance)]
    [InlineData(ComponentRole, Instance)]
    public async Task ValidateLine_CashPackageUnderAnyDeclaredRole_AdjustsAddToListOne(string? role, string? instanceId)
    {
        var draft = await CashDraft(RoleLine(QueuedPackage, "p1", role, instanceId));
        var fakes = Arrange(insured: false);

        var response = await fakes.CreateService().Validate(
            new ValidateDraftRequest { Draft = draft, Target = LineTarget, LineIndex = 0 }, Operator);

        Assert.Equal(1, Assert.IsType<int>(response.Adjusted[AddToListItem]));
        AssertPackageDetailRead(fakes, QueuedPackage);
    }

    /// <summary>Checks that a queued service declared as a component of a non-package parent stays a top-level line and sets ADD_TO_LIST 1.</summary>
    [Fact]
    [Trait("Decision", "D-37")]
    [Trait("Decision", "D-52")]
    public async Task Create_CashQueuedServiceDeclaredComponentOfNonPackage_SendsAddToListOne()
    {
        var draft = await CashDraft(
            RoleLine(PlainService, "p1", ParentRole, Instance),
            RoleLine(QueuedService, "k1", ComponentRole, Instance));
        var fakes = Arrange(insured: false);

        var response = await fakes.CreateService().Create(new CreateInvoiceRequest { Draft = draft }, Operator);

        Assert.NotNull(response.InvNo);
        Assert.DoesNotContain(fakes.Lookups.Calls, call => call.Method == nameof(ILookupQueries.GetPackageComponentFlags));
        Assert.Equal((int?)1, SentAddToList(fakes));
    }

    /// <summary>Checks that an insured draft with a queued service declared as a component of a non-package parent gates OI-32.</summary>
    [Fact]
    [Trait("Decision", "D-52")]
    [Trait("OpenItem", "OI-32")]
    public async Task Create_InsuredQueuedServiceDeclaredComponentOfNonPackage_GatesOi32()
    {
        var draft = await InsuredDraft(
            RoleLine(PlainService, "p1", ParentRole, Instance),
            RoleLine(QueuedService, "k1", ComponentRole, Instance));
        var fakes = Arrange(insured: true);

        var failure = await GateAsync(() => fakes.CreateService().Create(new CreateInvoiceRequest { Draft = draft }, Operator));

        Assert.Contains(OpenItemIds.OI32, GateIds(failure));
        Assert.Equal(0, CreateCalls(fakes));
    }

    /// <summary>Checks that a PKG1 parent sent with only its unqueued component still counts the omitted queued PACKAGE_DTL component.</summary>
    [Fact]
    [Trait("Decision", "D-37")]
    [Trait("Decision", "D-52")]
    public async Task Create_CashPartialPackageExpansionOmittingTheQueuedComponent_SendsAddToListOne()
    {
        var draft = await CashDraft(
            PackageLine(QueuedPackage, ParentRole, "p1", null),
            PackageLine(FirstComponent, ComponentRole, "k1", 1));
        var fakes = Arrange(insured: false);

        var response = await fakes.CreateService().Create(new CreateInvoiceRequest { Draft = draft }, Operator);

        Assert.NotNull(response.InvNo);
        AssertPackageDetailRead(fakes, QueuedPackage);
        Assert.Equal((int?)1, SentAddToList(fakes));
    }

    /// <summary>Checks that an insured PKG1 parent sent with only its unqueued component gates OI-32 on the omitted queued component.</summary>
    [Fact]
    [Trait("Decision", "D-52")]
    [Trait("OpenItem", "OI-32")]
    public async Task Create_InsuredPartialPackageExpansionOmittingTheQueuedComponent_GatesOi32()
    {
        var draft = await InsuredDraft(
            PackageLine(QueuedPackage, ParentRole, "p1", null),
            PackageLine(FirstComponent, ComponentRole, "k1", 1));
        var fakes = Arrange(insured: true);

        var failure = await GateAsync(() => fakes.CreateService().Create(new CreateInvoiceRequest { Draft = draft }, Operator));

        Assert.Contains(OpenItemIds.OI32, GateIds(failure));
        Assert.Equal(0, CreateCalls(fakes));
    }

    /// <summary>Checks that a fully expanded PKG1 reads its components' queue flags and sends ADD_TO_LIST 1.</summary>
    [Fact]
    [Trait("Decision", "D-37")]
    public async Task Create_CashFullyExpandedPackage_SendsAddToListOne()
    {
        var draft = await CashDraft(
            PackageLine(QueuedPackage, ParentRole, "p1", null),
            PackageLine(FirstComponent, ComponentRole, "k1", 1),
            PackageLine(QueuedComponent, ComponentRole, "k2", 2));
        var fakes = Arrange(insured: false);

        var response = await fakes.CreateService().Create(new CreateInvoiceRequest { Draft = draft }, Operator);

        Assert.NotNull(response.InvNo);
        var queueFlagIds = fakes.Lookups.Calls
            .Where(call => call.Method == nameof(ILookupQueries.GetServiceQueueFlags))
            .SelectMany(call => call.Arg<IReadOnlyCollection<string>>())
            .ToArray();
        Assert.Contains(FirstComponent, queueFlagIds);
        Assert.Contains(QueuedComponent, queueFlagIds);
        AssertPackageDetailRead(fakes, QueuedPackage);
        Assert.Equal((int?)1, SentAddToList(fakes));
    }

    /// <summary>Checks that a package whose PACKAGE_DTL components are all unqueued sends ADD_TO_LIST 0, unexpanded and expanded.</summary>
    [Theory]
    [Trait("Decision", "D-37")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_CashPackageWithOnlyUnqueuedComponents_SendsAddToListZero(bool expanded)
    {
        var draft = await CashDraft(expanded
            ? new[]
            {
                PackageLine(UnqueuedPackage, ParentRole, "p1", null),
                PackageLine(FirstComponent, ComponentRole, "k1", 1),
                PackageLine(ThirdComponent, ComponentRole, "k3", 2),
            }
            : new[] { RoleLine(UnqueuedPackage, "p1", null, null) });
        var fakes = Arrange(insured: false);

        var response = await fakes.CreateService().Create(new CreateInvoiceRequest { Draft = draft }, Operator);

        Assert.NotNull(response.InvNo);
        AssertPackageDetailRead(fakes, UnqueuedPackage);
        Assert.Equal((int?)0, SentAddToList(fakes));
    }

    /// <summary>Checks that an insured package whose PACKAGE_DTL components are all unqueued is not gated and is created.</summary>
    [Fact]
    [Trait("Decision", "D-52")]
    [Trait("OpenItem", "OI-21")]
    public async Task Create_InsuredPackageWithOnlyUnqueuedComponents_Creates()
    {
        var draft = await InsuredDraft(RoleLine(UnqueuedPackage, "p1", UnknownRole, null));
        var fakes = Arrange(insured: true);

        var response = await fakes.CreateService().Create(new CreateInvoiceRequest { Draft = draft }, Operator);

        Assert.NotNull(response.InvNo);
        Assert.DoesNotContain(OpenItemIds.OI32, response.OpenItems);
        AssertPackageDetailRead(fakes, UnqueuedPackage);
        Assert.Equal((int?)0, SentAddToList(fakes));
    }

    /// <summary>SERVICES flags of the catalogue: PKG1 and PKG2 are packages at SERV_LOC_ID 14, Q1 and C2 are queued, every other service is plain; no read carries components.</summary>
    private static ServiceProfile Catalogue(string serviceId) => serviceId switch
    {
        QueuedPackage or UnqueuedPackage => Plain(serviceId) with { IsPackage = 1, ServLocId = PackageServiceLocation },
        QueuedService or QueuedComponent => Plain(serviceId) with { AddToQue = 1 },
        _ => Plain(serviceId),
    };

    /// <summary>PACKAGE_DTL components of the catalogue: PKG1 holds C1 and the queued C2, PKG2 holds the unqueued C1 and C3.</summary>
    private static IReadOnlyList<ServiceProfile> PackageDetail(string packageServiceId) => packageServiceId switch
    {
        QueuedPackage => new[] { Plain(FirstComponent), Plain(QueuedComponent) with { AddToQue = 1 } },
        UnqueuedPackage => new[] { Plain(FirstComponent), Plain(ThirdComponent) },
        _ => Array.Empty<ServiceProfile>(),
    };

    private static ServiceProfile Plain(string serviceId) => new()
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

    private static FakeDataPorts Arrange(bool insured)
    {
        var fakes = new FakeDataPorts();
        fakes.Lookups.PatientCoverage = insured ? InsuredCoverage() : new PatientCoverageSnapshot { PatientNo = PatientNo, CompCode = CashCompany };
        fakes.Lookups.ClinicProfile = (clinicId, _) => new ClinicProfile { ClinicId = clinicId, SysCatType = "OPD" };
        fakes.Lookups.ServiceProfile = Catalogue;
        fakes.Lookups.PackageComponentFlags = PackageDetail;
        fakes.Lookups.UserMaxDiscount = 100m;
        fakes.Lookups.ClassAdvancedMode = null;
        fakes.Lookups.PatientCardId = null;
        fakes.Lookups.RequestedServices = Array.Empty<string>();
        fakes.Invoices.CreateRequest = _ => null;
        fakes.Invoices.ClaimPreload = _ => null;
        return fakes;
    }

    /// <summary>Credit coverage whose server-read sub-company is SC1.</summary>
    private static PatientCoverageSnapshot InsuredCoverage() => new()
    {
        PatientNo = PatientNo,
        CompCode = CreditCompany,
        SubCompCode = SubCompany,
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

    private static InvoiceLineDraft RoleLine(string serviceId, string clientId, string? role, string? instanceId) => new()
    {
        ServiceId = serviceId,
        Qty = 1m,
        DiscountType = "R",
        ClientId = clientId,
        PackageLineRole = role,
        PackageInstanceId = instanceId,
    };

    /// <summary>Line of an expanded PKG1 or PKG2 instance as the package import returns it.</summary>
    private static InvoiceLineDraft PackageLine(string serviceId, string role, string clientId, int? componentOrder) =>
        RoleLine(serviceId, clientId, role, Instance) with
        {
            PackageServiceId = serviceId is QueuedPackage or UnqueuedPackage ? serviceId : null,
            PackageComponentOrder = componentOrder,
        };

    private static Task<DraftDto> CashDraft(params InvoiceLineDraft[] lines) => Issued(new DraftDto
    {
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
        Lines = lines,
        Parameters = new InvoiceEntryParameters(),
        DiscountLimitChoice = null,
    });

    private static async Task<DraftDto> InsuredDraft(params InvoiceLineDraft[] lines)
    {
        var draft = await CashDraft(lines);
        return draft with { Header = draft.Header with { CompCode = CreditCompany, PayType = 2, ClassCode = ClassCode } };
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

    private static async Task<NotImplementedException> GateAsync(Func<Task> act)
    {
        var failure = await Assert.ThrowsAsync<NotImplementedException>(act);
        Assert.StartsWith(OpenItemIds.OI32 + ":", failure.Message, StringComparison.Ordinal);
        return failure;
    }

    private static IReadOnlyList<string> GateIds(Exception failure) =>
        Assert.IsAssignableFrom<IReadOnlyList<string>>(failure.Data[ProblemDetailsWriter.OpenItemsDataKey]);

    /// <summary>Asserts one PACKAGE_DTL read, of the package on the preview list.</summary>
    private static void AssertPackageDetailRead(FakeDataPorts fakes, string packageServiceId) =>
        Assert.Equal(
            new object?[] { packageServiceId, PreviewListId },
            Assert.Single(fakes.Lookups.Calls, call => call.Method == nameof(ILookupQueries.GetPackageComponentFlags)).Args);

    private static int CreateCalls(FakeDataPorts fakes) =>
        fakes.InvoiceApi.Calls.Count(call => call.Method == nameof(IBilInvoiceApiGateway.CreateFullInvoice));

    private static int? SentAddToList(FakeDataPorts fakes) =>
        Assert.Single(fakes.InvoiceApi.Calls, call => call.Method == nameof(IBilInvoiceApiGateway.CreateFullInvoice))
            .Arg<InvoiceHeaderDraft>()
            .AddToList;
}
