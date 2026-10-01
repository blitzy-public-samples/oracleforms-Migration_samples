using System.Globalization;
using System.Reflection;
using Billing.Invoicing.Api.Services;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;
using Oracle.ManagedDataAccess.Client;

namespace Billing.Invoicing.Tests.Api;

/// <summary>One recorded port call: the method name and its arguments in declared order, cancellation tokens omitted.</summary>
/// <param name="Method">Name of the called port method.</param>
/// <param name="Args">Arguments of the call.</param>
public sealed record FakeCall(string Method, IReadOnlyList<object?> Args)
{
    /// <summary>Returns the first argument assignable to <typeparamref name="T"/>.</summary>
    /// <exception cref="InvalidOperationException">No argument is assignable to <typeparamref name="T"/>.</exception>
    public T Arg<T>()
    {
        foreach (var arg in Args)
        {
            if (arg is T value)
            {
                return value;
            }
        }

        throw new InvalidOperationException($"{Method} received no argument of type {typeof(T).Name}.");
    }
}

/// <summary>Appends port calls to a fake's call list and to the shared journal.</summary>
internal static class FakeRecorder
{
    /// <summary>Records <paramref name="method"/> of <paramref name="port"/> with its arguments.</summary>
    public static void Record(List<string> journal, List<FakeCall> calls, string port, string method, IReadOnlyList<object?> args)
    {
        journal.Add($"{port}.{method}");
        calls.Add(new FakeCall(method, args));
    }
}

/// <summary>In-memory <see cref="IOracleSession"/> that records its transaction events.</summary>
public sealed class FakeOracleSession : IOracleSession
{
    private const string CommitEvent = "Commit";
    private const string RollbackEvent = "Rollback";
    private const string DisposeEvent = "Dispose";

    private readonly List<string> _journal;

    /// <summary>Creates a session writing its events to <paramref name="journal"/>.</summary>
    public FakeOracleSession(List<string> journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        _journal = journal;
    }

    /// <summary>Transaction events in call order: Commit, Rollback, Save:name, Rollback:name, Dispose.</summary>
    public List<string> Events { get; } = new();

    /// <summary>True when the session was committed.</summary>
    public bool Committed => Events.Contains(CommitEvent);

    /// <summary>True when the whole transaction was rolled back.</summary>
    public bool RolledBack => Events.Contains(RollbackEvent);

    /// <summary>Failure of a savepoint call by its event name, Save:name or Rollback:name, raised after the event is recorded; null, or a null result, lets the call succeed.</summary>
    public Func<string, Exception?>? SavepointFailure { get; set; }

    /// <summary>Failure the whole-transaction rollback throws after its event is recorded; null, or a null result, lets it succeed.</summary>
    public Func<Exception?>? RollbackFailure { get; set; }

    public Task Commit(CancellationToken cancellationToken = default)
    {
        Log(CommitEvent);
        return Task.CompletedTask;
    }

    public Task Rollback(CancellationToken cancellationToken = default)
    {
        Log(RollbackEvent);
        return RollbackFailure?.Invoke() is { } failure
            ? Task.FromException(failure)
            : Task.CompletedTask;
    }

    public Task Rollback(string savepointName, CancellationToken cancellationToken = default) =>
        Savepoint($"{RollbackEvent}:{savepointName}");

    public Task Save(string savepointName, CancellationToken cancellationToken = default) =>
        Savepoint($"Save:{savepointName}");

    public ValueTask DisposeAsync()
    {
        Log(DisposeEvent);
        return ValueTask.CompletedTask;
    }

    private Task Savepoint(string sessionEvent)
    {
        Log(sessionEvent);
        return SavepointFailure?.Invoke(sessionEvent) is { } failure
            ? Task.FromException(failure)
            : Task.CompletedTask;
    }

    private void Log(string sessionEvent)
    {
        Events.Add(sessionEvent);
        _journal.Add($"{nameof(IOracleSession)}.{sessionEvent}");
    }
}

/// <summary>In-memory <see cref="IOracleSessionFactory"/> that opens a new <see cref="FakeOracleSession"/> per call.</summary>
public sealed class FakeOracleSessionFactory : IOracleSessionFactory
{
    private readonly List<string> _journal;

    /// <summary>Creates a factory whose sessions share <paramref name="journal"/>.</summary>
    public FakeOracleSessionFactory(List<string> journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        _journal = journal;
    }

    /// <summary>Recorded calls.</summary>
    public List<FakeCall> Calls { get; } = new();

    /// <summary>Sessions opened, in opening order.</summary>
    public List<FakeOracleSession> Sessions { get; } = new();

    /// <summary><see cref="FakeOracleSession.SavepointFailure"/> of every session opened.</summary>
    public Func<string, Exception?>? SavepointFailure { get; set; }

    /// <summary><see cref="FakeOracleSession.RollbackFailure"/> of every session opened.</summary>
    public Func<Exception?>? RollbackFailure { get; set; }

    public Task<IOracleSession> Open(CancellationToken cancellationToken = default)
    {
        FakeRecorder.Record(_journal, Calls, nameof(IOracleSessionFactory), nameof(Open), []);
        var session = new FakeOracleSession(_journal) { SavepointFailure = SavepointFailure, RollbackFailure = RollbackFailure };
        Sessions.Add(session);
        return Task.FromResult<IOracleSession>(session);
    }
}

/// <summary>In-memory <see cref="ILookupQueries"/> returning canned snapshots and flags.</summary>
public sealed class FakeLookupQueries : ILookupQueries
{
    private readonly List<string> _journal;

