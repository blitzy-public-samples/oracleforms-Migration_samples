using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Tests.Api;

/// <summary>Same-request-id creates that pass the replay-first read and are answered by the package's replay inside the create transaction.</summary>
[Trait("Category", "Orchestration")]
public sealed class CreateReplayRaceTests
{
    private const string PatientNo = "P100";
    private const int ClinicId = 5;
    private const int DoctorId = 12;
    private const string CashCompany = "0";
    private const string OrdinaryService = "S1";
    private const string OpdCategory = "OPD";
    private const long CreatedInvoiceNo = 9001L;
    private const long OtherInvoiceNo = 8001L;
    private const string CreatedMessage = "Invoice 9001 created successfully.";
    private const string ReplayMessage = "Invoice 9001 was already created for this request.";
    private const string CommitEvent = "Commit";
    private const string RollbackEvent = "Rollback";
    private const string ReceptionTransferSavepointEvent = "Save:dr21";
    private const string ReceptionTransferRuleId = "DR-21";
    private const string CreateRequestEntry = $"{nameof(IInvoiceQueries)}.{nameof(IInvoiceQueries.GetCreateRequest)}";
    private const string CreateFullInvoiceEntry = $"{nameof(IBilInvoiceApiGateway)}.{nameof(IBilInvoiceApiGateway.CreateFullInvoice)}";
    private const string ReceptionTransferSaveEntry = $"{nameof(IOracleSession)}.{ReceptionTransferSavepointEvent}";
    private const string ReceptionTransferClearEntry = $"{nameof(IPatientTransferCommand)}.{nameof(IPatientTransferCommand.ClearReceptionTransfer)}";
    private const string TotalCheckEntry = $"{nameof(ILegacyExternalCalls)}.{nameof(ILegacyExternalCalls.ValidateTotalInvoice)}";
    private const string SessionCommitEntry = $"{nameof(IOracleSession)}.{CommitEvent}";
    private const string SessionRollbackEntry = $"{nameof(IOracleSession)}.{RollbackEvent}";
    private const string SessionDisposeEntry = $"{nameof(IOracleSession)}.Dispose";

    private static readonly OperatorContext Operator = new()
    {
        UserNo = 101,
        UserName = "TESTER",
        InfoCenterId = "1",
        MachineName = "TEST-PC",
        SessionId = "7D3F2C1B9A8E4F6D8C2B1A0F9E8D7C6B",
    };

    private static readonly DateTime DraftDate = new(2026, 9, 29, 10, 0, 0);

    private static readonly DateTime CommittedInvDate = new(2026, 9, 29, 10, 0, 5);

    private static readonly DateTimeOffset CommittedAt = new(2026, 9, 29, 10, 0, 6, TimeSpan.Zero);

    [Fact]
    [Trait("Decision", "D-44")]
    [Trait("Decision", "D-45")]
    [Trait("OpenItem", "OI-20")]
    public async Task Create_SameIdCreateAnsweredByThePackageReplay_SkipsTheClearAndTheTotalCheckAndCommits()
    {
        var draft = await CashDraft();
        var fakes = Arrange(draft);
        var reads = 0;
        fakes.Invoices.CreateRequest = CommittedAfterFirstRead(draft, () => ++reads, Committed(CreatedInvoiceNo));
        fakes.InvoiceApi.CreateMessage = ReplayMessage;

        var response = await fakes.CreateService().Create(new CreateInvoiceRequest { Draft = draft }, Operator);

        Assert.Equal(CreatedInvoiceNo, response.InvNo);
        Assert.Equal(ReplayMessage, response.Message);
        Assert.Contains(OpenItemIds.OI20, response.OpenItems);
        Assert.DoesNotContain(response.Messages, message => message.Rule == ReceptionTransferRuleId);
        Assert.Equal(2, reads);
        Assert.All(fakes.CallsTo(nameof(IInvoiceQueries.GetCreateRequest)), call => Assert.Equal(draft.RequestId, call.Arg<string>()));
        Assert.Empty(fakes.PatientTransfer.Calls);
        Assert.DoesNotContain(fakes.Legacy.Calls, call => call.Method == nameof(ILegacyExternalCalls.ValidateTotalInvoice));
        var session = CreateSession(fakes);
        Assert.DoesNotContain(ReceptionTransferSavepointEvent, session.Events);
        Assert.True(session.Committed);
        Assert.False(session.RolledBack);
        Assert.Equal(
            new[] { CreateFullInvoiceEntry, CreateRequestEntry, SessionCommitEntry, SessionDisposeEntry },
            JournalFromTheSave(fakes));
    }

