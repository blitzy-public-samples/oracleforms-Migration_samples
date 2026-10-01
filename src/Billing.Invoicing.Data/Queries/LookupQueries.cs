using System.Data;
using System.Globalization;
using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;
using Dapper;

namespace Billing.Invoicing.Data.Queries;

/// <summary>Read-only lookups that fill the Domain snapshots, service flags and preferences. UNVERIFIED against Oracle.</summary>
public sealed class LookupQueries : ILookupQueries
{
    /// <summary>SELECT of the PREF values read by T003.</summary>
    public const string GetPreferencesSql =
        "SELECT PREF_NO, PREF_VALUE FROM PREF WHERE PREF_NO IN (960, 970, 422)";

    /// <summary>SELECT of V_PAT_DATA and the sub-company's COMPANYS row read by T023.</summary>
    public const string GetPatientCoverageSql =
        "SELECT v.PATIENTNO, v.PATIENTNAME, v.COMP_CODE, v.CN, v.CC2, v.CN2, v.CLASS_CODE, v.CLASS_NAME, v.PAY_VAT, v.MAX_DEDUCTABLE, v.APPROV_LVL, "
        + "v.PAT_POLICY_NO, v.INS_NUMBER, v.CARD_END, v.CCONTEND, v.CISACTIIVE, v.CCOMP_TYPE, v.MYCLASS, v.WITH_REF, v.ISA, "
        + "s.CONTEND AS SUB_CONTEND, s.ISACTIIVE AS SUB_ISACTIIVE "
        + "FROM V_PAT_DATA v LEFT JOIN COMPANYS s ON s.COMP_CODE = v.CC2 WHERE v.PATIENTNO = :patientNo";

    /// <summary>SELECT of the CLINICS restrictions and the patient's sex read by T031.</summary>
    public const string GetClinicProfileSql =
        "SELECT c.CLINICID, c.SIX, c.AGE_MIN, c.AGE_MAX, c.SYS_CAT_TYPE, "
        + "(SELECT p.SIX FROM PATIENT p WHERE p.PATIENTNO = :patientNo) AS PATIENT_SIX "
        + "FROM CLINICS c WHERE c.CLINICID = :clinicId";

    /// <summary>SELECT of the SERVICES flags read by T052, T066, T068 and OKA.</summary>
    public const string GetServiceProfileSql =
        "SELECT SERVICEID, SHOW_QTY, NVL(BEGIN_OF_CLAIM,0) BEGIN_OF_CLAIM, NVL(ADD_TO_QUE,0) ADD_TO_QUE, SERV_LOC_ID, "
        + "NVL(CONS_REV,0) CONS_REV, NVL(IS_PACKAGE,0) IS_PACKAGE, PKG_TYPE, PRICE_IS_FIXED "
        + "FROM SERVICES WHERE SERVICEID = :serviceId AND LIST_ID = :listId";

    /// <summary>SELECT of the SERVICES flags of several services on one price list read by T052, T066, T068 and OKA.</summary>
    public const string GetServiceProfilesSql =
        "SELECT SERVICEID, SHOW_QTY, NVL(BEGIN_OF_CLAIM,0) BEGIN_OF_CLAIM, NVL(ADD_TO_QUE,0) ADD_TO_QUE, SERV_LOC_ID, "
        + "NVL(CONS_REV,0) CONS_REV, NVL(IS_PACKAGE,0) IS_PACKAGE, PKG_TYPE, PRICE_IS_FIXED "
        + "FROM SERVICES WHERE LIST_ID = :listId AND SERVICEID IN :serviceIds";

    /// <summary>SELECT of the SERVICES flags of a package's PACKAGE_DTL components read by T066 and OKA.</summary>
    public const string GetPackageComponentFlagsSql =
        "SELECT S.SERVICEID, S.SHOW_QTY, NVL(S.BEGIN_OF_CLAIM,0) BEGIN_OF_CLAIM, NVL(S.ADD_TO_QUE,0) ADD_TO_QUE, S.SERV_LOC_ID, "
        + "NVL(S.CONS_REV,0) CONS_REV, NVL(S.IS_PACKAGE,0) IS_PACKAGE, S.PKG_TYPE, S.PRICE_IS_FIXED "
        + "FROM PACKAGE_DTL P, SERVICES S "
        + "WHERE S.SERVICEID = P.SUB_SERVICEID AND S.LIST_ID = P.LIST_ID AND P.SERVICEID = :packageServiceId AND P.LIST_ID = :listId";