    /// <summary>Creates the lookups writing their calls to <paramref name="journal"/>.</summary>
    public FakeLookupQueries(List<string> journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        _journal = journal;
        PackageComponentFlags = packageServiceId => ServiceProfile(packageServiceId)?.Components ?? Array.Empty<ServiceProfile>();
        ServiceQueueFlags = QueueFlagsFromProfiles;
    }

    /// <summary>Recorded calls.</summary>
    public List<FakeCall> Calls { get; } = new();

    /// <summary>PREF values keyed by preference number.</summary>
    public IReadOnlyDictionary<int, string?> Preferences { get; set; } = new Dictionary<int, string?>();

    /// <summary>Coverage snapshot returned for any patient.</summary>
    public PatientCoverageSnapshot? PatientCoverage { get; set; }

    /// <summary>Clinic profile by clinic id and patient number.</summary>
    public Func<int, string?, ClinicProfile?> ClinicProfile { get; set; } =
        (clinicId, _) => new ClinicProfile { ClinicId = clinicId, SysCatType = "OPD" };

    /// <summary>Service profile by service id, on any price list.</summary>
    public Func<string, ServiceProfile?> ServiceProfile { get; set; } =
        serviceId => new ServiceProfile
        {
            ServiceId = serviceId,
            ShowQty = 0,
            BeginOfClaim = 1,
            AddToQue = 0,
            ServLocId = 1,
            ConsRev = 0,
            IsPackage = 0,
            PriceIsFixed = "Y",
            ReqNeedA = 0,
        };

    /// <summary>Component profiles by package service id; defaults to the <c>Components</c> of <see cref="ServiceProfile"/> for the package.</summary>
    public Func<string, IReadOnlyList<ServiceProfile>> PackageComponentFlags { get; set; }

    /// <summary>ADD_TO_QUE by service id; defaults to the <c>AddToQue</c> of <see cref="ServiceProfile"/> per id, ids without a profile absent.</summary>
    public Func<IReadOnlyCollection<string>, IReadOnlyDictionary<string, int>> ServiceQueueFlags { get; set; }

    /// <summary>Maximum final-discount percent of any user.</summary>
    public decimal UserMaxDiscount { get; set; } = 100m;

    /// <summary>Company type of any company.</summary>
    public int? CompanyType { get; set; } = 1;

    /// <summary>IS_DIRECT flag of any company.</summary>
    public int? CompanyIsDirect { get; set; }

    /// <summary>Doctor of any visit.</summary>
    public int? VisitDoctor { get; set; }

    /// <summary>Service ids requested for any claim.</summary>
    public IReadOnlyList<string> RequestedServices { get; set; } = Array.Empty<string>();

    /// <summary>Failure the requested-services read throws after recording the call; null returns <see cref="RequestedServices"/>.</summary>
    public Func<Exception>? RequestedServicesFailure { get; set; }

    /// <summary>Database time.</summary>
    public DateTime DatabaseTime { get; set; } = new(2026, 9, 29, 10, 0, 0);

    /// <summary>Invoice-type list items.</summary>
    public IReadOnlyList<(string Id, string Description)> InvoiceTypes { get; set; } = Array.Empty<(string Id, string Description)>();

    /// <summary>Currency list items.</summary>
    public IReadOnlyList<(string Code, string Name)> Currencies { get; set; } = Array.Empty<(string Code, string Name)>();

    /// <summary>Cash-card id of any patient.</summary>
    public int? PatientCardId { get; set; }

    /// <summary>DISC_CLASSES.USE_ADVANCED of any class.</summary>
    public int? ClassAdvancedMode { get; set; }

    /// <summary>Clinic id, clinic name and doctor name by doctor id; null when the doctor has no single clinic row.</summary>
    public Func<int, (int ClinicId, string? ClinicName, string? DocName)?> DoctorClinic { get; set; } = _ => null;

    public Task<IReadOnlyDictionary<int, string?>> GetPreferences(CancellationToken cancellationToken = default)
    {
        Record(nameof(GetPreferences), []);
        return Task.FromResult(Preferences);
    }