    [Fact]
    [Trait("Decision", "D-44")]
    [Trait("Decision", "D-45")]
    [Trait("OpenItem", "OI-20")]
    public async Task Create_NewCreateWithNoCommittedRequest_ClearsBehindItsSavepointThenRunsTheTotalCheckBeforeTheCommit()
    {
        var draft = await CashDraft();
        var fakes = Arrange(draft);
        var reads = 0;
        fakes.Invoices.CreateRequest = _ =>
        {
            reads++;
            return null;
        };

        var response = await fakes.CreateService().Create(new CreateInvoiceRequest { Draft = draft }, Operator);

        Assert.Equal(CreatedInvoiceNo, response.InvNo);
        Assert.Equal(CreatedMessage, response.Message);
        Assert.Contains(OpenItemIds.OI20, response.OpenItems);
        Assert.Equal(2, reads);
        Assert.Equal(
            new[]
            {
                CreateFullInvoiceEntry,
                CreateRequestEntry,
                ReceptionTransferSaveEntry,
                ReceptionTransferClearEntry,
                TotalCheckEntry,
                SessionCommitEntry,
                SessionDisposeEntry,
            },
            JournalFromTheSave(fakes));
        var clear = Assert.Single(fakes.PatientTransfer.Calls);
        Assert.Same(CreateSession(fakes), clear.Arg<FakeOracleSession>());
        Assert.Equal(PatientNo, clear.Arg<string>());
        Assert.False(CreateSession(fakes).RolledBack);
    }

    [Theory]
    [Trait("Decision", "D-44")]
    [Trait("Decision", "D-54")]
    [InlineData(OtherInvoiceNo)]
    [InlineData(null)]
    public async Task Create_SameIdCreateWhoseCommittedRequestNamesAnotherInvoice_RollsBackWithoutTheClearOrTheCommit(long? committedInvNo)
    {
        var draft = await CashDraft();
        var fakes = Arrange(draft);
        var reads = 0;
        fakes.Invoices.CreateRequest = CommittedAfterFirstRead(draft, () => ++reads, Committed(committedInvNo));
        fakes.InvoiceApi.CreateMessage = ReplayMessage;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fakes.CreateService().Create(new CreateInvoiceRequest { Draft = draft }, Operator));