    /// <summary>SELECT of USERS_TABLE.MAX_DISC read by T039 and T041.</summary>
    public const string GetUserMaxDiscountSql =
        "SELECT NVL(MAX_DISC,0) FROM USERS_TABLE WHERE USER_NO = :userNo";

    /// <summary>SELECT of COMPANYS.COMP_TYPE read by T023.</summary>
    public const string GetCompanyTypeSql =
        "SELECT COMP_TYPE FROM COMPANYS WHERE COMP_CODE = :compCode";

    /// <summary>SELECT of COMPANYS.IS_DIRECT read by T027, T066 and OKA.</summary>
    public const string GetCompanyIsDirectSql =
        "SELECT IS_DIRECT FROM COMPANYS WHERE COMP_CODE = :compCode";

    /// <summary>SELECT of PAT_VISIT_M.DOCID read by T015.</summary>
    public const string GetVisitDoctorSql =
        "SELECT DOCID FROM PAT_VISIT_M WHERE VISIT_UNIQUE = :visitUnique";

    /// <summary>SELECT of the PAT_SERV_REQ service ids of a claim read by T052.</summary>
    public const string GetRequestedServicesSql =
        "SELECT DISTINCT SERVICEID FROM PAT_SERV_REQ WHERE CLAIM_NO_ = :claimNo AND LIST_ID = :listId";

    /// <summary>SELECT of SYSDATE read by T015.</summary>
    public const string GetDatabaseTimeSql =
        "SELECT SYSDATE FROM DUAL";

    /// <summary>SELECT of the invoice-type list read by T004.</summary>
    public const string GetInvoiceTypesSql =
        "SELECT INVtypeDESC,TO_CHAR(INVtypeID) ME FROM invoices_type ORDER BY INVtypeID";

    /// <summary>SELECT of the currency list read by T004.</summary>
    public const string GetCurrenciesSql =
        "SELECT CURR_NAME_EN,CURR_CODE ME FROM CURRENCIES ORDER BY CURR_NAME_EN";

    /// <summary>SELECT of PATIENT.CARD_ID read by T027.</summary>
    public const string GetPatientCardIdSql =
        "SELECT CARD_ID FROM PATIENT WHERE PATIENTNO = :patientNo";

    /// <summary>SELECT of SERVICES.ADD_TO_QUE read by T029 and T066.</summary>
    public const string GetServiceQueueFlagsSql =
        "SELECT SERVICEID, NVL(ADD_TO_QUE,0) ADD_TO_QUE FROM SERVICES WHERE LIST_ID = :listId AND SERVICEID IN :serviceIds";

    /// <summary>SELECT of DISC_CLASSES.USE_ADVANCED read by CHK_ADV_CLASS.</summary>
    public const string GetClassAdvancedModeSql =
        "SELECT USE_ADVANCED FROM DISC_CLASSES WHERE COMP_CODE = :subCompCode AND CLASS_CODE = :classCode";

    private const int MaxInListIds = 1000;

    private readonly InvoicingDataOptions _options;

    /// <summary>Stores the data-layer settings; opens nothing.</summary>
    /// <param name="options">Connection string and command settings.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="InvoicingDataOptions.CommandTimeoutSeconds"/> is below 1.</exception>
    public LookupQueries(InvoicingDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.EnsureCommandTimeout(nameof(options));

        _options = options;
    }

