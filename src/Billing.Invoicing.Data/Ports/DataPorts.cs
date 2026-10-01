using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Data.Ports;

/// <summary>Unit of work over one Oracle connection; disposing it uncommitted attempts to roll back its local transaction.</summary>
public interface IOracleSession : IAsyncDisposable
{
    /// <summary>Commits the transaction.</summary>
    Task Commit(CancellationToken cancellationToken = default);

    /// <summary>Rolls back the whole transaction.</summary>
    Task Rollback(CancellationToken cancellationToken = default);

    /// <summary>Rolls back to a named savepoint; the transaction stays open.</summary>
    /// <param name="savepointName">Name of a savepoint set earlier with <see cref="Save(string, CancellationToken)"/>.</param>
    /// <param name="cancellationToken">Cancels the savepoint rollback.</param>
    Task Rollback(string savepointName, CancellationToken cancellationToken = default);

    /// <summary>Sets a named savepoint in the transaction.</summary>
    /// <param name="savepointName">Name of the savepoint.</param>
    /// <param name="cancellationToken">Cancels the savepoint request.</param>
    Task Save(string savepointName, CancellationToken cancellationToken = default);
}

/// <summary>Opens Oracle sessions.</summary>
public interface IOracleSessionFactory
{
    /// <summary>Opens a connection and begins a transaction on it.</summary>
    Task<IOracleSession> Open(CancellationToken cancellationToken = default);
}

/// <summary>Read-only record-group queries behind the served LOVs; each row is keyed by column name.</summary>
public interface ILovQueries
{
    /// <summary>Rows of the COMPANY1_2 LOV for an information centre.</summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> Company(string infoCenterId, CancellationToken cancellationToken = default);

    /// <summary>Rows of the SUB_COMPANY LOV for a company, limited to the companies of an information centre.</summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SubCompany(string compCode, string infoCenterId, CancellationToken cancellationToken = default);

    /// <summary>Rows of the THE_CLASS LOV for a sub-company, limited to the sub-companies of an information centre's companies.</summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TheClass(string subCompCode, string infoCenterId, CancellationToken cancellationToken = default);

    /// <summary>Rows of the PAY_TYPE1 and PAY_TYPE2 LOVs.</summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PayTypes(CancellationToken cancellationToken = default);

    /// <summary>Rows of the DOC LOV for an information centre.</summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> Doc(string infoCenterId, CancellationToken cancellationToken = default);

    /// <summary>Rows of the view-only RESERV_NO LOV for a date, doctor and patient, limited to the active doctors of an information centre.</summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReservNo(DateTime invDate, int docId, string patientNo, string infoCenterId, CancellationToken cancellationToken = default);

    /// <summary>Rows of the OFFERS LOV for a pay type, date and information centre.</summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> Offers(int payType, DateTime invDate, string infoCenterId, CancellationToken cancellationToken = default);

    /// <summary>Rows of the CAT LOV.</summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> Cat(CancellationToken cancellationToken = default);
}

/// <summary>Read-only lookups that fill the Domain snapshots and preferences.</summary>
public interface ILookupQueries
{
    /// <summary>PREF values keyed by preference number (960, 970, 422); a missing number is absent.</summary>
    Task<IReadOnlyDictionary<int, string?>> GetPreferences(CancellationToken cancellationToken = default);

    /// <summary>Coverage snapshot of the patient, or null when none exists.</summary>
    Task<PatientCoverageSnapshot?> GetPatientCoverage(string patientNo, CancellationToken cancellationToken = default);

    /// <summary>Clinic profile with the patient's sex, or null when the clinic does not exist.</summary>
    Task<ClinicProfile?> GetClinicProfile(int clinicId, string? patientNo, CancellationToken cancellationToken = default);

    /// <summary>Service flags on a price list, or null when the service is not on it.</summary>
    Task<ServiceProfile?> GetServiceProfile(string serviceId, decimal listId, CancellationToken cancellationToken = default);

    /// <summary>Service flags by service id on a price list; ids not on it are absent.</summary>
    Task<IReadOnlyDictionary<string, ServiceProfile>> GetServiceProfiles(IReadOnlyCollection<string> serviceIds, decimal listId, CancellationToken cancellationToken = default);

