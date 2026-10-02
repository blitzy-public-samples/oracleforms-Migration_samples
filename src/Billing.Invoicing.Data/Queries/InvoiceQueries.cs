using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Domain.Rules;
using Dapper;

namespace Billing.Invoicing.Data.Queries;

/// <summary>Read-only queries over saved invoices, selected request rows, claim preloads and create requests. UNVERIFIED against Oracle.</summary>
public sealed class InvoiceQueries : IInvoiceQueries
{
    /// <summary>Saved invoice header with its persisted display columns and the T010 display lookups, in the T_INV block's INV_NO order; binds :invNo and :rowType.</summary>
    public const string GetInvoiceSql =
        "SELECT t.INV_NO, t.PATIENTNO, t.INVDATE, t.INVTYPEID, t.PAYTYPE, t.SUB_PAYTYPE, t.SUB_PAYTYPE2, t.CLINICID, t.DOCID, t.CURR_CODE, "
        + "t.PRE_AUTHORIZATION, t.CLAIM_NO, t.CLAIM_FLAG, t.NOTE_NO, t.FINALDISC_PERC, t.FINALDISC, t.AMOUNT_1, t.AMOUNT_2, t.ADD_TO_LIST, "
        + "t.USER_NO, t.MACHINE_N, t.INFO_CENTER_ID, t.DEPT_WISE, t.CALL, t.DISC_T, t.CASH_PAYED, t.COMP_CODE, t.SUB_COMP_CODE, t.CLASS_CODE, "
        + "t.DOCID1, t.SEQ_NO, "
        + "t.PATIENTNAME, t.INV_TIME, t.CARD_ID, t.LIST_ID, t.PLAN_CODE, t.UPD_USER_NO, t.ROW_TYPE, t.PFLAG, t.PAT_PAY, t.COMP_PAY, "
        + "t.VAT_TOTAL_PAT, t.VAT_TOTAL_CO, t.VAT_TOTAL, t.CASH_COLLECTED, t.REUND, t.MAX_DEDUCTABLE, t.RESERV_THE_TIME, t.INS_NUMBER, "
        + "t.CARD_END, t.PAT_POLICY_NO, "
        + "(SELECT CASE WHEN COUNT(*) = 1 THEN MAX(x.U_NAME) END FROM USERS_TABLE x WHERE x.USER_NO = t.USER_NO) AS USER_NAME_TO_SHOW, "
        + "(SELECT CASE WHEN COUNT(*) = 1 THEN MAX(x.PAY_TYPE_NAME_AR) END FROM PAY_TYPES x WHERE x.PAY_TYPE_ID = t.SUB_PAYTYPE) AS SUB_PAYTYPE_NAME, "
        + "(SELECT CASE WHEN COUNT(*) = 1 THEN MAX(x.PAY_TYPE_NAME_AR) END FROM PAY_TYPES x WHERE x.PAY_TYPE_ID = t.SUB_PAYTYPE2) AS SUB_PAYTYPE2_NAME, "
        + "(SELECT CASE WHEN COUNT(*) = 1 THEN MAX(x.U_NAME) END FROM USERS_TABLE x WHERE x.USER_NO = t.UPD_USER_NO) AS EDIT_USER_NAME_TO_SHOW, "
        + "(SELECT CASE WHEN COUNT(*) = 1 THEN MAX(x.CLINICNAME) END FROM CLINICS x WHERE x.CLINICID = t.CLINICID) AS CLINICNAME, "
        + "(SELECT CASE WHEN COUNT(*) = 1 THEN MAX(x.DOC_NAME) END FROM DOCTORS x WHERE x.DOCID = t.DOCID) AS DOC_NAME, "
        + "(SELECT CASE WHEN COUNT(*) = 1 THEN MAX(x.DOC_NAME) END FROM DOCTORS x WHERE x.DOCID = t.DOCID1) AS DOC_NAME1, "
        + "(SELECT CASE WHEN COUNT(*) = 1 THEN MAX(x.PLAN_NAME) END FROM PRICE_PLAN_M x WHERE x.PLAN_CODE = t.PLAN_CODE) AS PLAN_NAME, "
        + "(SELECT CASE WHEN COUNT(*) = 1 THEN MAX(x.LIST_NAME) END FROM PRICE_LIST_MASTER x WHERE x.LIST_ID = t.LIST_ID) AS LIST_NAME, "
        + "(SELECT CASE WHEN COUNT(*) = 1 THEN MAX(x.COMP_NAME) END FROM COMPANYS x WHERE x.COMP_CODE = t.COMP_CODE) AS COMP_NAME, "
        + "(SELECT CASE WHEN COUNT(*) = 1 THEN MAX(x.COMP_NAME) END FROM COMPANYS x WHERE x.COMP_CODE = t.SUB_COMP_CODE) AS SUB_COMP_NAME, "
        + "(SELECT CASE WHEN COUNT(*) = 1 THEN MAX(x.CLASS_NAME) END FROM DISC_CLASSES x WHERE x.COMP_CODE = t.SUB_COMP_CODE AND x.CLASS_CODE = t.CLASS_CODE) AS CLASS_NAME, "
        + "(SELECT CASE WHEN COUNT(*) = 1 THEN MAX(g.COMP_NAME) END FROM COMPANYS g WHERE g.COMP_CODE IN (SELECT y.XGROUP FROM COMPANYS y WHERE y.COMP_CODE = t.SUB_COMP_CODE)) AS G_NAME, "
        + "(SELECT CASE WHEN COUNT(*) = 1 THEN MAX(x.CARD_NAME) END FROM CASH_CARD_DISC x WHERE x.CARD_ID = t.CARD_ID) AS CARD_NAME "
        + "FROM T_INV t "
        + "WHERE (t.INVTYPEID <> 8 and t.INVTYPEID <> 9) and t.PHARMACY_INV_NO is null AND t.INV_NO = :invNo AND t.ROW_TYPE = :rowType "
        + "ORDER BY t.INV_NO";

    /// <summary>Lines of a saved invoice in D_INV_ROW_ID order, the row id returned as text; binds :invNo.</summary>
    public const string GetInvoiceLinesSql =
        "SELECT TO_CHAR(d.D_INV_ROW_ID) AS D_INV_ROW_ID, d.SERVICEID, d.SERVICEDESC, d.CATID, d.XCAT_NAMEX, d.PRICE, d.QTY, d.DISC, d.MY_DISC, d.FIXPAY, d.PAYRATE, "
        + "d.TEETH_NO, d.TOOTH_SURFACE, d.TEETH_NO2, d.PAT_SERV_REQ_ROW_ID, d.APPROV_DATE, d.APPROV_VALIDITY, d.APPROV_REF_NO, "
        + "d.CLAIM_NO, d.REQ_NEED_A, d.REQ_A_STATUS, d.LIST_ID, d.CURR_CODE, d.MY_PRICE, d.MY_NET, d.THE_PAY, d.THE_COMP, d.THE_FIX, d.THE_RATE, "
        + "d.VAT_RATE, d.VAT_VAL_CO, d.VAT_VAL_PAT, d.VAT_VAL_PAT_EX, d.REGULAR_LENSES_TYPE, d.LENS_SPECIFICATIONS, d.CONTACT_LENSES_TYPE, "
        + "d.F_L_INDICATOR, d.NUMBER_OF_PAIRS, d.INS_EMP, "
        + "d.PACKAGE_SERVICE_ID, d.PACKAGE_INSTANCE_ID, d.PACKAGE_LINE_ROLE, d.PACKAGE_COMPONENT_ORDER, d.PACKAGE_PARENT_LINE_ID, d.PACKAGE_PRICING_METHOD, "
        + "d.OFFER_ID, d.OFFER_DTL_ID, d.OFFER_TYPE, d.OFFER_INSTANCE_ID, d.OFFER_LINE_ROLE, d.OFFER_PARENT_LINE_ID, d.OFFER_PRICE_APPLIED, "
        + "d.OFFER_DIS_APPLIED, d.OFFER_NAME_SNAPSHOT, d.OFFER_OBJECT_VERSION_NUMBER, d.OFFER_DTL_OBJECT_VERSION_NUMBER "
        + "FROM D_INV d WHERE d.INV_NO = :invNo ORDER BY d.D_INV_ROW_ID";