    public Task<PatientCoverageSnapshot?> GetPatientCoverage(string patientNo, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetPatientCoverage), [patientNo]);
        return Task.FromResult(PatientCoverage);
    }

    public Task<ClinicProfile?> GetClinicProfile(int clinicId, string? patientNo, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetClinicProfile), [clinicId, patientNo]);
        return Task.FromResult(ClinicProfile(clinicId, patientNo));
    }

    public Task<ServiceProfile?> GetServiceProfile(string serviceId, decimal listId, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetServiceProfile), [serviceId, listId]);
        return Task.FromResult(ServiceProfile(serviceId));
    }

    /// <summary>Returns the <see cref="ServiceProfile"/> of each distinct id that has one, recording a copy of the ids.</summary>
    public Task<IReadOnlyDictionary<string, ServiceProfile>> GetServiceProfiles(IReadOnlyCollection<string> serviceIds, decimal listId, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetServiceProfiles), [serviceIds.ToArray(), listId]);
        var profiles = new Dictionary<string, ServiceProfile>(StringComparer.Ordinal);
        foreach (var serviceId in serviceIds)
        {
            if (!profiles.ContainsKey(serviceId) && ServiceProfile(serviceId) is { } profile)
            {
                profiles[serviceId] = profile;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, ServiceProfile>>(profiles);
    }

    public Task<IReadOnlyList<ServiceProfile>> GetPackageComponentFlags(string packageServiceId, decimal listId, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetPackageComponentFlags), [packageServiceId, listId]);
        return Task.FromResult(PackageComponentFlags(packageServiceId));
    }

    public Task<decimal> GetUserMaxDiscount(int userNo, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetUserMaxDiscount), [userNo]);
        return Task.FromResult(UserMaxDiscount);
    }

    public Task<int?> GetCompanyType(string compCode, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetCompanyType), [compCode]);
        return Task.FromResult(CompanyType);
    }

    public Task<int?> GetCompanyIsDirect(string compCode, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetCompanyIsDirect), [compCode]);
        return Task.FromResult(CompanyIsDirect);
    }

    public Task<int?> GetVisitDoctor(string visitUnique, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetVisitDoctor), [visitUnique]);
        return Task.FromResult(VisitDoctor);
    }

    public Task<IReadOnlyList<string>> GetRequestedServices(string claimNo, decimal listId, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetRequestedServices), [claimNo, listId]);
        if (RequestedServicesFailure is { } failure)
        {
            throw failure();
        }

        return Task.FromResult(RequestedServices);
    }

    public Task<DateTime> GetDatabaseTime(CancellationToken cancellationToken = default)
    {
        Record(nameof(GetDatabaseTime), []);
        return Task.FromResult(DatabaseTime);
    }

    public Task<IReadOnlyList<(string Id, string Description)>> GetInvoiceTypes(CancellationToken cancellationToken = default)
    {
        Record(nameof(GetInvoiceTypes), []);
        return Task.FromResult(InvoiceTypes);
    }

    public Task<IReadOnlyList<(string Code, string Name)>> GetCurrencies(CancellationToken cancellationToken = default)
    {
        Record(nameof(GetCurrencies), []);
        return Task.FromResult(Currencies);
    }

    public Task<int?> GetPatientCardId(string patientNo, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetPatientCardId), [patientNo]);
        return Task.FromResult(PatientCardId);
    }

    public Task<IReadOnlyDictionary<string, int>> GetServiceQueueFlags(IReadOnlyCollection<string> serviceIds, decimal listId, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetServiceQueueFlags), [serviceIds, listId]);
        return Task.FromResult(ServiceQueueFlags(serviceIds));
    }

    public Task<int?> GetClassAdvancedMode(string subCompCode, string classCode, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetClassAdvancedMode), [subCompCode, classCode]);
        return Task.FromResult(ClassAdvancedMode);
    }

    public Task<(int ClinicId, string? ClinicName, string? DocName)?> GetDoctorClinic(int docId, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetDoctorClinic), [docId]);
        return Task.FromResult(DoctorClinic(docId));
    }

    private IReadOnlyDictionary<string, int> QueueFlagsFromProfiles(IReadOnlyCollection<string> serviceIds)
    {
        var flags = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var serviceId in serviceIds)
        {
            if (!flags.ContainsKey(serviceId) && ServiceProfile(serviceId) is { } profile)
            {
                flags[serviceId] = profile.AddToQue ?? 0;
            }
        }

        return flags;
    }

    private void Record(string method, IReadOnlyList<object?> args) =>
        FakeRecorder.Record(_journal, Calls, nameof(ILookupQueries), method, args);
}

/// <summary>In-memory <see cref="IInvoiceQueries"/> returning canned invoices, request rows, claim preloads and create requests.</summary>
public sealed class FakeInvoiceQueries : IInvoiceQueries
{
    private readonly List<string> _journal;

    /// <summary>Creates the queries writing their calls to <paramref name="journal"/>.</summary>
    public FakeInvoiceQueries(List<string> journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        _journal = journal;
    }

    /// <summary>Recorded calls.</summary>
    public List<FakeCall> Calls { get; } = new();

    /// <summary>Saved invoice by invoice number and local document type.</summary>
    public Func<long, int, (InvoiceHeaderDraft Header, IReadOnlyList<InvoiceLineDraft> Lines, IReadOnlyDictionary<string, object?> Display)?> Invoice { get; set; } =
        (_, _) => null;

    /// <summary>More-details rows by invoice number and local document type.</summary>
    public Func<long, int, (IReadOnlyDictionary<string, object?> Header, IReadOnlyList<IReadOnlyDictionary<string, object?>> Lines, IReadOnlyList<IReadOnlyDictionary<string, object?>> Transfers)?> MoreDetails { get; set; } =
        (_, _) => null;

    /// <summary>Last invoice number of any information centre.</summary>
    public long? LastInvoiceNo { get; set; }

    /// <summary>Selected request rows by patient number, visit and pay type.</summary>
    public Func<string, string, int, IReadOnlyList<(long PatServReqRowId, string ServiceId, int? ReqAStatus, int? ReqNeedA, string? ApprovRefNo)>> SelectedRequestRows { get; set; } =
        (_, _, _) => Array.Empty<(long PatServReqRowId, string ServiceId, int? ReqAStatus, int? ReqNeedA, string? ApprovRefNo)>();

    /// <summary>Claim preload by claim number.</summary>
    public Func<string, (InvoiceHeaderDraft Header, decimal? ListId, decimal? MaxDeductable, int? CardId)?> ClaimPreload { get; set; } =
        _ => null;

    /// <summary>Recorded create request, with its invoice's date, company, sub-company and clinic age-limit flag, by request id.</summary>
    public Func<string, (long? InvNo, string? PatientNo, DateTimeOffset? CompletedAt, DateTime? InvDate, string? CompCode, string? SubCompCode, bool ClinicHasAgeLimit)?> CreateRequest { get; set; } =
        _ => null;