    /// <summary>Service flags of every component of a package on a price list.</summary>
    Task<IReadOnlyList<ServiceProfile>> GetPackageComponentFlags(string packageServiceId, decimal listId, CancellationToken cancellationToken = default);

    /// <summary>Maximum final-discount percent of a user; 0 when unset.</summary>
    Task<decimal> GetUserMaxDiscount(int userNo, CancellationToken cancellationToken = default);

    /// <summary>Company type of a company, or null.</summary>
    Task<int?> GetCompanyType(string compCode, CancellationToken cancellationToken = default);

    /// <summary>IS_DIRECT flag of a company, or null.</summary>
    Task<int?> GetCompanyIsDirect(string compCode, CancellationToken cancellationToken = default);

    /// <summary>Doctor of a visit, or null.</summary>
    Task<int?> GetVisitDoctor(string visitUnique, CancellationToken cancellationToken = default);

    /// <summary>Service ids requested for a claim on a price list.</summary>
    Task<IReadOnlyList<string>> GetRequestedServices(string claimNo, decimal listId, CancellationToken cancellationToken = default);

    /// <summary>Current database time (SYSDATE).</summary>
    Task<DateTime> GetDatabaseTime(CancellationToken cancellationToken = default);

    /// <summary>Invoice types for the invoice-type list.</summary>
    Task<IReadOnlyList<(string Id, string Description)>> GetInvoiceTypes(CancellationToken cancellationToken = default);

    /// <summary>Currencies for the currency list.</summary>
    Task<IReadOnlyList<(string Code, string Name)>> GetCurrencies(CancellationToken cancellationToken = default);

    /// <summary>Cash-card id of the patient, or null.</summary>
    Task<int?> GetPatientCardId(string patientNo, CancellationToken cancellationToken = default);

    /// <summary>ADD_TO_QUE flag by service id on a price list; ids without a row are absent.</summary>
    Task<IReadOnlyDictionary<string, int>> GetServiceQueueFlags(IReadOnlyCollection<string> serviceIds, decimal listId, CancellationToken cancellationToken = default);

    /// <summary>DISC_CLASSES.USE_ADVANCED of a class, or null.</summary>
    Task<int?> GetClassAdvancedMode(string subCompCode, string classCode, CancellationToken cancellationToken = default);
}

/// <summary>Read-only queries over saved invoices, request rows, claim preloads and create requests.</summary>
public interface IInvoiceQueries
{
    /// <summary>Saved invoice header, lines and display values keyed by column name, or null when not found.</summary>
    /// <param name="invNo">Invoice number.</param>
    /// <param name="localDocType">LOCAL_DOC_TYPE of the request, mapped to the required ROW_TYPE filter: 532 or 505 filters ROW_TYPE 1, 783 filters ROW_TYPE 2, any other value is rejected.</param>
    /// <param name="cancellationToken">Cancels the connection open and the reads.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="localDocType"/> is not 505, 532 or 783.</exception>
    /// <exception cref="InvalidCastException">The saved invoice has no INVDATE, or a whole-number column holds a fractional number.</exception>
    /// <exception cref="OverflowException">A whole-number column holds a number outside the Int32 or Int64 range.</exception>
    Task<(InvoiceHeaderDraft Header, IReadOnlyList<InvoiceLineDraft> Lines, IReadOnlyDictionary<string, object?> Display)?> GetInvoice(long invNo, int localDocType, CancellationToken cancellationToken = default);

    /// <summary>More-details header, line and transfer rows of a saved invoice keyed by column name, or null when not found.</summary>
    /// <param name="invNo">Invoice number.</param>
    /// <param name="localDocType">LOCAL_DOC_TYPE of the request, mapped to the required ROW_TYPE filter: 532 or 505 filters ROW_TYPE 1, 783 filters ROW_TYPE 2, any other value is rejected.</param>
    /// <param name="cancellationToken">Cancels the connection open and the reads.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="localDocType"/> is not 505, 532 or 783.</exception>
    Task<(IReadOnlyDictionary<string, object?> Header, IReadOnlyList<IReadOnlyDictionary<string, object?>> Lines, IReadOnlyList<IReadOnlyDictionary<string, object?>> Transfers)?> GetMoreDetails(long invNo, int localDocType, CancellationToken cancellationToken = default);