    /// <summary>Gross, discount and net totals of a saved invoice's lines that are not flagged deleted, each 0 when none; binds :invNo.</summary>
    public const string GetInvoiceTotalsSql =
        "SELECT COALESCE(SUM(COALESCE(d.MY_PRICE, 0)), 0) AS TOTAL_GROSS, COALESCE(SUM(COALESCE(d.MY_DISC, 0)), 0) AS TOTAL_DISCOUNT, "
        + "COALESCE(SUM(COALESCE(d.MY_NET, 0)), 0) AS TOTAL_NET "
        + "FROM D_INV d WHERE d.INV_NO = :invNo AND COALESCE(d.IS_DELETED, 0) = 0";

    /// <summary>More-details insurance header of a saved invoice; binds :invNo and :rowType.</summary>
    public const string GetMoreDetailsSql =
        "SELECT t.INV_NO, t.INS_NUMBER, t.CARD_END, t.PAT_POLICY_NO FROM T_INV t "
        + "WHERE (t.INVTYPEID <> 8 and t.INVTYPEID <> 9) and t.PHARMACY_INV_NO is null AND t.INV_NO = :invNo AND t.ROW_TYPE = :rowType";

    /// <summary>More-details dental, lens, approval and insurance-employee fields of each saved line in D_INV_ROW_ID order, the row id returned as text; binds :invNo.</summary>
    public const string GetMoreDetailsLinesSql =
        "SELECT TO_CHAR(d.D_INV_ROW_ID) AS D_INV_ROW_ID, d.SERVICEID, d.TEETH_NO, d.TOOTH_SURFACE, d.REGULAR_LENSES_TYPE, d.LENS_SPECIFICATIONS, d.CONTACT_LENSES_TYPE, "
        + "d.F_L_INDICATOR, d.NUMBER_OF_PAIRS, d.APPROV_DATE, d.APPROV_VALIDITY, d.APPROV_REF_NO, d.REQ_NEED_A, d.REQ_A_STATUS, d.INS_EMP, "
        + "(SELECT CASE WHEN COUNT(*) = 1 THEN MAX(e.EMP_NAME_EN) END FROM EMP e WHERE e.EMP_ID = d.INS_EMP) AS INS_EMP_NAME "
        + "FROM D_INV d WHERE d.INV_NO = :invNo ORDER BY d.D_INV_ROW_ID";

    /// <summary>Store-transfer rows imported from a saved invoice with INV_TYPE '197'; binds :invNo.</summary>
    public const string GetMoreDetailsTransfersSql =
        "SELECT m.TRANS_M_ROW_ID, m.IMP_FROM_T_INV_NO, m.INV_TYPE, m.THE_YEAR, m.INFO_CENTER_ID FROM TRANS_M m "
        + "WHERE m.IMP_FROM_T_INV_NO = :invNo AND m.INV_TYPE = '197'";

    /// <summary>Highest invoice number with IN_OUT 1, ROW_TYPE 1 and no pharmacy invoice at an information centre; binds :infoCenterId.</summary>
    public const string GetLastInvoiceNoSql =
        "SELECT MAX(INV_NO) FROM T_INV WHERE IN_OUT = 1 AND ROW_TYPE = 1 AND PHARMACY_INV_NO IS NULL AND INFO_CENTER_ID = :infoCenterId";

    /// <summary>Uninvoiced request rows flagged for invoicing on a patient's visit and pay type; binds :patientNo, :visitUnique and :payType.</summary>
    public const string GetSelectedRequestRowsSql =
        "SELECT PAT_SERV_REQ_ROW_ID, SERVICEID, REQ_A_STATUS, REQ_NEED_A, APPROV_REF_NO FROM V_SERVICES_REQ "
        + "WHERE PATIENTNO = :patientNo AND D_INV_ROW_ID IS NULL AND SELECT_TO_INV = 1 AND VISIT_UNIQUE = :visitUnique "
        + "AND DECODE(PAY_TYPE,'Cash',1,'Credit',2) = :payType";

    /// <summary>Header and insurance card fields, price list, deductible and card id of a claim's first invoice; binds :claimNo.</summary>
    public const string GetClaimPreloadSql =
        "SELECT CLAIM_NO, PATIENTNO, CLINICID, COMP_CODE, SUB_COMP_CODE, CLASS_CODE, PAYTYPE, LIST_ID, MAX_DEDUCTABLE, CARD_ID, "
        + "INS_NUMBER, CARD_END, PAT_POLICY_NO FROM T_INV "
        + "WHERE INV_NO = (SELECT MIN(INV_NO) FROM T_INV WHERE CLAIM_NO = :claimNo AND :claimNo NOT IN ('1','2'))";

    /// <summary>Invoice number, patient and completion time recorded for a create request id, with the recorded invoice's date, company, sub-company and whether its clinic has an age limit; binds :requestId.</summary>
    public const string GetCreateRequestSql =
        "SELECT r.INV_NO, r.PATIENTNO, r.COMPLETED_AT, t.INVDATE, t.COMP_CODE, t.SUB_COMP_CODE, "
        + "CASE WHEN c.AGE_MIN IS NOT NULL OR c.AGE_MAX IS NOT NULL THEN 1 ELSE 0 END AS CLINIC_AGE_LIMIT "
        + "FROM BIL_INVOICE_CREATE_REQUEST r "
        + "LEFT JOIN T_INV t ON t.INV_NO = r.INV_NO "
        + "LEFT JOIN CLINICS c ON c.CLINICID = t.CLINICID "
        + "WHERE r.REQUEST_ID = :requestId";

    private const string LineDisplayKey = "LINE_DISPLAY";
    private const string OfferNameKey = "OFFER_NAME";
    private const string PreAuthorizationKey = "PRE_AUTHORIZATION";
    private const string TotalCollectedKey = "TOTAL_COLLECTED";

    private readonly InvoicingDataOptions _options;

    /// <summary>Stores the data-layer settings; opens nothing.</summary>
    /// <param name="options">Connection string and command settings.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="InvoicingDataOptions.CommandTimeoutSeconds"/> is below 1 or above <see cref="InvoicingDataOptions.MaxCommandTimeoutSeconds"/>.</exception>
    public InvoiceQueries(InvoicingDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.EnsureCommandTimeout(nameof(options));

        _options = options;
    }