    public Task<(InvoiceHeaderDraft Header, IReadOnlyList<InvoiceLineDraft> Lines, IReadOnlyDictionary<string, object?> Display)?> GetInvoice(long invNo, int localDocType, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetInvoice), [invNo, localDocType]);
        return Task.FromResult(Invoice(invNo, localDocType));
    }

    public Task<(IReadOnlyDictionary<string, object?> Header, IReadOnlyList<IReadOnlyDictionary<string, object?>> Lines, IReadOnlyList<IReadOnlyDictionary<string, object?>> Transfers)?> GetMoreDetails(long invNo, int localDocType, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetMoreDetails), [invNo, localDocType]);
        return Task.FromResult(MoreDetails(invNo, localDocType));
    }

    public Task<long?> GetLastInvoiceNo(string infoCenterId, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetLastInvoiceNo), [infoCenterId]);
        return Task.FromResult(LastInvoiceNo);
    }

    public Task<IReadOnlyList<(long PatServReqRowId, string ServiceId, int? ReqAStatus, int? ReqNeedA, string? ApprovRefNo)>> GetSelectedRequestRows(string patientNo, string visitUnique, int payType, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetSelectedRequestRows), [patientNo, visitUnique, payType]);
        return Task.FromResult(SelectedRequestRows(patientNo, visitUnique, payType));
    }

    public Task<(InvoiceHeaderDraft Header, decimal? ListId, decimal? MaxDeductable, int? CardId)?> GetClaimPreload(string claimNo, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetClaimPreload), [claimNo]);
        return Task.FromResult(ClaimPreload(claimNo));
    }

    public Task<(long? InvNo, string? PatientNo, DateTimeOffset? CompletedAt, DateTime? InvDate, string? CompCode, string? SubCompCode, bool ClinicHasAgeLimit)?> GetCreateRequest(string requestId, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetCreateRequest), [requestId]);
        return Task.FromResult(CreateRequest(requestId));
    }

    private void Record(string method, IReadOnlyList<object?> args) =>
        FakeRecorder.Record(_journal, Calls, nameof(IInvoiceQueries), method, args);
}

/// <summary>In-memory <see cref="ILovQueries"/> returning the same canned rows for every list.</summary>
public sealed class FakeLovQueries : ILovQueries
{
    private readonly List<string> _journal;

    /// <summary>Creates the LOV queries writing their calls to <paramref name="journal"/>.</summary>
    public FakeLovQueries(List<string> journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        _journal = journal;
    }

    /// <summary>Recorded calls.</summary>
    public List<FakeCall> Calls { get; } = new();

    /// <summary>Rows returned by every list.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; set; } = Array.Empty<IReadOnlyDictionary<string, object?>>();

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> Company(string infoCenterId, CancellationToken cancellationToken = default) =>
        Answer(nameof(Company), [infoCenterId]);

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SubCompany(string compCode, string infoCenterId, CancellationToken cancellationToken = default) =>
        Answer(nameof(SubCompany), [compCode, infoCenterId]);

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TheClass(string subCompCode, string infoCenterId, CancellationToken cancellationToken = default) =>
        Answer(nameof(TheClass), [subCompCode, infoCenterId]);

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PayTypes(CancellationToken cancellationToken = default) =>
        Answer(nameof(PayTypes), []);

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> Doc(string infoCenterId, CancellationToken cancellationToken = default) =>
        Answer(nameof(Doc), [infoCenterId]);

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReservNo(DateTime invDate, int docId, string patientNo, string infoCenterId, CancellationToken cancellationToken = default) =>
        Answer(nameof(ReservNo), [invDate, docId, patientNo, infoCenterId]);

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> Offers(int payType, DateTime invDate, string infoCenterId, CancellationToken cancellationToken = default) =>
        Answer(nameof(Offers), [payType, invDate, infoCenterId]);

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> Cat(CancellationToken cancellationToken = default) =>
        Answer(nameof(Cat), []);

    private Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> Answer(string method, IReadOnlyList<object?> args)
    {
        FakeRecorder.Record(_journal, Calls, nameof(ILovQueries), method, args);
        return Task.FromResult(Rows);
    }
}

/// <summary>In-memory <see cref="IBilInvoiceApiGateway"/> echoing preview lines and returning a fixed saved invoice.</summary>
public sealed class FakeBilInvoiceApiGateway : IBilInvoiceApiGateway
{
    private const string PackageSource = "PACKAGE";
    private const string NoAmountDue = "No Amount Due";
    private const string Posted = "Y";
    private const string NotRequested = "N";

    /// <summary>Draft-seal key every fake gateway shares by default, as instances configured with one Invoicing:DraftSealKey do.</summary>
    public const string SharedDraftSealKey = "RmFrZURhdGFQb3J0cyBzaGFyZWQgZHJhZnQtc2VhbCBrZXkgMDE=";

    private readonly List<string> _journal;

    private string _draftSealKey = SharedDraftSealKey;

    private BilInvoiceApiGateway _sealer = Sealer(SharedDraftSealKey);

    /// <summary>Creates the gateway writing its calls to <paramref name="journal"/>.</summary>
    public FakeBilInvoiceApiGateway(List<string> journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        _journal = journal;
    }

    /// <summary>Base64 draft-seal key the seals are computed under by <see cref="BilInvoiceApiGateway.SealDraftDate"/>; empty gives this fake a key of its own.</summary>
    public string DraftSealKey
    {
        get => _draftSealKey;
        set
        {
            _sealer = Sealer(value);
            _draftSealKey = value;
        }
    }

    /// <summary>Recorded calls.</summary>
    public List<FakeCall> Calls { get; } = new();

    /// <summary>Most engine lines a create accepts; reading it is not recorded as a call.</summary>
    public int MaxDraftLines { get; set; } = 1000;

    /// <summary>Price list carried by every default preview line.</summary>
    public decimal? PreviewListId { get; set; } = 10m;

    /// <summary>Preview result by header and lines; null echoes each input line with <see cref="PreviewListId"/>, zero totals and status No Amount Due.</summary>
    public Func<InvoiceHeaderDraft, IReadOnlyList<InvoiceLineDraft>, (IReadOnlyList<EditablePreviewLine> Lines, PreviewTotalsRow Totals)>? Preview { get; set; }