        Assert.Equal(
            $"The package returned invoice {CreatedInvoiceNo}, which is not the invoice recorded for request {draft.RequestId}.",
            failure.Message);
        Assert.Equal(2, reads);
        Assert.Empty(fakes.PatientTransfer.Calls);
        Assert.DoesNotContain(fakes.Legacy.Calls, call => call.Method == nameof(ILegacyExternalCalls.ValidateTotalInvoice));
        var session = CreateSession(fakes);
        Assert.True(session.RolledBack);
        Assert.False(session.Committed);
        Assert.Equal(
            new[] { CreateFullInvoiceEntry, CreateRequestEntry, SessionRollbackEntry, SessionDisposeEntry },
            JournalFromTheSave(fakes));
    }

    [Fact]
    [Trait("Decision", "D-44")]
    public async Task Create_RequestRereadAfterThePackageCreateFails_RollsBackWithoutTheClearOrTheCommit()
    {
        var draft = await CashDraft();
        var fakes = Arrange(draft);
        var reads = 0;
        fakes.Invoices.CreateRequest = _ => ++reads == 1 ? null : throw new TimeoutException("create-request read timed out");

        await Assert.ThrowsAsync<TimeoutException>(
            () => fakes.CreateService().Create(new CreateInvoiceRequest { Draft = draft }, Operator));

        Assert.Equal(2, reads);
        Assert.Empty(fakes.PatientTransfer.Calls);
        Assert.DoesNotContain(fakes.Legacy.Calls, call => call.Method == nameof(ILegacyExternalCalls.ValidateTotalInvoice));
        var session = CreateSession(fakes);
        Assert.True(session.RolledBack);
        Assert.False(session.Committed);
        Assert.DoesNotContain(ReceptionTransferSavepointEvent, session.Events);
    }

    /// <summary>Returns a cash draft with the request id, draft date and seal that NewDraft issues when the database time is the draft's date.</summary>
    private static async Task<DraftDto> CashDraft()
    {
        var issuer = new FakeDataPorts();
        issuer.Lookups.DatabaseTime = DraftDate;
        var issued = (await issuer.CreateService().NewDraft(new InvoiceEntryParameters(), Operator)).Draft;

        Assert.Equal(DraftDate, issued.DraftDate);
        Assert.NotNull(issued.DraftSeal);
        return new DraftDto
        {
            RequestId = issued.RequestId,
            DraftDate = issued.DraftDate,
            DraftSeal = issued.DraftSeal,
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
            Lines = new[]
            {
                new InvoiceLineDraft { ServiceId = OrdinaryService, Qty = 1m, DiscountType = "R", ClientId = "c1" },
            },
            Parameters = new InvoiceEntryParameters(),
            DiscountLimitChoice = null,
        };
    }

    /// <summary>Fakes under which the cash draft passes the create pre-flight and reaches the package create.</summary>
    private static FakeDataPorts Arrange(DraftDto draft)
    {
        var fakes = new FakeDataPorts();
        fakes.Lookups.PatientCoverage = new PatientCoverageSnapshot { PatientNo = draft.Header.PatientNo, CompCode = CashCompany };
        fakes.Lookups.ClinicProfile = (clinicId, _) => new ClinicProfile { ClinicId = clinicId, SysCatType = OpdCategory };
        fakes.Lookups.ServiceProfile = serviceId => new ServiceProfile
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
        fakes.Lookups.UserMaxDiscount = 100m;
        fakes.Lookups.ClassAdvancedMode = null;
        fakes.Lookups.PatientCardId = null;
        fakes.Lookups.RequestedServices = Array.Empty<string>();
        fakes.Invoices.ClaimPreload = _ => null;
        fakes.InvoiceApi.InvoiceNo = CreatedInvoiceNo;
        fakes.InvoiceApi.CreateMessage = CreatedMessage;
        return fakes;
    }

    /// <summary>Completed create-request row committed by an earlier attempt for <paramref name="invNo"/>.</summary>
    private static (long? InvNo, string? PatientNo, DateTimeOffset? CompletedAt, DateTime? InvDate, string? CompCode, string? SubCompCode, bool ClinicHasAgeLimit) Committed(
        long? invNo) =>
        (invNo, PatientNo, CommittedAt, CommittedInvDate, CashCompany, null, false);

    /// <summary>Create-request fake that counts every read, answers the draft's first read with null and every later read of it with <paramref name="committed"/>; any other id reads null.</summary>
    private static Func<string, (long? InvNo, string? PatientNo, DateTimeOffset? CompletedAt, DateTime? InvDate, string? CompCode, string? SubCompCode, bool ClinicHasAgeLimit)?> CommittedAfterFirstRead(
        DraftDto draft,
        Func<int> countRead,
        (long? InvNo, string? PatientNo, DateTimeOffset? CompletedAt, DateTime? InvDate, string? CompCode, string? SubCompCode, bool ClinicHasAgeLimit) committed) =>
        requestId => countRead() > 1 && string.Equals(requestId, draft.RequestId, StringComparison.Ordinal) ? committed : null;

    /// <summary>The session the package create ran in.</summary>
    private static FakeOracleSession CreateSession(FakeDataPorts fakes) =>
        Assert.Single(fakes.InvoiceApi.Calls, call => call.Method == nameof(IBilInvoiceApiGateway.CreateFullInvoice)).Arg<FakeOracleSession>();

    /// <summary>Journal entries from the package create onward.</summary>
    private static IEnumerable<string> JournalFromTheSave(FakeDataPorts fakes) =>
        fakes.Journal.Skip(fakes.Journal.IndexOf(CreateFullInvoiceEntry));
}