    /// <summary>Saved invoice header, lines and display values keyed by column name, or null when not found.</summary>
    /// <param name="invNo">Invoice number.</param>
    /// <param name="localDocType">LOCAL_DOC_TYPE of the request: 532 or 505 filters ROW_TYPE 1, 783 filters ROW_TYPE 2.</param>
    /// <param name="cancellationToken">Cancels the connection open and the reads.</param>
    /// <returns>The header, the lines in D_INV_ROW_ID order and the display values, or null.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="localDocType"/> is not 505, 532 or 783; no connection is opened.</exception>
    /// <exception cref="InvalidCastException">The saved invoice has no INVDATE, or a whole-number column holds a fractional number.</exception>
    /// <exception cref="OverflowException">A whole-number column holds a number outside the Int32 or Int64 range.</exception>
    public async Task<(InvoiceHeaderDraft Header, IReadOnlyList<InvoiceLineDraft> Lines, IReadOnlyDictionary<string, object?> Display)?> GetInvoice(
        long invNo,
        int localDocType,
        CancellationToken cancellationToken = default)
    {
        var rowType = RowTypeOf(localDocType);

        await using var connection = await OracleSessionFactory.OpenConnection(_options, cancellationToken).ConfigureAwait(false);

        return await ReadInvoice(connection, invNo, rowType, _options.CommandTimeoutSeconds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>More-details header, line and transfer rows of a saved invoice keyed by upper-case column name, or null when not found.</summary>
    /// <param name="invNo">Invoice number.</param>
    /// <param name="localDocType">LOCAL_DOC_TYPE of the request: 532 or 505 filters ROW_TYPE 1, 783 filters ROW_TYPE 2.</param>
    /// <param name="cancellationToken">Cancels the connection open and the reads.</param>
    /// <returns>The header row, the line rows in D_INV_ROW_ID order and the transfer rows, or null.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="localDocType"/> is not 505, 532 or 783; no connection is opened.</exception>
    public async Task<(IReadOnlyDictionary<string, object?> Header, IReadOnlyList<IReadOnlyDictionary<string, object?>> Lines, IReadOnlyList<IReadOnlyDictionary<string, object?>> Transfers)?> GetMoreDetails(
        long invNo,
        int localDocType,
        CancellationToken cancellationToken = default)
    {
        var rowType = RowTypeOf(localDocType);

        await using var connection = await OracleSessionFactory.OpenConnection(_options, cancellationToken).ConfigureAwait(false);

        return await ReadMoreDetails(connection, invNo, rowType, _options.CommandTimeoutSeconds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Last invoice number of an information centre, or null when it has none.</summary>
    /// <param name="infoCenterId">Information centre id of the operator.</param>
    /// <param name="cancellationToken">Cancels the connection open and the read.</param>
    /// <returns>The highest matching invoice number, or null.</returns>
    /// <exception cref="ArgumentException"><paramref name="infoCenterId"/> is null, empty or white space, or longer than 10 characters or UTF-8 bytes.</exception>
    public async Task<long?> GetLastInvoiceNo(string infoCenterId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(infoCenterId);
        BoundedVarchar2.Validate("infoCenterId", infoCenterId, BoundedVarchar2.InfoCenterIdBytes, nameof(infoCenterId));

        await using var connection = await OracleSessionFactory.OpenConnection(_options, cancellationToken).ConfigureAwait(false);

        return await ReadLastInvoiceNo(connection, infoCenterId, _options.CommandTimeoutSeconds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Request rows selected for invoicing on a visit and pay type.</summary>
    /// <param name="patientNo">Patient number.</param>
    /// <param name="visitUnique">VISIT_UNIQUE of the patient visit.</param>
    /// <param name="payType">Pay type: 1 matches 'Cash' rows, 2 matches 'Credit' rows.</param>
    /// <param name="cancellationToken">Cancels the connection open and the read.</param>
    /// <returns>Every matching row with its request row id, service id and approval fields.</returns>
    /// <exception cref="ArgumentException"><paramref name="patientNo"/> or <paramref name="visitUnique"/> is null, empty or white space, <paramref name="patientNo"/> is longer than 12 characters or UTF-8 bytes, or <paramref name="visitUnique"/> is longer than 39 characters or UTF-8 bytes.</exception>
    /// <exception cref="InvalidCastException">A selected row has no PAT_SERV_REQ_ROW_ID or SERVICEID, or a non-integral number.</exception>
    /// <exception cref="OverflowException">A selected row holds a number outside the Int32 or Int64 range.</exception>
    public async Task<IReadOnlyList<(long PatServReqRowId, string ServiceId, int? ReqAStatus, int? ReqNeedA, string? ApprovRefNo)>> GetSelectedRequestRows(
        string patientNo,
        string visitUnique,
        int payType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(patientNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(visitUnique);
        BoundedVarchar2.Validate("patientNo", patientNo, BoundedVarchar2.PatientNoBytes, nameof(patientNo));
        BoundedVarchar2.Validate("visitUnique", visitUnique, BoundedVarchar2.VisitUniqueBytes, nameof(visitUnique));

        await using var connection = await OracleSessionFactory.OpenConnection(_options, cancellationToken).ConfigureAwait(false);

        return await ReadSelectedRequestRows(connection, patientNo, visitUnique, payType, _options.CommandTimeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Header preload from the claim's first invoice with its price list, deductible and card id, or null.</summary>
    /// <param name="claimNo">Claim number; '1' and '2' never match.</param>
    /// <param name="cancellationToken">Cancels the connection open and the read.</param>
    /// <returns>A header carrying claim, patient, clinic, company, sub-company, class, pay type, insurance number, card end and policy number, with LIST_ID, MAX_DEDUCTABLE and CARD_ID; or null.</returns>
    /// <exception cref="ArgumentException"><paramref name="claimNo"/> is null, empty or white space, or longer than 40 characters or UTF-8 bytes.</exception>
    public async Task<(InvoiceHeaderDraft Header, decimal? ListId, decimal? MaxDeductable, int? CardId)?> GetClaimPreload(
        string claimNo,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimNo);
        BoundedVarchar2.Validate("claimNo", claimNo, BoundedVarchar2.ClaimNoBytes, nameof(claimNo));

        await using var connection = await OracleSessionFactory.OpenConnection(_options, cancellationToken).ConfigureAwait(false);

        return await ReadClaimPreload(connection, claimNo, _options.CommandTimeoutSeconds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Recorded create request for a request id with its invoice's INVDATE, COMP_CODE, SUB_COMP_CODE and clinic age-limit flag, or null when none exists.</summary>
    /// <param name="requestId">Idempotency request id of the draft.</param>
    /// <param name="cancellationToken">Cancels the connection open and the read.</param>
    /// <returns>The recorded invoice number, patient and completion time with the invoice's date, company, sub-company and clinic age-limit flag, or null; the invoice fields are null and the flag false while no invoice row matches.</returns>
    /// <exception cref="ArgumentException"><paramref name="requestId"/> is null, empty or white space, or longer than 64 characters or UTF-8 bytes.</exception>
    public async Task<(long? InvNo, string? PatientNo, DateTimeOffset? CompletedAt, DateTime? InvDate, string? CompCode, string? SubCompCode, bool ClinicHasAgeLimit)?> GetCreateRequest(
        string requestId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        BoundedVarchar2.Validate("requestId", requestId, BoundedVarchar2.RequestIdBytes, nameof(requestId));

        await using var connection = await OracleSessionFactory.OpenConnection(_options, cancellationToken).ConfigureAwait(false);

        return await ReadCreateRequest(connection, requestId, _options.CommandTimeoutSeconds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads a saved invoice header of the ROW_TYPE, then its lines and line totals when the header exists.</summary>
    private static async Task<(InvoiceHeaderDraft Header, IReadOnlyList<InvoiceLineDraft> Lines, IReadOnlyDictionary<string, object?> Display)?> ReadInvoice(
        DbConnection connection,
        long invNo,
        int rowType,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var headerParameters = InvoiceNumberParameters(invNo);
        headerParameters.Add("rowType", rowType, DbType.Int32);

        var header = await connection.QueryFirstOrDefaultAsync<InvoiceHeaderRow>(
            Command(GetInvoiceSql, headerParameters, commandTimeoutSeconds, cancellationToken)).ConfigureAwait(false);
        if (header is null)
        {
            return null;
        }

        var lines = (await connection.QueryAsync<InvoiceLineRow>(
            Command(GetInvoiceLinesSql, InvoiceNumberParameters(invNo), commandTimeoutSeconds, cancellationToken)).ConfigureAwait(false)).AsList();
        var totals = await connection.QuerySingleAsync<InvoiceTotalsRow>(
            Command(GetInvoiceTotalsSql, InvoiceNumberParameters(invNo), commandTimeoutSeconds, cancellationToken)).ConfigureAwait(false);

        var drafts = new InvoiceLineDraft[lines.Count];
        var lineDisplay = new IReadOnlyDictionary<string, object?>[lines.Count];
        for (var index = 0; index < lines.Count; index++)
        {
            drafts[index] = ToLineDraft(lines[index]);
            lineDisplay[index] = ToLineDisplay(lines[index]);
        }

        return (ToHeaderDraft(header), drafts, ToDisplay(header, lineDisplay, totals));
    }

    /// <summary>Reads the more-details header row of the ROW_TYPE, then the line and transfer rows when the header exists.</summary>
    private static async Task<(IReadOnlyDictionary<string, object?> Header, IReadOnlyList<IReadOnlyDictionary<string, object?>> Lines, IReadOnlyList<IReadOnlyDictionary<string, object?>> Transfers)?> ReadMoreDetails(
        DbConnection connection,
        long invNo,
        int rowType,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var headerParameters = InvoiceNumberParameters(invNo);
        headerParameters.Add("rowType", rowType, DbType.Int32);

        var header = await connection.QueryFirstOrDefaultAsync<object>(
            Command(GetMoreDetailsSql, headerParameters, commandTimeoutSeconds, cancellationToken)).ConfigureAwait(false);
        if (header is null)
        {
            return null;
        }

        var lines = await connection.QueryAsync<object>(
            Command(GetMoreDetailsLinesSql, InvoiceNumberParameters(invNo), commandTimeoutSeconds, cancellationToken)).ConfigureAwait(false);
        var transfers = await connection.QueryAsync<object>(
            Command(GetMoreDetailsTransfersSql, InvoiceNumberParameters(invNo), commandTimeoutSeconds, cancellationToken)).ConfigureAwait(false);

        return (ToRow(header), ToRows(lines), ToRows(transfers));
    }

    /// <summary>Reads the highest matching invoice number of an information centre.</summary>
    private static async Task<long?> ReadLastInvoiceNo(
        DbConnection connection,
        string infoCenterId,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        BoundedVarchar2.AddInput(parameters, "infoCenterId", infoCenterId, BoundedVarchar2.InfoCenterIdBytes, nameof(infoCenterId));

        var value = await connection.ExecuteScalarAsync<object?>(
            Command(GetLastInvoiceNoSql, parameters, commandTimeoutSeconds, cancellationToken)).ConfigureAwait(false);

        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    /// <summary>Reads the request rows selected for invoicing on a visit and pay type.</summary>
    private static async Task<IReadOnlyList<(long PatServReqRowId, string ServiceId, int? ReqAStatus, int? ReqNeedA, string? ApprovRefNo)>> ReadSelectedRequestRows(
        DbConnection connection,
        string patientNo,
        string visitUnique,
        int payType,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        BoundedVarchar2.AddInput(parameters, "patientNo", patientNo, BoundedVarchar2.PatientNoBytes, nameof(patientNo));
        BoundedVarchar2.AddInput(parameters, "visitUnique", visitUnique, BoundedVarchar2.VisitUniqueBytes, nameof(visitUnique));
        parameters.Add("payType", payType, DbType.Int32);

        var rows = await connection.QueryAsync<RequestRow>(
            Command(GetSelectedRequestRowsSql, parameters, commandTimeoutSeconds, cancellationToken)).ConfigureAwait(false);

        return rows.Select(ToSelectedRequestRow).ToArray();
    }

    /// <summary>Reads the first invoice of a claim.</summary>
    private static async Task<(InvoiceHeaderDraft Header, decimal? ListId, decimal? MaxDeductable, int? CardId)?> ReadClaimPreload(
        DbConnection connection,
        string claimNo,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        BoundedVarchar2.AddInput(parameters, "claimNo", claimNo, BoundedVarchar2.ClaimNoBytes, nameof(claimNo));

        var row = await connection.QueryFirstOrDefaultAsync<ClaimPreloadRow>(
            Command(GetClaimPreloadSql, parameters, commandTimeoutSeconds, cancellationToken)).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var header = new InvoiceHeaderDraft
        {
            ClaimNo = row.CLAIM_NO,
            PatientNo = row.PATIENTNO,
            ClinicId = ToInt32(row.CLINICID),
            CompCode = row.COMP_CODE,
            SubCompCode = row.SUB_COMP_CODE,
            ClassCode = ToInt32(row.CLASS_CODE),
            PayType = ToInt32(row.PAYTYPE),
            InsNumber = row.INS_NUMBER,
            CardEnd = row.CARD_END,
            PatPolicyNo = row.PAT_POLICY_NO,
        };

        return (header, row.LIST_ID, row.MAX_DEDUCTABLE, ToInt32(row.CARD_ID));
    }

    /// <summary>Reads the create-request row of a request id with its invoice and clinic columns.</summary>
    private static async Task<(long? InvNo, string? PatientNo, DateTimeOffset? CompletedAt, DateTime? InvDate, string? CompCode, string? SubCompCode, bool ClinicHasAgeLimit)?> ReadCreateRequest(
        DbConnection connection,
        string requestId,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        BoundedVarchar2.AddInput(parameters, "requestId", requestId, BoundedVarchar2.RequestIdBytes, nameof(requestId));

        var row = await connection.QueryFirstOrDefaultAsync<CreateRequestRow>(
            Command(GetCreateRequestSql, parameters, commandTimeoutSeconds, cancellationToken)).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        return (
            ToInt64(row.INV_NO),
            row.PATIENTNO,
            ToCompletedAt(row.COMPLETED_AT),
            row.INVDATE,
            row.COMP_CODE,
            row.SUB_COMP_CODE,
            ToInt32(row.CLINIC_AGE_LIMIT) == 1);
    }


    /// <summary>Command over the given text and parameters with the configured timeout and cancellation.</summary>
    private static CommandDefinition Command(string sql, DynamicParameters parameters, int commandTimeoutSeconds, CancellationToken cancellationToken) =>
        new(sql, parameters, commandTimeout: commandTimeoutSeconds, cancellationToken: cancellationToken);

    /// <summary>Parameters holding :invNo.</summary>
    private static DynamicParameters InvoiceNumberParameters(long invNo)
    {
        var parameters = new DynamicParameters();
        parameters.Add("invNo", invNo, DbType.Int64);
        return parameters;
    }

    /// <summary>ROW_TYPE of a LOCAL_DOC_TYPE: 532 or 505 gives 1, 783 gives 2.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="localDocType"/> is not 505, 532 or 783.</exception>
    private static int RowTypeOf(int localDocType) => localDocType switch
    {
        532 or 505 => 1,
        783 => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(localDocType), localDocType, "LOCAL_DOC_TYPE must be 505, 532 or 783."),
    };

    /// <summary>Header draft of a saved invoice row; the pre-authorisation and header offer stay null.</summary>
    /// <exception cref="InvalidCastException">INVDATE is null, or a whole-number column holds a fractional number.</exception>
    private static InvoiceHeaderDraft ToHeaderDraft(InvoiceHeaderRow row) => new()
    {
        PatientNo = row.PATIENTNO,
        InvDate = row.INVDATE,
        InvTypeId = ToInt32(row.INVTYPEID),
        PayType = ToInt32(row.PAYTYPE),
        SubPayType = ToInt32(row.SUB_PAYTYPE),
        SubPayType2 = ToInt32(row.SUB_PAYTYPE2),
        ClinicId = ToInt32(row.CLINICID),
        DocId = ToInt32(row.DOCID),
        CurrCode = row.CURR_CODE,
        ClaimNo = row.CLAIM_NO,
        ClaimFlag = row.CLAIM_FLAG,
        NoteNo = row.NOTE_NO,
        FinalDiscPerc = row.FINALDISC_PERC,
        FinalDisc = row.FINALDISC,
        Amount1 = row.AMOUNT_1,
        Amount2 = row.AMOUNT_2,
        AddToList = ToInt32(row.ADD_TO_LIST),
        UserNo = ToInt32(row.USER_NO),
        MachineN = row.MACHINE_N,
        InfoCenterId = row.INFO_CENTER_ID,
        DraftDate = row.INVDATE ?? throw new InvalidCastException("Column INVDATE of T_INV holds null."),
        InvNo = ToInt64(row.INV_NO),
        DeptWise = ToInt32(row.DEPT_WISE),
        Call = ToInt32(row.CALL),
        DiscT = ToInt32(row.DISC_T),
        CashPayed = row.CASH_PAYED,
        CompCode = row.COMP_CODE,
        SubCompCode = row.SUB_COMP_CODE,
        ClassCode = ToInt32(row.CLASS_CODE),
        DocId1 = ToInt32(row.DOCID1),
        SeqNo = ToInt32(row.SEQ_NO),
    };

    /// <summary>Line draft of a saved line row; the price override, override flag, discount type and definition token keep their defaults.</summary>
    private static InvoiceLineDraft ToLineDraft(InvoiceLineRow row) => new()
    {
        ServiceId = row.SERVICEID,
        Qty = row.QTY,
        Disc = row.DISC,
        MyDisc = row.MY_DISC,
        TeethNo = row.TEETH_NO,
        ToothSurface = row.TOOTH_SURFACE,
        TeethNo2 = row.TEETH_NO2,
        PatServReqRowId = ToInt64(row.PAT_SERV_REQ_ROW_ID),
        ApprovDate = row.APPROV_DATE,
        ApprovValidity = row.APPROV_VALIDITY,
        ApprovRefNo = row.APPROV_REF_NO,
        ClaimNo = row.CLAIM_NO,
        ReqNeedA = ToInt32(row.REQ_NEED_A),
        ReqAStatus = ToInt32(row.REQ_A_STATUS),
        PackageServiceId = row.PACKAGE_SERVICE_ID,
        PackageInstanceId = row.PACKAGE_INSTANCE_ID,
        PackageLineRole = row.PACKAGE_LINE_ROLE,
        PackageComponentOrder = ToInt32(row.PACKAGE_COMPONENT_ORDER),
        PackageParentLineId = ToInt64(row.PACKAGE_PARENT_LINE_ID),
        PackagePricingMethod = row.PACKAGE_PRICING_METHOD,
        OfferId = ToInt32(row.OFFER_ID),
        OfferDtlId = ToInt64(row.OFFER_DTL_ID),
        OfferType = ToInt32(row.OFFER_TYPE),
        OfferInstanceId = row.OFFER_INSTANCE_ID,
        OfferLineRole = row.OFFER_LINE_ROLE,
        OfferParentLineId = ToInt64(row.OFFER_PARENT_LINE_ID),
        OfferPriceApplied = row.OFFER_PRICE_APPLIED,
        OfferDisApplied = row.OFFER_DIS_APPLIED,
        OfferNameSnapshot = row.OFFER_NAME_SNAPSHOT,
        OfferObjectVersionNumber = ToInt64(row.OFFER_OBJECT_VERSION_NUMBER),
        OfferDtlObjectVersionNumber = ToInt64(row.OFFER_DTL_OBJECT_VERSION_NUMBER),
        ClientId = row.D_INV_ROW_ID,
        Price = row.PRICE,
        CatId = ToInt32(row.CATID),
        FixPay = row.FIXPAY,
        PayRate = row.PAYRATE,
        RegularLensesType = row.REGULAR_LENSES_TYPE,
        LensSpecifications = row.LENS_SPECIFICATIONS,
        ContactLensesType = row.CONTACT_LENSES_TYPE,
        FLIndicator = row.F_L_INDICATOR,
        NumberOfPairs = row.NUMBER_OF_PAIRS,
        InsEmp = ToInt32(row.INS_EMP),
    };

    /// <summary>Display values of a saved line: row id as text, description, category name, currency, price list, amounts, shares and VAT as read.</summary>
    private static Dictionary<string, object?> ToLineDisplay(InvoiceLineRow row) => new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(InvoiceLineRow.D_INV_ROW_ID)] = row.D_INV_ROW_ID,
        [nameof(InvoiceLineRow.SERVICEDESC)] = row.SERVICEDESC,
        [nameof(InvoiceLineRow.XCAT_NAMEX)] = row.XCAT_NAMEX,
        [nameof(InvoiceLineRow.CURR_CODE)] = row.CURR_CODE,
        [nameof(InvoiceLineRow.LIST_ID)] = row.LIST_ID,
        [nameof(InvoiceLineRow.MY_PRICE)] = row.MY_PRICE,
        [nameof(InvoiceLineRow.MY_NET)] = row.MY_NET,
        [nameof(InvoiceLineRow.THE_PAY)] = row.THE_PAY,
        [nameof(InvoiceLineRow.THE_COMP)] = row.THE_COMP,
        [nameof(InvoiceLineRow.THE_FIX)] = row.THE_FIX,
        [nameof(InvoiceLineRow.THE_RATE)] = row.THE_RATE,
        [nameof(InvoiceLineRow.VAT_RATE)] = row.VAT_RATE,
        [nameof(InvoiceLineRow.VAT_VAL_CO)] = row.VAT_VAL_CO,
        [nameof(InvoiceLineRow.VAT_VAL_PAT)] = row.VAT_VAL_PAT,
        [nameof(InvoiceLineRow.VAT_VAL_PAT_EX)] = row.VAT_VAL_PAT_EX,
    };

    /// <summary>Builds saved-invoice display values, distinguishing amount due from total collected.</summary>
    private static Dictionary<string, object?> ToDisplay(
        InvoiceHeaderRow row,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> lineDisplay,
        InvoiceTotalsRow totals) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(InvoiceHeaderRow.USER_NAME_TO_SHOW)] = row.USER_NAME_TO_SHOW,
            [nameof(InvoiceHeaderRow.SUB_PAYTYPE_NAME)] = row.SUB_PAYTYPE_NAME,
            [nameof(InvoiceHeaderRow.SUB_PAYTYPE2_NAME)] = row.SUB_PAYTYPE2_NAME,
            [nameof(InvoiceHeaderRow.EDIT_USER_NAME_TO_SHOW)] = row.EDIT_USER_NAME_TO_SHOW,
            [nameof(InvoiceHeaderRow.CLINICNAME)] = row.CLINICNAME,
            [nameof(InvoiceHeaderRow.DOC_NAME)] = row.DOC_NAME,
            [nameof(InvoiceHeaderRow.DOC_NAME1)] = row.DOC_NAME1,
            [nameof(InvoiceHeaderRow.PLAN_NAME)] = row.PLAN_NAME,
            [nameof(InvoiceHeaderRow.LIST_NAME)] = row.LIST_NAME,
            [nameof(InvoiceHeaderRow.COMP_NAME)] = row.COMP_NAME,
            [nameof(InvoiceHeaderRow.SUB_COMP_NAME)] = row.SUB_COMP_NAME,
            [nameof(InvoiceHeaderRow.CLASS_NAME)] = row.CLASS_NAME,
            [nameof(InvoiceHeaderRow.G_NAME)] = row.G_NAME,
            [nameof(InvoiceHeaderRow.CARD_NAME)] = row.CARD_NAME,
            [OfferNameKey] = null,
            [nameof(InvoiceHeaderRow.PATIENTNAME)] = row.PATIENTNAME,
            [nameof(InvoiceHeaderRow.INV_TIME)] = row.INV_TIME,
            [nameof(InvoiceHeaderRow.CARD_ID)] = row.CARD_ID,
            [nameof(InvoiceHeaderRow.LIST_ID)] = row.LIST_ID,
            [nameof(InvoiceHeaderRow.PLAN_CODE)] = row.PLAN_CODE,
            [nameof(InvoiceHeaderRow.UPD_USER_NO)] = row.UPD_USER_NO,
            [nameof(InvoiceHeaderRow.ROW_TYPE)] = row.ROW_TYPE,
            [nameof(InvoiceHeaderRow.PFLAG)] = row.PFLAG,
            [nameof(InvoiceHeaderRow.PAT_PAY)] = row.PAT_PAY,
            [nameof(InvoiceHeaderRow.COMP_PAY)] = row.COMP_PAY,
            [nameof(InvoiceHeaderRow.VAT_TOTAL_PAT)] = row.VAT_TOTAL_PAT,
            [nameof(InvoiceHeaderRow.VAT_TOTAL_CO)] = row.VAT_TOTAL_CO,
            [nameof(InvoiceHeaderRow.VAT_TOTAL)] = row.VAT_TOTAL,
            [nameof(InvoiceHeaderRow.CASH_COLLECTED)] = row.CASH_COLLECTED,
            [TotalCollectedKey] = PaymentAllocationRules.TotalCollected(row.AMOUNT_1, row.AMOUNT_2),
            [nameof(InvoiceHeaderRow.REUND)] = row.REUND,
            [nameof(InvoiceHeaderRow.MAX_DEDUCTABLE)] = row.MAX_DEDUCTABLE,
            [nameof(InvoiceHeaderRow.RESERV_THE_TIME)] = row.RESERV_THE_TIME,
            [nameof(InvoiceHeaderRow.INS_NUMBER)] = row.INS_NUMBER,
            [nameof(InvoiceHeaderRow.CARD_END)] = row.CARD_END,
            [nameof(InvoiceHeaderRow.PAT_POLICY_NO)] = row.PAT_POLICY_NO,
            [nameof(InvoiceTotalsRow.TOTAL_GROSS)] = totals.TOTAL_GROSS,
            [nameof(InvoiceTotalsRow.TOTAL_DISCOUNT)] = totals.TOTAL_DISCOUNT,
            [nameof(InvoiceTotalsRow.TOTAL_NET)] = totals.TOTAL_NET,
            [PreAuthorizationKey] = row.PRE_AUTHORIZATION,
            [LineDisplayKey] = lineDisplay,
        };

    /// <summary>Selected request row as the port tuple, mapped strictly (D-114).</summary>
    /// <exception cref="InvalidCastException">The row has no PAT_SERV_REQ_ROW_ID or SERVICEID, or a non-integral number.</exception>
    /// <exception cref="OverflowException">The row holds a number outside the Int32 or Int64 range.</exception>
    private static (long PatServReqRowId, string ServiceId, int? ReqAStatus, int? ReqNeedA, string? ApprovRefNo) ToSelectedRequestRow(RequestRow row) =>
        (ToInt64(row.PAT_SERV_REQ_ROW_ID) ?? throw new InvalidCastException("Column PAT_SERV_REQ_ROW_ID of V_SERVICES_REQ holds null."),
         string.IsNullOrEmpty(row.SERVICEID) ? throw new InvalidCastException("Column SERVICEID of V_SERVICES_REQ holds null.") : row.SERVICEID,
         ToInt32(row.REQ_A_STATUS),
         ToInt32(row.REQ_NEED_A),
         row.APPROV_REF_NO);

    /// <summary>Completion time as read: a DateTimeOffset as is, a DateTime at offset zero, null or DBNull as null.</summary>
    /// <exception cref="InvalidCastException">The value is of any other type.</exception>
    private static DateTimeOffset? ToCompletedAt(object? value) => value switch
    {
        null or DBNull => null,
        DateTimeOffset offset => offset,
        DateTime date => new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), TimeSpan.Zero),
        _ => throw new InvalidCastException(
            $"Column COMPLETED_AT holds a {value.GetType().FullName} value; a date or timestamp is required."),
    };

    /// <summary>Upper-case, case-insensitive column map of a Dapper row, with DBNull as null.</summary>
    /// <exception cref="InvalidCastException">The row does not expose its columns.</exception>
    private static Dictionary<string, object?> ToRow(object dapperRow)
    {
        ArgumentNullException.ThrowIfNull(dapperRow);

        if (dapperRow is not IEnumerable<KeyValuePair<string, object?>> columns)
        {
            throw new InvalidCastException($"A {dapperRow.GetType().FullName} row does not expose its columns.");
        }

        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in columns)
        {
            row[name.ToUpperInvariant()] = value is DBNull ? null : value;
        }

        return row;
    }

    /// <summary>Column maps of Dapper rows, in order.</summary>
    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ToRows(IEnumerable<object> dapperRows) =>
        dapperRows.Select(IReadOnlyDictionary<string, object?> (row) => ToRow(row)).ToArray();

    /// <summary>Checked conversion of an integral NUMBER to Int32, or null.</summary>
    /// <exception cref="InvalidCastException">The value has a fractional part.</exception>
    /// <exception cref="OverflowException">The value is outside the Int32 range.</exception>
    private static int? ToInt32(decimal? value, [CallerArgumentExpression(nameof(value))] string column = "") =>
        value is { } number ? checked((int)Integral(number, column)) : null;

    /// <summary>Checked conversion of an integral NUMBER to Int64, or null.</summary>
    /// <exception cref="InvalidCastException">The value has a fractional part.</exception>
    /// <exception cref="OverflowException">The value is outside the Int64 range.</exception>
    private static long? ToInt64(decimal? value, [CallerArgumentExpression(nameof(value))] string column = "") =>
        value is { } number ? checked((long)Integral(number, column)) : null;

    /// <summary>The value itself when it has no fractional part.</summary>
    /// <exception cref="InvalidCastException">The value has a fractional part.</exception>
    private static decimal Integral(decimal value, string column) =>
        decimal.Truncate(value) == value
            ? value
            : throw new InvalidCastException(
                $"Column {ColumnName(column)} holds {value.ToString(CultureInfo.InvariantCulture)}; a whole number is required.");

    /// <summary>Column name of a row-property expression such as <c>row.INV_NO</c>.</summary>
    private static string ColumnName(string expression) =>
        expression[(expression.LastIndexOf('.') + 1)..];


    /// <summary>Row of <see cref="GetInvoiceSql"/>.</summary>
    private sealed class InvoiceHeaderRow
    {
        /// <summary>Invoice number.</summary>
        public decimal? INV_NO { get; set; }
        /// <summary>Patient number.</summary>
        public string? PATIENTNO { get; set; }
        /// <summary>Invoice date.</summary>
        public DateTime? INVDATE { get; set; }
        /// <summary>Invoice type id.</summary>
        public decimal? INVTYPEID { get; set; }
        /// <summary>Pay type: 1 cash, 2 credit.</summary>
        public decimal? PAYTYPE { get; set; }
        /// <summary>Payment method 1.</summary>
        public decimal? SUB_PAYTYPE { get; set; }
        /// <summary>Payment method 2.</summary>
        public decimal? SUB_PAYTYPE2 { get; set; }
        /// <summary>Clinic id.</summary>
        public decimal? CLINICID { get; set; }
        /// <summary>Doctor id.</summary>
        public decimal? DOCID { get; set; }
        /// <summary>Currency code.</summary>
        public string? CURR_CODE { get; set; }
        /// <summary>Persisted pre-authorisation.</summary>
        public string? PRE_AUTHORIZATION { get; set; }
        /// <summary>Claim number.</summary>
        public string? CLAIM_NO { get; set; }
        /// <summary>Claim flag.</summary>
        public string? CLAIM_FLAG { get; set; }
        /// <summary>Note number.</summary>
        public string? NOTE_NO { get; set; }
        /// <summary>Final discount percent.</summary>
        public decimal? FINALDISC_PERC { get; set; }
        /// <summary>Final discount amount.</summary>
        public decimal? FINALDISC { get; set; }
        /// <summary>Amount paid by payment method 1.</summary>
        public decimal? AMOUNT_1 { get; set; }
        /// <summary>Amount paid by payment method 2.</summary>
        public decimal? AMOUNT_2 { get; set; }
        /// <summary>Add-to-visit-list flag.</summary>
        public decimal? ADD_TO_LIST { get; set; }
        /// <summary>User number of the operator who created the invoice.</summary>
        public decimal? USER_NO { get; set; }
        /// <summary>Machine name of the creating host.</summary>
        public string? MACHINE_N { get; set; }
        /// <summary>Information centre id.</summary>
        public string? INFO_CENTER_ID { get; set; }
        /// <summary>ER / department-wise flag.</summary>
        public decimal? DEPT_WISE { get; set; }
        /// <summary>Call flag.</summary>
        public decimal? CALL { get; set; }
        /// <summary>Final-discount entry mode: 1 percent, 0 value.</summary>
        public decimal? DISC_T { get; set; }
        /// <summary>Cash tendered by the patient.</summary>
        public decimal? CASH_PAYED { get; set; }
        /// <summary>Company code; '0' is the cash company.</summary>
        public string? COMP_CODE { get; set; }
        /// <summary>Sub-company code.</summary>
        public string? SUB_COMP_CODE { get; set; }
        /// <summary>Insurance class code.</summary>
        public decimal? CLASS_CODE { get; set; }
        /// <summary>Referring doctor id.</summary>
        public decimal? DOCID1 { get; set; }
        /// <summary>Reservation sequence number.</summary>
        public decimal? SEQ_NO { get; set; }
        /// <summary>Patient name.</summary>
        public string? PATIENTNAME { get; set; }
        /// <summary>Invoice time.</summary>
        public string? INV_TIME { get; set; }
        /// <summary>Cash discount card id.</summary>
        public decimal? CARD_ID { get; set; }
        /// <summary>Price list id.</summary>
        public decimal? LIST_ID { get; set; }
        /// <summary>Price plan code.</summary>
        public decimal? PLAN_CODE { get; set; }
        /// <summary>User number of the last editor.</summary>
        public decimal? UPD_USER_NO { get; set; }
        /// <summary>Row type.</summary>
        public decimal? ROW_TYPE { get; set; }
        /// <summary>AM/PM flag.</summary>
        public string? PFLAG { get; set; }
        /// <summary>Patient share.</summary>
        public decimal? PAT_PAY { get; set; }
        /// <summary>Company (credit) share.</summary>
        public decimal? COMP_PAY { get; set; }
        /// <summary>Patient VAT total.</summary>
        public decimal? VAT_TOTAL_PAT { get; set; }
        /// <summary>Company VAT total.</summary>
        public decimal? VAT_TOTAL_CO { get; set; }
        /// <summary>Total VAT.</summary>
        public decimal? VAT_TOTAL { get; set; }
        /// <summary>Amount due from the patient.</summary>
        public decimal? CASH_COLLECTED { get; set; }
        /// <summary>Refund to the patient.</summary>
        public decimal? REUND { get; set; }
        /// <summary>Maximum deductible.</summary>
        public decimal? MAX_DEDUCTABLE { get; set; }
        /// <summary>Reservation time.</summary>
        public decimal? RESERV_THE_TIME { get; set; }
        /// <summary>Insurance number.</summary>
        public string? INS_NUMBER { get; set; }
        /// <summary>Insurance card end date.</summary>
        public DateTime? CARD_END { get; set; }
        /// <summary>Patient policy number.</summary>
        public string? PAT_POLICY_NO { get; set; }
        /// <summary>Name of the creating user (USER_NO); null unless exactly one row matches.</summary>
        public string? USER_NAME_TO_SHOW { get; set; }
        /// <summary>Arabic name of payment method 1; null unless exactly one row matches.</summary>
        public string? SUB_PAYTYPE_NAME { get; set; }
        /// <summary>Arabic name of payment method 2; null unless exactly one row matches.</summary>
        public string? SUB_PAYTYPE2_NAME { get; set; }
        /// <summary>Name of the last editor (UPD_USER_NO); null unless exactly one row matches.</summary>
        public string? EDIT_USER_NAME_TO_SHOW { get; set; }
        /// <summary>Clinic name; null unless exactly one row matches.</summary>
        public string? CLINICNAME { get; set; }
        /// <summary>Doctor name; null unless exactly one row matches.</summary>
        public string? DOC_NAME { get; set; }
        /// <summary>Referring doctor name (DOCID1); null unless exactly one row matches.</summary>
        public string? DOC_NAME1 { get; set; }
        /// <summary>Price plan name; null unless exactly one row matches.</summary>
        public string? PLAN_NAME { get; set; }
        /// <summary>Price list name; null unless exactly one row matches.</summary>
        public string? LIST_NAME { get; set; }
        /// <summary>Company name; null unless exactly one row matches.</summary>
        public string? COMP_NAME { get; set; }
        /// <summary>Sub-company name; null unless exactly one row matches.</summary>
        public string? SUB_COMP_NAME { get; set; }
        /// <summary>Insurance class name; null unless exactly one row matches.</summary>
        public string? CLASS_NAME { get; set; }
        /// <summary>Name of the sub-company's group company (XGROUP); null unless exactly one row matches.</summary>
        public string? G_NAME { get; set; }
        /// <summary>Cash discount card name; null unless exactly one row matches.</summary>
        public string? CARD_NAME { get; set; }
    }

    /// <summary>Row of <see cref="GetInvoiceLinesSql"/>.</summary>
    private sealed class InvoiceLineRow
    {
        /// <summary>Line row id as text.</summary>
        public string? D_INV_ROW_ID { get; set; }
        /// <summary>Service id.</summary>
        public string? SERVICEID { get; set; }
        /// <summary>Service description.</summary>
        public string? SERVICEDESC { get; set; }
        /// <summary>Service category id.</summary>
        public decimal? CATID { get; set; }
        /// <summary>Service category name.</summary>
        public string? XCAT_NAMEX { get; set; }
        /// <summary>Unit price.</summary>
        public decimal? PRICE { get; set; }
        /// <summary>Quantity.</summary>
        public decimal? QTY { get; set; }
        /// <summary>Line discount rate.</summary>
        public decimal? DISC { get; set; }
        /// <summary>Line discount value.</summary>
        public decimal? MY_DISC { get; set; }
        /// <summary>Fixed payer amount.</summary>
        public decimal? FIXPAY { get; set; }
        /// <summary>Payer rate.</summary>
        public decimal? PAYRATE { get; set; }
        /// <summary>Tooth number.</summary>
        public string? TEETH_NO { get; set; }
        /// <summary>Tooth surface.</summary>
        public string? TOOTH_SURFACE { get; set; }
        /// <summary>Second tooth number.</summary>
        public string? TEETH_NO2 { get; set; }
        /// <summary>Linked PAT_SERV_REQ row id.</summary>
        public decimal? PAT_SERV_REQ_ROW_ID { get; set; }
        /// <summary>Approval date.</summary>
        public DateTime? APPROV_DATE { get; set; }
        /// <summary>Approval validity.</summary>
        public decimal? APPROV_VALIDITY { get; set; }
        /// <summary>Approval reference number.</summary>
        public string? APPROV_REF_NO { get; set; }
        /// <summary>Line claim number.</summary>
        public string? CLAIM_NO { get; set; }
        /// <summary>Approval-needed flag.</summary>
        public decimal? REQ_NEED_A { get; set; }
        /// <summary>Approval status.</summary>
        public decimal? REQ_A_STATUS { get; set; }
        /// <summary>Price list id.</summary>
        public decimal? LIST_ID { get; set; }
        /// <summary>Currency code.</summary>
        public string? CURR_CODE { get; set; }
        /// <summary>Line gross.</summary>
        public decimal? MY_PRICE { get; set; }
        /// <summary>Line net.</summary>
        public decimal? MY_NET { get; set; }
        /// <summary>Patient share.</summary>
        public decimal? THE_PAY { get; set; }
        /// <summary>Company share.</summary>
        public decimal? THE_COMP { get; set; }
        /// <summary>Resolved payer fixed amount.</summary>
        public decimal? THE_FIX { get; set; }
        /// <summary>Resolved payer rate.</summary>
        public decimal? THE_RATE { get; set; }
        /// <summary>VAT rate.</summary>
        public decimal? VAT_RATE { get; set; }
        /// <summary>Company VAT.</summary>
        public decimal? VAT_VAL_CO { get; set; }
        /// <summary>Patient VAT.</summary>
        public decimal? VAT_VAL_PAT { get; set; }
        /// <summary>Exempt patient VAT.</summary>
        public decimal? VAT_VAL_PAT_EX { get; set; }
        /// <summary>Regular lenses type.</summary>
        public string? REGULAR_LENSES_TYPE { get; set; }
        /// <summary>Lens specifications.</summary>
        public string? LENS_SPECIFICATIONS { get; set; }
        /// <summary>Contact lenses type.</summary>
        public string? CONTACT_LENSES_TYPE { get; set; }
        /// <summary>F L indicator.</summary>
        public string? F_L_INDICATOR { get; set; }
        /// <summary>Number of pairs.</summary>
        public string? NUMBER_OF_PAIRS { get; set; }
        /// <summary>Insurance employee number.</summary>
        public decimal? INS_EMP { get; set; }
        /// <summary>Owning package service id.</summary>
        public string? PACKAGE_SERVICE_ID { get; set; }
        /// <summary>Package instance id.</summary>
        public string? PACKAGE_INSTANCE_ID { get; set; }
        /// <summary>Package line role.</summary>
        public string? PACKAGE_LINE_ROLE { get; set; }
        /// <summary>Package component order.</summary>
        public decimal? PACKAGE_COMPONENT_ORDER { get; set; }
        /// <summary>Package parent line id.</summary>
        public decimal? PACKAGE_PARENT_LINE_ID { get; set; }
        /// <summary>Package pricing method.</summary>
        public string? PACKAGE_PRICING_METHOD { get; set; }
        /// <summary>Offer id.</summary>
        public decimal? OFFER_ID { get; set; }
        /// <summary>Offer detail id.</summary>
        public decimal? OFFER_DTL_ID { get; set; }
        /// <summary>Offer type.</summary>
        public decimal? OFFER_TYPE { get; set; }
        /// <summary>Offer instance id.</summary>
        public string? OFFER_INSTANCE_ID { get; set; }
        /// <summary>Offer line role.</summary>
        public string? OFFER_LINE_ROLE { get; set; }
        /// <summary>Offer parent line id.</summary>
        public decimal? OFFER_PARENT_LINE_ID { get; set; }
        /// <summary>Offer price applied.</summary>
        public decimal? OFFER_PRICE_APPLIED { get; set; }
        /// <summary>Offer discount applied.</summary>
        public decimal? OFFER_DIS_APPLIED { get; set; }
        /// <summary>Offer name snapshot.</summary>
        public string? OFFER_NAME_SNAPSHOT { get; set; }
        /// <summary>Offer object version number.</summary>
        public decimal? OFFER_OBJECT_VERSION_NUMBER { get; set; }
        /// <summary>Offer detail object version number.</summary>
        public decimal? OFFER_DTL_OBJECT_VERSION_NUMBER { get; set; }
    }

    /// <summary>Row of <see cref="GetInvoiceTotalsSql"/>.</summary>
    private sealed class InvoiceTotalsRow
    {
        /// <summary>Sum of MY_PRICE over the invoice's lines not flagged deleted; 0 when none.</summary>
        public decimal? TOTAL_GROSS { get; set; }
        /// <summary>Sum of MY_DISC over the invoice's lines not flagged deleted; 0 when none.</summary>
        public decimal? TOTAL_DISCOUNT { get; set; }
        /// <summary>Sum of MY_NET over the invoice's lines not flagged deleted; 0 when none.</summary>
        public decimal? TOTAL_NET { get; set; }
    }

    /// <summary>Row of <see cref="GetSelectedRequestRowsSql"/>.</summary>
    private sealed class RequestRow
    {
        /// <summary>Request row id.</summary>
        public decimal? PAT_SERV_REQ_ROW_ID { get; set; }
        /// <summary>Requested service id.</summary>
        public string? SERVICEID { get; set; }
        /// <summary>Approval status.</summary>
        public decimal? REQ_A_STATUS { get; set; }
        /// <summary>Approval-needed flag.</summary>
        public decimal? REQ_NEED_A { get; set; }
        /// <summary>Approval reference number.</summary>
        public string? APPROV_REF_NO { get; set; }
    }

    /// <summary>Row of <see cref="GetClaimPreloadSql"/>.</summary>
    private sealed class ClaimPreloadRow
    {
        /// <summary>Claim number of the claim's first invoice.</summary>
        public string? CLAIM_NO { get; set; }
        /// <summary>Patient number of the claim's first invoice.</summary>
        public string? PATIENTNO { get; set; }
        /// <summary>Clinic id of the claim's first invoice.</summary>
        public decimal? CLINICID { get; set; }
        /// <summary>Company code of the claim's first invoice.</summary>
        public string? COMP_CODE { get; set; }
        /// <summary>Sub-company code of the claim's first invoice.</summary>
        public string? SUB_COMP_CODE { get; set; }
        /// <summary>Insurance class code of the claim's first invoice.</summary>
        public decimal? CLASS_CODE { get; set; }
        /// <summary>Pay type of the claim's first invoice.</summary>
        public decimal? PAYTYPE { get; set; }
        /// <summary>Price list id of the claim's first invoice.</summary>
        public decimal? LIST_ID { get; set; }
        /// <summary>Maximum deductible of the claim's first invoice.</summary>
        public decimal? MAX_DEDUCTABLE { get; set; }
        /// <summary>Cash discount card id of the claim's first invoice.</summary>
        public decimal? CARD_ID { get; set; }
        /// <summary>Insurance number of the claim's first invoice.</summary>
        public string? INS_NUMBER { get; set; }
        /// <summary>Insurance card end date of the claim's first invoice.</summary>
        public DateTime? CARD_END { get; set; }
        /// <summary>Patient policy number of the claim's first invoice.</summary>
        public string? PAT_POLICY_NO { get; set; }
    }

    /// <summary>Row of <see cref="GetCreateRequestSql"/>.</summary>
    private sealed class CreateRequestRow
    {
        /// <summary>Invoice number recorded for the request.</summary>
        public decimal? INV_NO { get; set; }
        /// <summary>Patient number recorded for the request.</summary>
        public string? PATIENTNO { get; set; }
        /// <summary>Completion time of the request, in the type the provider returns.</summary>
        public object? COMPLETED_AT { get; set; }
        /// <summary>Date of the recorded invoice; null when no invoice row matches.</summary>
        public DateTime? INVDATE { get; set; }
        /// <summary>Company code of the recorded invoice; null when no invoice row matches.</summary>
        public string? COMP_CODE { get; set; }
        /// <summary>Sub-company code of the recorded invoice; null when no invoice row matches.</summary>
        public string? SUB_COMP_CODE { get; set; }
        /// <summary>1 when the recorded invoice's clinic has AGE_MIN or AGE_MAX, else 0.</summary>
        public decimal? CLINIC_AGE_LIMIT { get; set; }
    }
}