    /// <summary>Last invoice number of an information centre, or null.</summary>
    Task<long?> GetLastInvoiceNo(string infoCenterId, CancellationToken cancellationToken = default);

    /// <summary>Request rows selected for invoicing on a visit and pay type.</summary>
    Task<IReadOnlyList<(long PatServReqRowId, string ServiceId, int? ReqAStatus, int? ReqNeedA, string? ApprovRefNo)>> GetSelectedRequestRows(string patientNo, string visitUnique, int payType, CancellationToken cancellationToken = default);

    /// <summary>Header preload from the claim's first invoice with its price list, deductible and card id, or null.</summary>
    Task<(InvoiceHeaderDraft Header, decimal? ListId, decimal? MaxDeductable, int? CardId)?> GetClaimPreload(string claimNo, CancellationToken cancellationToken = default);

    /// <summary>Recorded create request for a request id with its invoice's INVDATE, COMP_CODE, SUB_COMP_CODE and clinic age-limit flag, or null when none exists.</summary>
    Task<(long? InvNo, string? PatientNo, DateTimeOffset? CompletedAt, DateTime? InvDate, string? CompCode, string? SubCompCode, bool ClinicHasAgeLimit)?> GetCreateRequest(string requestId, CancellationToken cancellationToken = default);
}

/// <summary>Calls to BIL_INVOICE_API operations.</summary>
public interface IBilInvoiceApiGateway
{
    /// <summary>Most engine lines, rebuilt bundle parents included, that a create accepts and a preview returns.</summary>
    int MaxDraftLines { get; }

    /// <summary>Seal of the request id and draft date under the configured draft-seal key; equal on every instance sharing that key.</summary>
    string SealDraftDate(string requestId, DateTime draftDate);

    /// <summary>Expands bundled offers and calculates the editable invoice preview.</summary>
    /// <param name="session">Open session whose transaction the call runs in.</param>
    /// <param name="header">Invoice header draft.</param>
    /// <param name="lines">Visible draft lines in bind order.</param>
    /// <param name="operatorContext">Operator bound into the header.</param>
    /// <param name="amount1Auto">Whether the package fills amount 1 automatically.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<(IReadOnlyList<EditablePreviewLine> Lines, PreviewTotalsRow Totals)> CalculatePreview(IOracleSession session, InvoiceHeaderDraft header, IReadOnlyList<InvoiceLineDraft> lines, OperatorContext operatorContext, bool amount1Auto, CancellationToken cancellationToken = default);

    /// <summary>Expands bundled offers and saves the invoice with its posting stages; with no lines CREATE_FULL_INVOICE runs without expansion, so a recorded request id returns its existing invoice.</summary>
    /// <param name="session">Open session whose transaction the call runs in; the caller commits or rolls it back.</param>
    /// <param name="header">Invoice header draft; its amounts and sub pay types are passed to the create call.</param>
    /// <param name="lines">Visible draft lines in bind order; when empty, expansion is skipped.</param>
    /// <param name="operatorContext">Operator bound into the header.</param>
    /// <param name="requestId">Idempotency request id of the draft.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<FullInvoiceResultRow> CreateFullInvoice(IOracleSession session, InvoiceHeaderDraft header, IReadOnlyList<InvoiceLineDraft> lines, OperatorContext operatorContext, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Component lines of a bundled offer for the draft's patient and pay type.</summary>
    Task<IReadOnlyList<EditablePreviewLine>> GetBundledOfferLines(IOracleSession session, InvoiceHeaderDraft header, OperatorContext operatorContext, decimal offerId, decimal bundleQty, CancellationToken cancellationToken = default);

    /// <summary>Package component lines converted to engine lines, with the import result.</summary>
    Task<(IReadOnlyList<EngineLineInput> Lines, ImportResultRow Result)> GetPackageLines(IOracleSession session, string packageServiceId, decimal listId, string? parentSourceId, CancellationToken cancellationToken = default);

    /// <summary>Print URL of a saved invoice.</summary>
    string BuildPrintUrl(long invNo);
}