    /// <summary>Failure the preview throws for a header and lines after recording the call; null, or a null result, previews normally.</summary>
    public Func<InvoiceHeaderDraft, IReadOnlyList<InvoiceLineDraft>, Exception?>? PreviewFailure { get; set; }

    /// <summary>Invoice number of the default create result.</summary>
    public long InvoiceNo { get; set; } = 9001;

    /// <summary>Message of the default create result.</summary>
    public string CreateMessage { get; set; } = "Invoice 9001 created.";

    /// <summary>Create result by header, lines and request id; null returns <see cref="InvoiceNo"/>, <see cref="CreateMessage"/>, payment, queue and stock flags Y, print and SMS flags N.</summary>
    public Func<InvoiceHeaderDraft, IReadOnlyList<InvoiceLineDraft>, string, FullInvoiceResultRow>? Create { get; set; }

    /// <summary>Bundled-offer lines by header, offer id and bundle quantity; null returns none.</summary>
    public Func<InvoiceHeaderDraft, decimal, decimal, IReadOnlyList<EditablePreviewLine>>? BundledOfferLines { get; set; }

    /// <summary>Package lines by package service id, price list and parent source id; null returns none with a zero import result.</summary>
    public Func<string, decimal, string?, (IReadOnlyList<EngineLineInput> Lines, ImportResultRow Result)>? PackageLines { get; set; }

    /// <summary>Seals through a real gateway configured with <see cref="DraftSealKey"/>; not recorded as a call.</summary>
    public string SealDraftDate(string requestId, DateTime draftDate) => _sealer.SealDraftDate(requestId, draftDate);

    /// <summary>Real gateway used only to seal draft dates under <paramref name="draftSealKey"/>.</summary>
    private static BilInvoiceApiGateway Sealer(string draftSealKey) =>
        new(new InvoicingDataOptions { DraftSealKey = draftSealKey });

    public Task<(IReadOnlyList<EditablePreviewLine> Lines, PreviewTotalsRow Totals)> CalculatePreview(IOracleSession session, InvoiceHeaderDraft header, IReadOnlyList<InvoiceLineDraft> lines, OperatorContext operatorContext, bool amount1Auto, CancellationToken cancellationToken = default)
    {
        Record(nameof(CalculatePreview), [session, header, lines, operatorContext, amount1Auto]);
        if (PreviewFailure?.Invoke(header, lines) is { } failure)
        {
            throw failure;
        }

        return Task.FromResult(Preview is { } preview ? preview(header, lines) : EchoPreview(lines));
    }

    public Task<FullInvoiceResultRow> CreateFullInvoice(IOracleSession session, InvoiceHeaderDraft header, IReadOnlyList<InvoiceLineDraft> lines, OperatorContext operatorContext, string requestId, CancellationToken cancellationToken = default)
    {
        Record(nameof(CreateFullInvoice), [session, header, lines, operatorContext, requestId]);
        return Task.FromResult(Create is { } create ? create(header, lines, requestId) : SavedInvoice(header, lines));
    }

    public Task<IReadOnlyList<EditablePreviewLine>> GetBundledOfferLines(IOracleSession session, InvoiceHeaderDraft header, OperatorContext operatorContext, decimal offerId, decimal bundleQty, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetBundledOfferLines), [session, header, operatorContext, offerId, bundleQty]);
        return Task.FromResult(
            BundledOfferLines is { } offerLines ? offerLines(header, offerId, bundleQty) : Array.Empty<EditablePreviewLine>());
    }

    public Task<(IReadOnlyList<EngineLineInput> Lines, ImportResultRow Result)> GetPackageLines(IOracleSession session, string packageServiceId, decimal listId, string? parentSourceId, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetPackageLines), [session, packageServiceId, listId, parentSourceId]);
        return Task.FromResult(
            PackageLines is { } packageLines
                ? packageLines(packageServiceId, listId, parentSourceId)
                : ((IReadOnlyList<EngineLineInput>)Array.Empty<EngineLineInput>(), FakeImportResults.Empty(PackageSource)));
    }

    public string BuildPrintUrl(long invNo)
    {
        Record(nameof(BuildPrintUrl), [invNo]);
        throw new NotImplementedException($"{OpenItemIds.OI11}: BIL_REPORTS_PRINT is not available in this build.");
    }

    private (IReadOnlyList<EditablePreviewLine> Lines, PreviewTotalsRow Totals) EchoPreview(IReadOnlyList<InvoiceLineDraft> lines)
    {
        var previewLines = lines
            .Select((line, index) => new EditablePreviewLine
            {
                ClientId = line.ClientId,
                LineNo = index + 1,
                ServiceId = line.ServiceId,
                CatId = line.CatId,
                ListId = PreviewListId,
                Qty = line.Qty,
                PackageServiceId = line.PackageServiceId,
                PackageInstanceId = line.PackageInstanceId,
                PackageLineRole = line.PackageLineRole,
                PackageComponentOrder = line.PackageComponentOrder,
                PackageParentLineId = line.PackageParentLineId,
                PackagePricingMethod = line.PackagePricingMethod,
                PackageDefinitionToken = line.PackageDefinitionToken,
                OfferId = line.OfferId,
                OfferDtlId = line.OfferDtlId,
                OfferType = line.OfferType,
                OfferInstanceId = line.OfferInstanceId,
                OfferLineRole = line.OfferLineRole,
                OfferParentLineId = line.OfferParentLineId,
            })
            .ToArray();

        var totals = new PreviewTotalsRow
        {
            LineCount = previewLines.Length,
            TotalGross = 0m,
            TotalDiscount = 0m,
            TotalNet = 0m,
            PatPay = 0m,
            CompPay = 0m,
            VatTotalPat = 0m,
            VatTotalCo = 0m,
            CashCollected = 0m,
            Amount1 = 0m,
            Amount2 = 0m,
            RemainingAmount = 0m,
            PaymentStatus = NoAmountDue,
        };

        return (previewLines, totals);
    }

    private FullInvoiceResultRow SavedInvoice(InvoiceHeaderDraft header, IReadOnlyList<InvoiceLineDraft> lines) => new()
    {
        InvNo = InvoiceNo,
        InvDate = header.DraftDate,
        PatientNo = header.PatientNo,
        CurrCode = header.CurrCode,
        LineCount = lines.Count,
        TotalGross = 0m,
        TotalDiscount = 0m,
        TotalNet = 0m,
        PatPay = 0m,
        CompPay = 0m,
        VatTotalPat = 0m,
        VatTotalCo = 0m,
        VatTotal = 0m,
        FinalDisc = 0m,
        CashCollected = 0m,
        PaymentPosted = Posted,
        QueuePosted = Posted,
        StockPosted = Posted,
        PrintUrlBuilt = NotRequested,
        SmsSent = NotRequested,
        Message = CreateMessage,
    };

    private void Record(string method, IReadOnlyList<object?> args) =>
        FakeRecorder.Record(_journal, Calls, nameof(IBilInvoiceApiGateway), method, args);
}

