using System.Data;
using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Ports;
using Dapper;

namespace Billing.Invoicing.Data.Queries;

/// <summary>Record-group SELECTs behind the served LOVs of the invoice screen. UNVERIFIED against Oracle.</summary>
public sealed class LovQueries : ILovQueries
{
    /// <summary>SELECT of record group COMPANY, verbatim except for bind names.</summary>
    public const string CompanySql =
        "SELECT ALL c.COMP_CODE, c.COMP_NAME ,'SAR' curr_code\n" +
        "FROM v_VALID_MAIN_CO c\n" +
        "WHERE c.info_center_id=:infoCenterId\n" +
        "ORDER BY c.COMP_CODE ";

    /// <summary>SELECT of record group SUB_COMP, verbatim except for bind names.</summary>
    public const string SubCompanySql =
        "SELECT ALL COMPANYS.COMP_CODE, COMPANYS.COMP_NAME\n" +
        "FROM COMPANYS \n" +
        "where comp_type =3\n" +
        "and parent_comp=:compCode\n" +
        "ORDER BY COMPANYS.COMP_CODE \n";

    /// <summary>SELECT of record group THE_CLASS, verbatim except for bind names.</summary>
    public const string TheClassSql =
        "SELECT ALL DISC_CLASSES.CLASS_CODE, DISC_CLASSES.CLASS_NAME\n" +
        "FROM DISC_CLASSES \n" +
        "WHERE COMP_CODE=:subCompCode";

    /// <summary>SELECT of record group PAY_TYPE, verbatim except for bind names.</summary>
    public const string PayTypesSql =
        "SELECT ALL PAY_TYPES.PAY_TYPE_ID, \n" +
        "decode (:lang,'E',PAY_TYPES.PAY_TYPE_NAME_en,'A',\n" +
        "PAY_TYPES.PAY_TYPE_NAME_ar) as\n" +
        "PAY_TYPE_NAME,\n" +
        "PAY_TYPES.PAY_TYPE_ACC_NO, \n" +
        "PAY_TYPES.PAY_COMM_RATE, PAY_TYPES.PAY_COMM_ACC\n" +
        "FROM PAY_TYPES where RECEP_USe=1\n";

    /// <summary>SELECT of record group DOC, verbatim except for bind names.</summary>
    public const string DocSql =
        "SELECT ALL DOCTORS.DOCID, \n" +
        "DOCTORS.DOC_NAME, CLINICS.CLINICID, CLINICS.CLINICNAME\n" +
        "FROM DOCTORS, CLINICS\n" +
        "WHERE DOCTORS.DOC_ACTIVE = 1\n" +
        " AND (DOCTORS.CLINICID = CLINICS.CLINICID) \n" +
        "and CURR_INFO_CENTER =:infoCenterId";

    /// <summary>SELECT of record group RESERV_NO, verbatim except for bind names.</summary>
    public const string ReservNoSql =
        "SELECT ALL DOC_DATES.RESERV_NO,the_time,\n" +
        "PATAINTNO,\n" +
        "PATIENTNAME,DOC_DATES_ROW_ID\n" +
        "FROM DOC_DATES\n" +
        "WHERE (DOC_DATES.THE_DATE = :invDate\n" +
        " AND DOC_DATES.DOCID = :docId\n" +
        " AND (:reservSystem500=1  or DOC_DATES.PFLAG = PFLAG)\n" +
        " AND ((DOC_DATES.PATAINTNO IS NULL) OR (DOC_DATES.PATAINTNO = :patientNo)))";

    /// <summary>SELECT of record group OFFERS, verbatim except for bind names.</summary>
    public const string OffersSql =
        "SELECT ALL OFFERS.OFERID, OFFERS.OFFER_NAME\n" +
        "FROM OFFERS \n" +
        "where :payType=1\n" +
        "and offers.offer_type=0\n" +
        "and :invDate >= START_DATE    and :invDate <=ENDDATE   \n" +
        "and OFFER_INFO_CENTER=:infoCenterId\n";

    /// <summary>SELECT of record group CAT, verbatim except for bind names.</summary>
    public const string CatSql =
        "SELECT ALL CATID,CATDESC,STORE_ID\n" +
        "FROM  servicecat\n" +
        "order by CATID";

    private const string LanguageCode = "E";

    private const int ReservationSystem = 0;

    private readonly InvoicingDataOptions _options;

    /// <summary>Stores the data-layer settings; opens nothing.</summary>
    /// <param name="options">Connection string and command timeout.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="InvoicingDataOptions.CommandTimeoutSeconds"/> is below 1.</exception>
    public LovQueries(InvoicingDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.EnsureCommandTimeout(nameof(options));

        _options = options;
    }

    /// <summary>Companies of the information centre for the COMPANY1_2 LOV.</summary>
    /// <param name="infoCenterId">Operator's information centre.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>COMP_CODE, COMP_NAME and CURR_CODE rows ordered by COMP_CODE.</returns>
    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> Company(string infoCenterId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(infoCenterId);

        var parameters = new DynamicParameters();
        AddText(parameters, "infoCenterId", infoCenterId);

        return Query(_options, CompanySql, parameters, cancellationToken);
    }

    /// <summary>Sub-companies of a company for the SUB_COMPANY LOV.</summary>
    /// <param name="compCode">Company code of the draft header.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>COMP_CODE and COMP_NAME rows ordered by COMP_CODE.</returns>
    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SubCompany(string compCode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compCode);

        var parameters = new DynamicParameters();
        AddText(parameters, "compCode", compCode);