    /// <summary>PREF values of 960, 970 and 422 keyed by PREF_NO; a number without a row is absent.</summary>
    public async Task<IReadOnlyDictionary<int, string?>> GetPreferences(CancellationToken cancellationToken = default)
    {
        await using var connection = await OracleSessionFactory.OpenConnection(_options, cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<PreferenceRow>(Command(GetPreferencesSql, null, cancellationToken)).ConfigureAwait(false);

        var preferences = new Dictionary<int, string?>();
        foreach (var row in rows)
        {
            if (ToInt32(row.PREF_NO, nameof(row.PREF_NO)) is { } prefNo)
            {
                preferences.TryAdd(prefNo, ToText(row.PREF_VALUE));
            }
        }

        return preferences;
    }

    /// <summary>Coverage snapshot of the patient from V_PAT_DATA and the sub-company's COMPANYS row, or null.</summary>
    /// <param name="patientNo">Patient number.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    public async Task<PatientCoverageSnapshot?> GetPatientCoverage(string patientNo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(patientNo);

        var parameters = new DynamicParameters();
        parameters.Add("patientNo", patientNo, DbType.AnsiString);

        await using var connection = await OracleSessionFactory.OpenConnection(_options, cancellationToken).ConfigureAwait(false);
        var row = await connection.QueryFirstOrDefaultAsync<CoverageRow>(Command(GetPatientCoverageSql, parameters, cancellationToken)).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        return new PatientCoverageSnapshot
        {
            PatientNo = row.PATIENTNO,
            PatientName = row.PATIENTNAME,
            CompCode = row.COMP_CODE,
            CompName = row.CN,
            SubCompCode = row.CC2,
            SubCompName = row.CN2,
            ClassCode = ToInt32(row.CLASS_CODE, nameof(row.CLASS_CODE)),
            ClassName = row.CLASS_NAME,
            PayVat = row.PAY_VAT,
            MaxDeductable = row.MAX_DEDUCTABLE,
            ApprovLvl = row.APPROV_LVL,
            PatPolicyNo = row.PAT_POLICY_NO,
            InsNumber = row.INS_NUMBER,
            CardEnd = row.CARD_END,
            ContractEnd = row.CCONTEND,
            CompanyIsActive = ToInt32(row.CISACTIIVE, nameof(row.CISACTIIVE)),
            CompanyType = ToInt32(row.CCOMP_TYPE, nameof(row.CCOMP_TYPE)),
            MyClass = ToInt32(row.MYCLASS, nameof(row.MYCLASS)),
            ClassWithRef = ToInt32(row.WITH_REF, nameof(row.WITH_REF)),
            ClassIsActive = ToInt32(row.ISA, nameof(row.ISA)),
            SubCompanyContractEnd = row.SUB_CONTEND,
            SubCompanyIsActive = ToInt32(row.SUB_ISACTIIVE, nameof(row.SUB_ISACTIIVE)),
        };
    }

    /// <summary>Clinic restrictions with the patient's sex, or null when the clinic does not exist.</summary>
    /// <param name="clinicId">Clinic id.</param>
    /// <param name="patientNo">Patient number; null leaves the patient's sex null.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    public async Task<ClinicProfile?> GetClinicProfile(int clinicId, string? patientNo, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("clinicId", clinicId, DbType.Int32);
        parameters.Add("patientNo", patientNo, DbType.AnsiString);