/// <summary>Calls to BIL_IMPORT operations.</summary>
public interface IBilImportGateway
{
    /// <summary>Selects request rows, imports them as engine lines and clears the selection.</summary>
    /// <param name="session">Open session whose transaction the calls run in; the caller commits or rolls it back.</param>
    /// <param name="header">Draft header supplying the patient, pay type and draft date.</param>
    /// <param name="operatorContext">Operator supplying the selection session id and user name.</param>
    /// <param name="visitUnique">Visit the request rows belong to.</param>
    /// <param name="patServReqRowIds">PAT_SERV_REQ row ids to select for import.</param>
    /// <param name="approvalMode">Approval check mode passed to the package: 0 imports rows needing approval; 1 skips credit rows needing approval without a reference.</param>
    /// <param name="cancellationToken">Cancels the database call.</param>
    Task<(IReadOnlyList<EngineLineInput> Lines, ImportResultRow Result)> ImportRequestLines(IOracleSession session, InvoiceHeaderDraft header, OperatorContext operatorContext, string visitUnique, IReadOnlyList<long> patServReqRowIds, int approvalMode, CancellationToken cancellationToken = default);

    /// <summary>Approval check mode for an X422_APPROV_CHECK value.</summary>
    /// <returns>0 when the value is 2; otherwise 1.</returns>
    int ApprovalCheckMode(int? x422ApprovCheck);

    /// <summary>Consultation or review visit line as an engine line, with the import result.</summary>
    Task<(EngineLineInput? Line, ImportResultRow Result)> GetVisitLine(IOracleSession session, InvoiceHeaderDraft header, OperatorContext operatorContext, VisitLineChoice choice, CancellationToken cancellationToken = default);
}

/// <summary>Update of the patient's reception-transfer fields.</summary>
public interface IPatientTransferCommand
{
    /// <summary>Clears the patient's NEW_INV_* reception-transfer fields when set.</summary>
    /// <returns>Rows affected.</returns>
    Task<int> ClearReceptionTransfer(IOracleSession session, string patientNo, CancellationToken cancellationToken = default);
}

/// <summary>Legacy database functions, reports and messaging absent from this build.</summary>
public interface ILegacyExternalCalls
{
    /// <summary>Validates the totals of a saved invoice inside the open session.</summary>
    Task ValidateTotalInvoice(IOracleSession session, long invNo, CancellationToken cancellationToken = default);

    /// <summary>Pre-authorisation of the patient.</summary>
    Task<string?> GetPreAuthorization(string patientNo, CancellationToken cancellationToken = default);

    /// <summary>Age of the patient in years at a date.</summary>
    Task<decimal> ComputePatientAgeYears(string patientNo, DateTime asOf, CancellationToken cancellationToken = default);

    /// <summary>Amount paid before on the claim.</summary>
    Task<decimal> GetPaidBefore(string? compCode, string? subCompCode, string? claimNo, CancellationToken cancellationToken = default);

    /// <summary>Price plan and price list for an information centre and company.</summary>
    Task<(decimal? PlanCode, decimal? ListId)> ResolvePricePlan(string infoCenterId, string? compCode, string? subCompCode, CancellationToken cancellationToken = default);

    /// <summary>Legacy document URL of a saved invoice.</summary>
    /// <param name="invNo">Invoice number.</param>
    /// <param name="kind">Document kind, for example patient-card, barcode-sms or iqama-check.</param>
    /// <param name="cancellationToken">Cancellation token for the call.</param>
    Task<string> BuildLegacyDocument(long invNo, string kind, CancellationToken cancellationToken = default);

    /// <summary>Sends the invoice SMS.</summary>
    Task SendInvoiceSms(long invNo, CancellationToken cancellationToken = default);

    /// <summary>Transfers stock for a saved invoice.</summary>
    Task TransferStock(long invNo, CancellationToken cancellationToken = default);
}

/// <summary>Package-consumption invoicing.</summary>
public interface IPackageConsumptionGateway
{
    /// <summary>Starts package consumption for the entry parameters.</summary>
    Task Begin(InvoiceEntryParameters parameters, CancellationToken cancellationToken = default);
}