        return Query(_options, SubCompanySql, parameters, cancellationToken);
    }

    /// <summary>Classes of a sub-company for the THE_CLASS LOV.</summary>
    /// <param name="subCompCode">Sub-company code of the draft header.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>CLASS_CODE and CLASS_NAME rows.</returns>
    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TheClass(string subCompCode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subCompCode);

        var parameters = new DynamicParameters();
        AddText(parameters, "subCompCode", subCompCode);

        return Query(_options, TheClassSql, parameters, cancellationToken);
    }

    /// <summary>Reception payment types, with English names, for the PAY_TYPE1 and PAY_TYPE2 LOVs.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>PAY_TYPE_ID, PAY_TYPE_NAME, PAY_TYPE_ACC_NO, PAY_COMM_RATE and PAY_COMM_ACC rows.</returns>
    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PayTypes(CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        AddText(parameters, "lang", LanguageCode);

        return Query(_options, PayTypesSql, parameters, cancellationToken);
    }

    /// <summary>Active doctors of the information centre, with their clinics, for the DOC LOV.</summary>
    /// <param name="infoCenterId">Operator's information centre.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>DOCID, DOC_NAME, CLINICID and CLINICNAME rows.</returns>
    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> Doc(string infoCenterId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(infoCenterId);

        var parameters = new DynamicParameters();
        AddText(parameters, "infoCenterId", infoCenterId);

        return Query(_options, DocSql, parameters, cancellationToken);
    }

    /// <summary>Reservations of a doctor on a date, free or held by the patient, for the view-only RESERV_NO LOV.</summary>
    /// <param name="invDate">Draft date, bound as received.</param>
    /// <param name="docId">Doctor of the draft header.</param>
    /// <param name="patientNo">Patient of the draft header.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>RESERV_NO, THE_TIME, PATAINTNO, PATIENTNAME and DOC_DATES_ROW_ID rows.</returns>
    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReservNo(DateTime invDate, int docId, string patientNo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patientNo);

        var parameters = new DynamicParameters();
        AddDate(parameters, "invDate", invDate);
        AddNumber(parameters, "docId", docId);
        AddNumber(parameters, "reservSystem500", ReservationSystem);
        AddText(parameters, "patientNo", patientNo);

        return Query(_options, ReservNoSql, parameters, cancellationToken);
    }

    /// <summary>Standard offers valid on a date at the information centre for the OFFERS LOV; no rows unless the pay type is 1.</summary>
    /// <param name="payType">Pay type of the draft header.</param>
    /// <param name="invDate">Draft date.</param>
    /// <param name="infoCenterId">Operator's information centre.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>OFERID and OFFER_NAME rows.</returns>
    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> Offers(int payType, DateTime invDate, string infoCenterId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(infoCenterId);

        var parameters = new DynamicParameters();
        AddNumber(parameters, "payType", payType);
        AddDate(parameters, "invDate", invDate);
        AddText(parameters, "infoCenterId", infoCenterId);

        return Query(_options, OffersSql, parameters, cancellationToken);
    }

    /// <summary>Service categories for the CAT LOV.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>CATID, CATDESC and STORE_ID rows ordered by CATID.</returns>
    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> Cat(CancellationToken cancellationToken = default) =>
        Query(_options, CatSql, null, cancellationToken);

    /// <summary>Runs a SELECT on a new connection and returns its rows keyed by upper-case column name.</summary>
    /// <param name="options">Connection string and command timeout.</param>
    /// <param name="sql">SELECT text.</param>
    /// <param name="parameters">Named binds, or null when the SELECT has none.</param>
    /// <param name="cancellationToken">Cancels the connection open and the read.</param>
    /// <returns>The rows in query order.</returns>
    private static async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> Query(
        InvoicingDataOptions options,
        string sql,
        DynamicParameters? parameters,
        CancellationToken cancellationToken)
    {
        await using var connection = await OracleSessionFactory.OpenConnection(options, cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync(
            new CommandDefinition(
                sql,
                parameters,
                commandTimeout: options.CommandTimeoutSeconds,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        var result = new List<IReadOnlyDictionary<string, object?>>();
        foreach (object row in rows)
        {
            result.Add(ToRow(row));
        }

        return result;
    }

    /// <summary>Copies a Dapper row into a case-insensitive dictionary keyed by upper-case column name, with database nulls as null.</summary>
    /// <param name="row">Row returned by the non-generic Dapper query.</param>
    /// <returns>The row's column values.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="row"/> is not a column dictionary.</exception>
    private static IReadOnlyDictionary<string, object?> ToRow(object row)
    {
        if (row is not IDictionary<string, object> columns)
        {
            throw new InvalidOperationException($"Query row of type {row?.GetType().FullName ?? "null"} is not a column dictionary.");
        }

        var values = new Dictionary<string, object?>(columns.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (column, value) in columns)
        {
            values[column.ToUpperInvariant()] = value is DBNull ? null : value;
        }

        return values;
    }

    /// <summary>Adds a VARCHAR2 input bind.</summary>
    private static void AddText(DynamicParameters parameters, string name, string value) =>
        parameters.Add(name, value, DbType.AnsiString, ParameterDirection.Input);

    /// <summary>Adds a DATE input bind that keeps the time of day.</summary>
    private static void AddDate(DynamicParameters parameters, string name, DateTime value) =>
        parameters.Add(name, value, DbType.Date, ParameterDirection.Input);

    /// <summary>Adds a NUMBER input bind.</summary>
    private static void AddNumber(DynamicParameters parameters, string name, int value) =>
        parameters.Add(name, value, DbType.Int32, ParameterDirection.Input);
}