/// <summary>ODP.NET exceptions of package refusals and connectivity failures, built through the driver's non-public constructor.</summary>
public static class FakeOracleFailures
{
    private const string DataSource = "HISDB";
    private const string PreviewProcedure = "BIL_INVOICE_API.CALCULATE_PREVIEW";
    private const string PreviewOperation = "CalculatePreview";
    private const string EngineFrameName = "HIS.BIL_INVOICE_ENGINE";
    private const string ApiFrameName = "HIS.BIL_INVOICE_API";

    private static readonly ConstructorInfo? OracleExceptionConstructor = typeof(OracleException).GetConstructor(
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        new[] { typeof(int), typeof(string), typeof(string), typeof(string), typeof(Exception) },
        modifiers: null);

    /// <summary>Returns the preview's BIL_INVOICE_ENGINE -20949 refusal of a package service line without its package instance.</summary>
    public static OracleException UnexpandedParent() =>
        PreviewRefusal(20949, "Invoice create failed: package instance identity is required.", 2496);

    /// <summary>Returns the preview's BIL_INVOICE_ENGINE -20906 refusal of a negative discount percent on line 1.</summary>
    public static OracleException DiscountRefused() =>
        PreviewRefusal(20906, "Invoice create failed: discount percent cannot be negative on line 1.", 575);

    /// <summary>Returns the preview's BIL_INVOICE_API -20978 refusal of a bundled-offer component that does not match its offer definition.</summary>
    public static OracleException BundledOfferRowsInvalid() =>
        PreviewRefusal(
            20978, "Bundled Offer invoice rows are invalid. Component evidence does not match the current offer definition.", ApiFrameName, 442);

    /// <summary>Returns the ORA-12541 connectivity failure of a lookup.</summary>
    public static OracleException NoListener() => Driver(12541, "ORA-12541: TNS:no listener", string.Empty);

    /// <summary>Returns an ORA-20001 application error raised under a lookup's SELECT, with no gateway operation attached.</summary>
    public static OracleException LookupApplicationError() =>
        Driver(20001, "ORA-20001: Lookup refused by a database trigger.\nORA-06512: at \"HIS.SERVICES_GUARD\", line 7", string.Empty);

    private static OracleException PreviewRefusal(int number, string text, int engineLine) =>
        PreviewRefusal(number, text, EngineFrameName, engineLine);

    private static OracleException PreviewRefusal(int number, string text, string frameName, int frameLine)
    {
        var failure = Driver(
            number,
            string.Create(CultureInfo.InvariantCulture, $"ORA-{number}: {text}\nORA-06512: at \"{frameName}\", line {frameLine}"),
            PreviewProcedure);
        failure.Data[OracleErrorParser.OperationKey] = PreviewOperation;
        return failure;
    }

    private static OracleException Driver(int number, string message, string procedure)
    {
        if (OracleExceptionConstructor is null)
        {
            throw new InvalidOperationException("OracleException has no non-public (int, string, string, string, Exception) constructor.");
        }

        return (OracleException)OracleExceptionConstructor.Invoke(new object[]
        {
            number,
            DataSource,
            procedure,
            message,
            new InvalidOperationException("driver detail"),
        });
    }
}

/// <summary>Zero-count import results.</summary>
internal static class FakeImportResults
{
    /// <summary>Returns an import result of <paramref name="sourceType"/> with every count zero.</summary>
    public static ImportResultRow Empty(string sourceType) => new()
    {
        SourceType = sourceType,
        SourceCount = 0,
        ImportedCount = 0,
        SkippedRejectedCount = 0,
        SkippedNeedApprovalCount = 0,
        SkippedInvalidCount = 0,
        HasPriceOverrides = "N",
    };
}

/// <summary>In-memory <see cref="IBilImportGateway"/> returning canned request and visit imports.</summary>
public sealed class FakeBilImportGateway : IBilImportGateway
{
    private const string RequestSource = "REQUEST";
    private const string VisitSource = "NEW_VISIT";

    private readonly List<string> _journal;

    /// <summary>Creates the gateway writing its calls to <paramref name="journal"/>.</summary>
    public FakeBilImportGateway(List<string> journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        _journal = journal;
    }