        await using var connection = await OracleSessionFactory.OpenConnection(_options, cancellationToken).ConfigureAwait(false);
        var row = await connection.QueryFirstOrDefaultAsync<ClinicRow>(Command(GetClinicProfileSql, parameters, cancellationToken)).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        return new ClinicProfile
        {
            ClinicId = ToInt32(row.CLINICID, nameof(row.CLINICID)),
            Six = ToInt32(row.SIX, nameof(row.SIX)),
            AgeMin = row.AGE_MIN,
            AgeMax = row.AGE_MAX,
            SysCatType = row.SYS_CAT_TYPE,
            PatientSex = ToInt32(row.PATIENT_SIX, nameof(row.PATIENT_SIX)),
        };
    }

    /// <summary>SERVICES flags of a service on a price list, or null when the service is not on it.</summary>
    /// <param name="serviceId">Service id.</param>
    /// <param name="listId">Price list id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    public async Task<ServiceProfile?> GetServiceProfile(string serviceId, decimal listId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);

        var parameters = new DynamicParameters();
        parameters.Add("serviceId", serviceId, DbType.AnsiString);
        parameters.Add("listId", listId, DbType.Decimal);

        await using var connection = await OracleSessionFactory.OpenConnection(_options, cancellationToken).ConfigureAwait(false);
        var row = await connection.QueryFirstOrDefaultAsync<ServiceRow>(Command(GetServiceProfileSql, parameters, cancellationToken)).ConfigureAwait(false);

        return row is null ? null : ToServiceProfile(row);
    }

    /// <summary>SERVICES flags by service id on a price list; ids without a row are absent, and an empty input opens no connection.</summary>
    /// <param name="serviceIds">Service ids; duplicates, null and blank entries are ignored.</param>
    /// <param name="listId">Price list id.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    public async Task<IReadOnlyDictionary<string, ServiceProfile>> GetServiceProfiles(IReadOnlyCollection<string> serviceIds, decimal listId, CancellationToken cancellationToken = default)
    {
        var profiles = new Dictionary<string, ServiceProfile>(StringComparer.Ordinal);
        if (serviceIds is null || serviceIds.Count == 0)
        {
            return profiles;
        }

        var distinctIds = serviceIds.Where(serviceId => !string.IsNullOrWhiteSpace(serviceId)).Distinct(StringComparer.Ordinal).ToArray();
        if (distinctIds.Length == 0)
        {
            return profiles;
        }

        var requestedIds = new HashSet<string>(distinctIds, StringComparer.Ordinal);
        await using var connection = await OracleSessionFactory.OpenConnection(_options, cancellationToken).ConfigureAwait(false);
        foreach (var chunk in distinctIds.Chunk(MaxInListIds))
        {
            var parameters = new DynamicParameters();
            parameters.Add("listId", listId, DbType.Decimal);
            parameters.Add("serviceIds", chunk);

            var rows = await connection.QueryAsync<ServiceRow>(Command(GetServiceProfilesSql, parameters, cancellationToken)).ConfigureAwait(false);
            foreach (var row in rows)
            {
                if (RequestedServiceId(row.SERVICEID, requestedIds) is { } serviceId)
                {
                    profiles.TryAdd(serviceId, ToServiceProfile(row));
                }
            }
        }

        return profiles;
    }

    /// <summary>SERVICES flags of every PACKAGE_DTL component of a package on a price list; empty when it has none.</summary>
    /// <param name="packageServiceId">Service id of the package.</param>
    /// <param name="listId">Price list id of the package and its components.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    public async Task<IReadOnlyList<ServiceProfile>> GetPackageComponentFlags(string packageServiceId, decimal listId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageServiceId);

        var parameters = new DynamicParameters();
        parameters.Add("packageServiceId", packageServiceId, DbType.AnsiString);
        parameters.Add("listId", listId, DbType.Decimal);

        await using var connection = await OracleSessionFactory.OpenConnection(_options, cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<ServiceRow>(Command(GetPackageComponentFlagsSql, parameters, cancellationToken)).ConfigureAwait(false);

        return rows.Select(ToServiceProfile).ToArray();
    }

    /// <summary>USERS_TABLE.MAX_DISC of a user; 0 when the user has no row or no value.</summary>
    /// <param name="userNo">User number.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    public async Task<decimal> GetUserMaxDiscount(int userNo, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("userNo", userNo, DbType.Int32);

        await using var connection = await OracleSessionFactory.OpenConnection(_options, cancellationToken).ConfigureAwait(false);
        var value = await connection.ExecuteScalarAsync<object?>(Command(GetUserMaxDiscountSql, parameters, cancellationToken)).ConfigureAwait(false);

        return ToDecimal(value, "MAX_DISC") ?? 0m;
    }

    /// <summary>COMPANYS.COMP_TYPE of a company, or null.</summary>
    /// <param name="compCode">Company code.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    public Task<int?> GetCompanyType(string compCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compCode);

        var parameters = new DynamicParameters();
        parameters.Add("compCode", compCode, DbType.AnsiString);

        return ScalarInt32(GetCompanyTypeSql, parameters, "COMP_TYPE", cancellationToken);
    }

    /// <summary>COMPANYS.IS_DIRECT of a company, or null.</summary>
    /// <param name="compCode">Company code.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    public Task<int?> GetCompanyIsDirect(string compCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compCode);

        var parameters = new DynamicParameters();
        parameters.Add("compCode", compCode, DbType.AnsiString);

        return ScalarInt32(GetCompanyIsDirectSql, parameters, "IS_DIRECT", cancellationToken);
    }

    /// <summary>PAT_VISIT_M.DOCID of a visit, or null.</summary>
    /// <param name="visitUnique">Visit unique number.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    public Task<int?> GetVisitDoctor(string visitUnique, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(visitUnique);

        var parameters = new DynamicParameters();
        parameters.Add("visitUnique", visitUnique, DbType.AnsiString);

        return ScalarInt32(GetVisitDoctorSql, parameters, "DOCID", cancellationToken);
    }

    /// <summary>Distinct PAT_SERV_REQ service ids requested for a claim on a price list.</summary>
    /// <param name="claimNo">Claim number.</param>
    /// <param name="listId">Price list id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    public async Task<IReadOnlyList<string>> GetRequestedServices(string claimNo, decimal listId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimNo);

        var parameters = new DynamicParameters();
        parameters.Add("claimNo", claimNo, DbType.AnsiString);
        parameters.Add("listId", listId, DbType.Decimal);

        await using var connection = await OracleSessionFactory.OpenConnection(_options, cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<RequestedServiceRow>(Command(GetRequestedServicesSql, parameters, cancellationToken)).ConfigureAwait(false);

        var serviceIds = new List<string>();
        foreach (var row in rows)
        {
            if (ToText(row.SERVICEID) is { } serviceId)
            {
                serviceIds.Add(serviceId);
            }
        }

        return serviceIds;
    }

    /// <summary>Database SYSDATE as returned.</summary>
    /// <exception cref="InvalidCastException">The query returns no date.</exception>
    public async Task<DateTime> GetDatabaseTime(CancellationToken cancellationToken = default)
    {
        await using var connection = await OracleSessionFactory.OpenConnection(_options, cancellationToken).ConfigureAwait(false);
        var value = await connection.ExecuteScalarAsync<object?>(Command(GetDatabaseTimeSql, null, cancellationToken)).ConfigureAwait(false);

        return value is DateTime databaseTime
            ? databaseTime
            : throw new InvalidCastException($"SYSDATE returned {DescribeValue(value)}, not a date.");
    }

    /// <summary>Invoice types from INVOICES_TYPE in INVTYPEID order; a null text is returned empty.</summary>
    public async Task<IReadOnlyList<(string Id, string Description)>> GetInvoiceTypes(CancellationToken cancellationToken = default)
    {
        await using var connection = await OracleSessionFactory.OpenConnection(_options, cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<InvoiceTypeRow>(Command(GetInvoiceTypesSql, null, cancellationToken)).ConfigureAwait(false);

        return rows.Select(row => (row.ME ?? string.Empty, row.INVTYPEDESC ?? string.Empty)).ToArray();
    }

    /// <summary>Currencies from CURRENCIES in English-name order; a null text is returned empty.</summary>
    public async Task<IReadOnlyList<(string Code, string Name)>> GetCurrencies(CancellationToken cancellationToken = default)
    {
        await using var connection = await OracleSessionFactory.OpenConnection(_options, cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<CurrencyRow>(Command(GetCurrenciesSql, null, cancellationToken)).ConfigureAwait(false);

        return rows.Select(row => (row.ME ?? string.Empty, row.CURR_NAME_EN ?? string.Empty)).ToArray();
    }

    /// <summary>PATIENT.CARD_ID of the patient, or null.</summary>
    /// <param name="patientNo">Patient number.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    public Task<int?> GetPatientCardId(string patientNo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(patientNo);

        var parameters = new DynamicParameters();
        parameters.Add("patientNo", patientNo, DbType.AnsiString);

        return ScalarInt32(GetPatientCardIdSql, parameters, "CARD_ID", cancellationToken);
    }

    /// <summary>SERVICES.ADD_TO_QUE by service id on a price list; ids without a row are absent, and an empty input opens no connection.</summary>
    /// <param name="serviceIds">Service ids; duplicates and null entries are ignored.</param>
    /// <param name="listId">Price list id.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    public async Task<IReadOnlyDictionary<string, int>> GetServiceQueueFlags(IReadOnlyCollection<string> serviceIds, decimal listId, CancellationToken cancellationToken = default)
    {
        var flags = new Dictionary<string, int>(StringComparer.Ordinal);
        if (serviceIds is null || serviceIds.Count == 0)
        {
            return flags;
        }

        var distinctIds = serviceIds.Where(serviceId => serviceId is not null).Distinct(StringComparer.Ordinal).ToArray();
        if (distinctIds.Length == 0)
        {
            return flags;
        }

        await using var connection = await OracleSessionFactory.OpenConnection(_options, cancellationToken).ConfigureAwait(false);
        foreach (var chunk in distinctIds.Chunk(MaxInListIds))
        {
            var parameters = new DynamicParameters();
            parameters.Add("listId", listId, DbType.Decimal);
            parameters.Add("serviceIds", chunk);

            var rows = await connection.QueryAsync<QueueFlagRow>(Command(GetServiceQueueFlagsSql, parameters, cancellationToken)).ConfigureAwait(false);
            foreach (var row in rows)
            {
                if (row.SERVICEID is { } serviceId)
                {
                    flags.TryAdd(serviceId, ToInt32(row.ADD_TO_QUE, nameof(row.ADD_TO_QUE)) ?? 0);
                }
            }
        }

        return flags;
    }

    /// <summary>DISC_CLASSES.USE_ADVANCED of a sub-company's class, or null.</summary>
    /// <param name="subCompCode">Sub-company code.</param>
    /// <param name="classCode">Class code.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    public Task<int?> GetClassAdvancedMode(string subCompCode, string classCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subCompCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(classCode);

        var parameters = new DynamicParameters();
        parameters.Add("subCompCode", subCompCode, DbType.AnsiString);
        parameters.Add("classCode", classCode, DbType.AnsiString);

        return ScalarInt32(GetClassAdvancedModeSql, parameters, "USE_ADVANCED", cancellationToken);
    }

    /// <summary>First column of the first row of a query as a whole number, or null when there is no row or value.</summary>
    private async Task<int?> ScalarInt32(string sql, DynamicParameters parameters, string column, CancellationToken cancellationToken)
    {
        await using var connection = await OracleSessionFactory.OpenConnection(_options, cancellationToken).ConfigureAwait(false);
        var value = await connection.ExecuteScalarAsync<object?>(Command(sql, parameters, cancellationToken)).ConfigureAwait(false);

        return ToInt32(ToDecimal(value, column), column);
    }

    /// <summary>Command for a query with the configured timeout.</summary>
    private CommandDefinition Command(string sql, DynamicParameters? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, commandTimeout: _options.CommandTimeoutSeconds, cancellationToken: cancellationToken);

    /// <summary>Service profile of a SERVICES row with no components and no REQ_NEED_A.</summary>
    private static ServiceProfile ToServiceProfile(ServiceRow row) =>
        new()
        {
            ServiceId = row.SERVICEID,
            ShowQty = ToInt32(row.SHOW_QTY, nameof(row.SHOW_QTY)),
            BeginOfClaim = ToInt32(row.BEGIN_OF_CLAIM, nameof(row.BEGIN_OF_CLAIM)),
            AddToQue = ToInt32(row.ADD_TO_QUE, nameof(row.ADD_TO_QUE)),
            ServLocId = ToInt32(row.SERV_LOC_ID, nameof(row.SERV_LOC_ID)),
            ConsRev = ToInt32(row.CONS_REV, nameof(row.CONS_REV)),
            IsPackage = ToInt32(row.IS_PACKAGE, nameof(row.IS_PACKAGE)),
            PkgType = ToInt32(row.PKG_TYPE, nameof(row.PKG_TYPE)),
            PriceIsFixed = row.PRICE_IS_FIXED,
            ReqNeedA = null,
            Components = Array.Empty<ServiceProfile>(),
        };

    /// <summary>Requested id that a SERVICES row's SERVICEID equals as returned or without trailing blanks, or null.</summary>
    private static string? RequestedServiceId(string? serviceId, IReadOnlySet<string> requestedIds)
    {
        if (serviceId is null || requestedIds.Contains(serviceId))
        {
            return serviceId;
        }

        var trimmed = serviceId.TrimEnd();
        return requestedIds.Contains(trimmed) ? trimmed : null;
    }

    /// <summary>Converts a scalar to a decimal; null and DBNull give null.</summary>
    /// <exception cref="InvalidCastException">The value is not a number.</exception>
    private static decimal? ToDecimal(object? value, string column) =>
        value switch
        {
            null or DBNull => null,
            decimal number => number,
            IConvertible convertible => convertible.ToDecimal(CultureInfo.InvariantCulture),
            _ => throw new InvalidCastException($"Column {column} holds a {value.GetType().Name}, not a number."),
        };

    /// <summary>Converts a decimal to an Int32 exactly; null gives null.</summary>
    /// <exception cref="InvalidCastException">The value is not a whole number.</exception>
    /// <exception cref="OverflowException">The value exceeds the Int32 range.</exception>
    private static int? ToInt32(decimal? value, string column)
    {
        if (value is not { } number)
        {
            return null;
        }

        if (decimal.Truncate(number) != number)
        {
            throw new InvalidCastException($"Column {column} holds {number.ToString(CultureInfo.InvariantCulture)}, which is not a whole number.");
        }

        if (number < int.MinValue || number > int.MaxValue)
        {
            throw new OverflowException($"Column {column} holds {number.ToString(CultureInfo.InvariantCulture)}, which exceeds the Int32 range.");
        }

        return checked((int)number);
    }

    /// <summary>Converts a column value to invariant text; null and DBNull give null.</summary>
    private static string? ToText(object? value) =>
        value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);

    /// <summary>Names a scalar value for an error message.</summary>
    private static string DescribeValue(object? value) =>
        value is null or DBNull ? "no value" : $"a {value.GetType().Name}";

    private sealed class PreferenceRow
    {
        public decimal? PREF_NO { get; set; }

        public object? PREF_VALUE { get; set; }
    }

    private sealed class CoverageRow
    {
        public string? PATIENTNO { get; set; }

        public string? PATIENTNAME { get; set; }

        public string? COMP_CODE { get; set; }

        public string? CN { get; set; }

        public string? CC2 { get; set; }

        public string? CN2 { get; set; }

        public decimal? CLASS_CODE { get; set; }

        public string? CLASS_NAME { get; set; }

        public string? PAY_VAT { get; set; }

        public decimal? MAX_DEDUCTABLE { get; set; }

        public decimal? APPROV_LVL { get; set; }

        public string? PAT_POLICY_NO { get; set; }

        public string? INS_NUMBER { get; set; }

        public DateTime? CARD_END { get; set; }

        public DateTime? CCONTEND { get; set; }

        public decimal? CISACTIIVE { get; set; }

        public decimal? CCOMP_TYPE { get; set; }

        public decimal? MYCLASS { get; set; }

        public decimal? WITH_REF { get; set; }

        public decimal? ISA { get; set; }

        public DateTime? SUB_CONTEND { get; set; }

        public decimal? SUB_ISACTIIVE { get; set; }
    }

    private sealed class ClinicRow
    {
        public decimal? CLINICID { get; set; }

        public decimal? SIX { get; set; }

        public decimal? AGE_MIN { get; set; }

        public decimal? AGE_MAX { get; set; }

        public string? SYS_CAT_TYPE { get; set; }

        public decimal? PATIENT_SIX { get; set; }
    }

    private sealed class ServiceRow
    {
        public string? SERVICEID { get; set; }

        public decimal? SHOW_QTY { get; set; }

        public decimal? BEGIN_OF_CLAIM { get; set; }

        public decimal? ADD_TO_QUE { get; set; }

        public decimal? SERV_LOC_ID { get; set; }

        public decimal? CONS_REV { get; set; }

        public decimal? IS_PACKAGE { get; set; }

        public decimal? PKG_TYPE { get; set; }

        public string? PRICE_IS_FIXED { get; set; }
    }

    private sealed class RequestedServiceRow
    {
        public object? SERVICEID { get; set; }
    }

    private sealed class InvoiceTypeRow
    {
        public string? INVTYPEDESC { get; set; }

        public string? ME { get; set; }
    }

    private sealed class CurrencyRow
    {
        public string? CURR_NAME_EN { get; set; }

        public string? ME { get; set; }
    }

    private sealed class QueueFlagRow
    {
        public string? SERVICEID { get; set; }

        public decimal? ADD_TO_QUE { get; set; }
    }
}