    /// <summary>Recorded calls.</summary>
    public List<FakeCall> Calls { get; } = new();

    /// <summary>Request import by header, visit, request row ids and approval mode; null returns none with a zero import result.</summary>
    public Func<InvoiceHeaderDraft, string, IReadOnlyList<long>, int, (IReadOnlyList<EngineLineInput> Lines, ImportResultRow Result)>? RequestLines { get; set; }

    /// <summary>Approval check mode by X422_APPROV_CHECK value.</summary>
    public Func<int?, int> ApprovalMode { get; set; } = x422ApprovCheck => x422ApprovCheck == 2 ? 0 : 1;

    /// <summary>Visit line by header and choice; null returns no line with a zero import result.</summary>
    public Func<InvoiceHeaderDraft, VisitLineChoice, (EngineLineInput? Line, ImportResultRow Result)>? VisitLine { get; set; }

    public Task<(IReadOnlyList<EngineLineInput> Lines, ImportResultRow Result)> ImportRequestLines(IOracleSession session, InvoiceHeaderDraft header, OperatorContext operatorContext, string visitUnique, IReadOnlyList<long> patServReqRowIds, int approvalMode, CancellationToken cancellationToken = default)
    {
        Record(nameof(ImportRequestLines), [session, header, operatorContext, visitUnique, patServReqRowIds, approvalMode]);
        return Task.FromResult(
            RequestLines is { } requestLines
                ? requestLines(header, visitUnique, patServReqRowIds, approvalMode)
                : ((IReadOnlyList<EngineLineInput>)Array.Empty<EngineLineInput>(), FakeImportResults.Empty(RequestSource)));
    }

    public int ApprovalCheckMode(int? x422ApprovCheck)
    {
        Record(nameof(ApprovalCheckMode), [x422ApprovCheck]);
        return ApprovalMode(x422ApprovCheck);
    }

    public Task<(EngineLineInput? Line, ImportResultRow Result)> GetVisitLine(IOracleSession session, InvoiceHeaderDraft header, OperatorContext operatorContext, VisitLineChoice choice, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetVisitLine), [session, header, operatorContext, choice]);
        return Task.FromResult(
            VisitLine is { } visitLine ? visitLine(header, choice) : ((EngineLineInput?)null, FakeImportResults.Empty(VisitSource)));
    }

    private void Record(string method, IReadOnlyList<object?> args) =>
        FakeRecorder.Record(_journal, Calls, nameof(IBilImportGateway), method, args);
}

/// <summary>In-memory <see cref="IPatientTransferCommand"/> returning a canned row count or throwing a canned failure.</summary>
public sealed class FakePatientTransferCommand : IPatientTransferCommand
{
    private readonly List<string> _journal;

    /// <summary>Creates the command writing its calls to <paramref name="journal"/>.</summary>
    public FakePatientTransferCommand(List<string> journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        _journal = journal;
    }

    /// <summary>Recorded calls.</summary>
    public List<FakeCall> Calls { get; } = new();

    /// <summary>Rows affected returned by the clear.</summary>
    public int RowsAffected { get; set; } = 1;

    /// <summary>Failure thrown by the clear; null lets it succeed.</summary>
    public Func<Exception>? Throw { get; set; }

    public Task<int> ClearReceptionTransfer(IOracleSession session, string patientNo, CancellationToken cancellationToken = default)
    {
        FakeRecorder.Record(_journal, Calls, nameof(IPatientTransferCommand), nameof(ClearReceptionTransfer), [session, patientNo]);
        if (Throw is { } failure)
        {
            throw failure();
        }

        return Task.FromResult(RowsAffected);
    }
}

/// <summary>In-memory <see cref="ILegacyExternalCalls"/> whose members throw <see cref="NotImplementedException"/> starting with their open-item id.</summary>
public sealed class FakeLegacyExternalCalls : ILegacyExternalCalls
{
    private const string PatientCardKind = "patient-card";
    private const string BarcodeSmsKind = "barcode-sms";
    private const string IqamaCheckKind = "iqama-check";

    private readonly List<string> _journal;

    /// <summary>Creates the blocked calls writing their calls to <paramref name="journal"/>.</summary>
    public FakeLegacyExternalCalls(List<string> journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        _journal = journal;
    }

    /// <summary>Recorded calls.</summary>
    public List<FakeCall> Calls { get; } = new();

    /// <summary>Exception thrown by the total validation.</summary>
    public Func<Exception> ValidateTotalInvoiceException { get; set; } =
        () => new NotImplementedException($"{OpenItemIds.OI20}: VALIDATE_TOTAL_INV is not available in this build.");

    public Task ValidateTotalInvoice(IOracleSession session, long invNo, CancellationToken cancellationToken = default)
    {
        Record(nameof(ValidateTotalInvoice), [session, invNo]);
        throw ValidateTotalInvoiceException();
    }

    public Task<string?> GetPreAuthorization(string patientNo, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetPreAuthorization), [patientNo]);
        throw Blocked(OpenItemIds.OI21, "GET_ELLIGABILTY");
    }

    public Task<decimal> ComputePatientAgeYears(string patientNo, DateTime asOf, CancellationToken cancellationToken = default)
    {
        Record(nameof(ComputePatientAgeYears), [patientNo, asOf]);
        throw Blocked(OpenItemIds.OI22, "DAY_TO_DAYES");
    }

    public Task<decimal> GetPaidBefore(string? compCode, string? subCompCode, string? claimNo, CancellationToken cancellationToken = default)
    {
        Record(nameof(GetPaidBefore), [compCode, subCompCode, claimNo]);
        throw Blocked(OpenItemIds.OI23, "GET_PAYID_VALUE");
    }

    public Task<(decimal? PlanCode, decimal? ListId)> ResolvePricePlan(string infoCenterId, string? compCode, string? subCompCode, CancellationToken cancellationToken = default)
    {
        Record(nameof(ResolvePricePlan), [infoCenterId, compCode, subCompCode]);
        throw Blocked(OpenItemIds.OI24, "GET_PRICE_PLAN");
    }

    /// <summary>Throws OI-47 for patient-card and barcode-sms, OI-48 for iqama-check, and <see cref="ArgumentException"/> for any other kind.</summary>
    public Task<string> BuildLegacyDocument(long invNo, string kind, CancellationToken cancellationToken = default)
    {
        Record(nameof(BuildLegacyDocument), [invNo, kind]);
        throw kind switch
        {
            PatientCardKind or BarcodeSmsKind => Blocked(OpenItemIds.OI47, "PAT_CARD_INV.jsp"),
            IqamaCheckKind => Blocked(OpenItemIds.OI48, "iqama_check.jsp"),
            _ => new ArgumentException("Unknown legacy document kind.", nameof(kind)),
        };
    }

    public Task SendInvoiceSms(long invNo, CancellationToken cancellationToken = default)
    {
        Record(nameof(SendInvoiceSms), [invNo]);
        throw Blocked(OpenItemIds.OI12, "BIL_MESSAGE");
    }

    public Task TransferStock(long invNo, CancellationToken cancellationToken = default)
    {
        Record(nameof(TransferStock), [invNo]);
        throw Blocked(OpenItemIds.OI10, "BIL_STOCK_POSTING");
    }

    private static NotImplementedException Blocked(string openItemId, string dependency) =>
        new($"{openItemId}: {dependency} is not available in this build.");

    private void Record(string method, IReadOnlyList<object?> args) =>
        FakeRecorder.Record(_journal, Calls, nameof(ILegacyExternalCalls), method, args);
}

/// <summary>In-memory <see cref="IPackageConsumptionGateway"/> whose start throws OI-31.</summary>
public sealed class FakePackageConsumptionGateway : IPackageConsumptionGateway
{
    private readonly List<string> _journal;

    /// <summary>Creates the gateway writing its calls to <paramref name="journal"/>.</summary>
    public FakePackageConsumptionGateway(List<string> journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        _journal = journal;
    }

    /// <summary>Recorded calls.</summary>
    public List<FakeCall> Calls { get; } = new();

    public Task Begin(InvoiceEntryParameters parameters, CancellationToken cancellationToken = default)
    {
        FakeRecorder.Record(_journal, Calls, nameof(IPackageConsumptionGateway), nameof(Begin), [parameters]);
        throw new NotImplementedException($"{OpenItemIds.OI31}: package consumption is not available in this build.");
    }
}

/// <summary>The ten Data port fakes sharing one call journal, and the <see cref="InvoiceWorkflowService"/> built over them.</summary>
public sealed class FakeDataPorts
{
    /// <summary>Creates every fake over <see cref="Journal"/>.</summary>
    public FakeDataPorts()
    {
        SessionFactory = new FakeOracleSessionFactory(Journal);
        Lookups = new FakeLookupQueries(Journal);
        Invoices = new FakeInvoiceQueries(Journal);
        Lovs = new FakeLovQueries(Journal);
        InvoiceApi = new FakeBilInvoiceApiGateway(Journal);
        Import = new FakeBilImportGateway(Journal);
        PatientTransfer = new FakePatientTransferCommand(Journal);
        Legacy = new FakeLegacyExternalCalls(Journal);
        PackageConsumption = new FakePackageConsumptionGateway(Journal);
    }

    /// <summary>Every port call and session event, as Interface.Method or IOracleSession.Event, in call order.</summary>
    public List<string> Journal { get; } = new();

    /// <summary>Session factory fake.</summary>
    public FakeOracleSessionFactory SessionFactory { get; }

    /// <summary>Lookup queries fake.</summary>
    public FakeLookupQueries Lookups { get; }

    /// <summary>Invoice queries fake.</summary>
    public FakeInvoiceQueries Invoices { get; }

    /// <summary>LOV queries fake.</summary>
    public FakeLovQueries Lovs { get; }

    /// <summary>BIL_INVOICE_API gateway fake.</summary>
    public FakeBilInvoiceApiGateway InvoiceApi { get; }

    /// <summary>BIL_IMPORT gateway fake.</summary>
    public FakeBilImportGateway Import { get; }

    /// <summary>Reception-transfer command fake.</summary>
    public FakePatientTransferCommand PatientTransfer { get; }

    /// <summary>Blocked legacy calls fake.</summary>
    public FakeLegacyExternalCalls Legacy { get; }

    /// <summary>Blocked package-consumption gateway fake.</summary>
    public FakePackageConsumptionGateway PackageConsumption { get; }

    /// <summary>Builds the workflow service over the fakes.</summary>
    public InvoiceWorkflowService CreateService() =>
        new(SessionFactory, Lookups, Invoices, Lovs, InvoiceApi, Import, PatientTransfer, Legacy, PackageConsumption);

    /// <summary>Recorded calls of <paramref name="method"/> across every fake, grouped by fake in constructor order.</summary>
    public IReadOnlyList<FakeCall> CallsTo(string method) =>
        new[]
            {
                SessionFactory.Calls,
                Lookups.Calls,
                Invoices.Calls,
                Lovs.Calls,
                InvoiceApi.Calls,
                Import.Calls,
                PatientTransfer.Calls,
                Legacy.Calls,
                PackageConsumption.Calls,
            }
            .SelectMany(calls => calls)
            .Where(call => string.Equals(call.Method, method, StringComparison.Ordinal))
            .ToArray();
}
