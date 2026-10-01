using System.Data;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Data.Queries;
using Billing.Invoicing.Domain.Model;
using Dapper;
using Microsoft.Data.Sqlite;
using Oracle.ManagedDataAccess.Client;

namespace Billing.Invoicing.Tests.Data;

/// <summary>Data-layer query predicates, binds and row mappings, including the D-93 saved-invoice projections and the D-108 bind-width guards, and the D-12 approval check mode of <see cref="BilImportGateway"/>.</summary>
[Trait("Category", "DataUnit")]
public sealed class DataContractTests
{
    private const string WhereKeyword = "WHERE";

    private const string InfoCenterBind = "infoCenterId";
    private const string PatientBind = "patientNo";
    private const string VisitBind = "visitUnique";
    private const string PayTypeBind = "payType";
    private const string ListBind = "listId";
    private const string ServiceIdsBind = "serviceIds";
    private const string InvDateBind = "invDate";
    private const string DocBind = "docId";
    private const string ReservationSystemBind = "reservSystem500";
    private const string InvDateEchoSql = "SELECT :" + InvDateBind;
    private const string AddDateName = "AddDate";

    private const string ServicesSource = "FROMSERVICES" + WhereKeyword;

    private const string InvoiceBlockFrom = "FROM T_INV t ";
    private const string InvoiceBlockWhere = "(t.INVTYPEID <> 8 and t.INVTYPEID <> 9) and t.PHARMACY_INV_NO is null";
    private const string InvoiceBlockOrderBy = "ORDER BY t.INV_NO";

    private const string RequestRowTypeName = "RequestRow";
    private const string RequestRowMapperName = "ToSelectedRequestRow";
    private const string RequestRowIdColumn = "PAT_SERV_REQ_ROW_ID";
    private const string ServiceIdColumn = "SERVICEID";
    private const string RequestView = "V_SERVICES_REQ";
    private const string ReqAStatusColumn = "REQ_A_STATUS";
    private const string ReqNeedAColumn = "REQ_NEED_A";
    private const string ApprovRefNoColumn = "APPROV_REF_NO";
    private const string ClaimBind = "claimNo";
    private const string PackListParametersName = "PackListParameters";

    private const string LineRowTypeName = "InvoiceLineRow";
    private const string LineDraftMapperName = "ToLineDraft";
    private const string LineDisplayMapperName = "ToLineDisplay";
    private const string RowsMapperName = "ToRows";
    private const string LinesAlias = "d";
    private const string SavedLinesFrom = "FROM D_INV d WHERE";
    private const string RowIdColumn = "D_INV_ROW_ID";
    private const string RowIdSource = LinesAlias + "." + RowIdColumn;
    private const string RowIdAsText = "TO_CHAR(" + RowIdSource + ") AS " + RowIdColumn;
    private const string FortyDigitRowId = "1234567890123456789012345678901234567890";
    private const string InvoiceBind = "invNo";
    private const string LinePriceColumn = "MY_PRICE";
    private const string LineDiscountColumn = "MY_DISC";
    private const string LineNetColumn = "MY_NET";

    private const string HeaderRowTypeName = "InvoiceHeaderRow";
    private const string TotalsRowTypeName = "InvoiceTotalsRow";
    private const string DisplayMapperName = "ToDisplay";
    private const string HeaderDraftMapperName = "ToHeaderDraft";
    private const string InvoiceSource = "T_INV";
    private const string InvDateColumn = "INVDATE";
    private const string Amount1Column = "AMOUNT_1";
    private const string Amount2Column = "AMOUNT_2";
    private const string AmountDueColumn = "CASH_COLLECTED";
    private const string TotalCollectedKey = "TOTAL_COLLECTED";
    private const string TotalGrossColumn = "TOTAL_GROSS";
    private const string TotalDiscountColumn = "TOTAL_DISCOUNT";
    private const string TotalNetColumn = "TOTAL_NET";

    private const string SolutionFileName = "SmallCashInvoice.sln";
    private const string FormExportPath = "05_Complex/Inv_Small_Cash.xml";
    private const string EncodedNewline = "&#10;";
    private const string OwnCentre = "7";
    private const string ForeignCentre = "8";
    private const string OwnCompany = "C1";
    private const string ForeignCompany = "C9";
    private const string PatientHolding = "P1";
    private const int ActiveDoctor = 12;
    private const int InactiveDoctor = 13;
    private const int ForeignDoctor = 14;

    private const string LovSeed =
        "WITH COMPANYS (COMP_CODE, COMP_NAME, comp_type, parent_comp) AS (VALUES "
        + "('S2', 'Second sub', 3, 'C1'), "
        + "('S1', 'First sub', 3, 'C1'), "
        + "('S9', 'Foreign sub', 3, 'C9'), "
        + "('C1', 'Own company', 1, NULL), "
        + "('C9', 'Foreign company', 1, NULL)), "
        + "v_VALID_MAIN_CO (COMP_CODE, COMP_NAME, info_center_id) AS (VALUES "
        + "('C1', 'Own company', '7'), "
        + "('C9', 'Foreign company', '8')), "
        + "DISC_CLASSES (CLASS_CODE, CLASS_NAME, COMP_CODE) AS (VALUES "
        + "(1, 'Gold', 'S1'), "
        + "(2, 'Silver', 'S1'), "
        + "(3, 'Main', 'C1'), "
        + "(9, 'Foreign', 'S9')), "
        + "DOCTORS (DOCID, DOC_NAME, CLINICID, DOC_ACTIVE, CURR_INFO_CENTER) AS (VALUES "
        + "(12, 'Own active', 5, 1, '7'), "
        + "(13, 'Own inactive', 5, 0, '7'), "
        + "(14, 'Foreign active', 5, 1, '8')), "
        + "CLINICS (CLINICID, CLINICNAME) AS (VALUES "
        + "(5, 'Clinic five')), "
        + "DOC_DATES (RESERV_NO, THE_TIME, PATAINTNO, PATIENTNAME, DOC_DATES_ROW_ID, THE_DATE, DOCID, PFLAG) AS (VALUES "
        + "(1, '10:00', NULL, NULL, 101, '2026-03-31 00:00:00', 12, 'AM'), "
        + "(2, '10:15', 'P1', 'Patient one', 102, '2026-03-31 00:00:00', 12, 'AM'), "
        + "(3, '10:30', 'P2', 'Patient two', 103, '2026-03-31 00:00:00', 12, 'AM'), "
        + "(4, '11:00', NULL, NULL, 104, '2026-03-31 00:00:00', 13, 'AM'), "
        + "(5, '11:15', NULL, NULL, 105, '2026-03-31 00:00:00', 14, 'AM')), "
        + "PAY_TYPES (PAY_TYPE_ID, PAY_TYPE_NAME_en, PAY_TYPE_NAME_ar, PAY_TYPE_ACC_NO, PAY_COMM_RATE, PAY_COMM_ACC, RECEP_USe) AS (VALUES "
        + "(1, 'Mada', 'Mada AR', '4001', 1.5, '4002', 1), "
        + "(2, 'Visa', 'Visa AR', '4003', 2.5, '4004', 1), "
        + "(3, 'Internal', 'Internal AR', '4005', 0, '4006', 0)), "
        + "OFFERS (OFERID, OFFER_NAME, offer_type, START_DATE, ENDDATE, OFFER_INFO_CENTER) AS (VALUES "
        + "(21, 'Ends on the day', 0, '2026-03-01 00:00:00', '2026-03-31 00:00:00', '7'), "
        + "(22, 'Starts on the day', 0, '2026-03-31 00:00:00', '2026-04-30 00:00:00', '7'), "
        + "(23, 'Ended the day before', 0, '2026-03-01 00:00:00', '2026-03-30 00:00:00', '7'), "
        + "(24, 'Starts the day after', 0, '2026-04-01 00:00:00', '2026-04-30 00:00:00', '7')) ";

    private static readonly DateTime ReservationDate = new(2026, 3, 31);

    private static readonly string[] RequestRowApprovalColumns = ["REQ_A_STATUS", "REQ_NEED_A", "APPROV_REF_NO"];

    private static readonly string[] ClaimPreloadCardColumns = ["INS_NUMBER", "CARD_END", "PAT_POLICY_NO"];

    private static readonly (string Form, string Target)[] RecordGroupBindNames =
    [
        (":global.lang", ":lang"),
        (":t_inv.comp_Code", ":compCode"),
        (":SUB_COMP_CODE", ":subCompCode"),
        (":invdate", ":invDate"),
        (":docidx", ":docId"),
        (":global.reserv_system_500", ":reservSystem500"),
        (":PATIENTNO", ":patientNo"),
    ];

    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.CultureInvariant);

    private static readonly Regex BindReference = new(@":(\w+)", RegexOptions.CultureInvariant);

    private static readonly Regex OrKeyword = new(@"\bOR\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    [Fact]
    public void LastInvoiceNo_SelectsTheMaximumInvoiceNumberOfTInv()
    {
        string squashed = Squash(InvoiceQueries.GetLastInvoiceNoSql);

        Assert.StartsWith("SELECTMAX(INV_NO)FROMT_INV" + WhereKeyword, squashed, StringComparison.Ordinal);
    }

    [Fact]
    public void LastInvoiceNo_WhereClauseCarriesTheFourT082Predicates()
    {
        string where = WhereClause(InvoiceQueries.GetLastInvoiceNoSql);

        Assert.Contains("IN_OUT=1", where, StringComparison.Ordinal);
        Assert.Contains("ROW_TYPE=1", where, StringComparison.Ordinal);
        Assert.Contains("PHARMACY_INV_NOISNULL", where, StringComparison.Ordinal);
        Assert.Contains("INFO_CENTER_ID=:" + Squash(InfoCenterBind), where, StringComparison.Ordinal);
        Assert.Empty(OrKeyword.Matches(InvoiceQueries.GetLastInvoiceNoSql));
    }

    [Fact]
    public void LastInvoiceNo_BindsOnlyTheInformationCentre()
    {
        Assert.Equal(new[] { InfoCenterBind }, Binds(InvoiceQueries.GetLastInvoiceNoSql));
    }

    [Fact]
    public void Invoice_CarriesTheTInvBlockWhereAndEndsWithItsOrderBy()
    {
        string squashed = Squash(InvoiceQueries.GetInvoiceSql);

        Assert.Contains(Squash(InvoiceBlockFrom + WhereKeyword + " " + InvoiceBlockWhere), squashed, StringComparison.Ordinal);
        Assert.EndsWith(Squash(InvoiceBlockOrderBy), squashed, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedRequestRows_ReadsVServicesReq()
    {
        string squashed = Squash(InvoiceQueries.GetSelectedRequestRowsSql);

        Assert.StartsWith("SELECT", squashed, StringComparison.Ordinal);
        Assert.Contains("FROMV_SERVICES_REQ" + WhereKeyword, squashed, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedRequestRows_WhereClauseCarriesTheT085CursorPredicates()
    {
        string where = WhereClause(InvoiceQueries.GetSelectedRequestRowsSql);

        Assert.Contains("PATIENTNO=:" + Squash(PatientBind), where, StringComparison.Ordinal);
        Assert.Contains("D_INV_ROW_IDISNULL", where, StringComparison.Ordinal);
        Assert.Contains("SELECT_TO_INV=1", where, StringComparison.Ordinal);
        Assert.Contains("VISIT_UNIQUE=:" + Squash(VisitBind), where, StringComparison.Ordinal);
        Assert.Contains("DECODE(PAY_TYPE,'CASH',1,'CREDIT',2)=:" + Squash(PayTypeBind), where, StringComparison.Ordinal);
        Assert.Empty(OrKeyword.Matches(InvoiceQueries.GetSelectedRequestRowsSql));
    }

    [Fact]
    public void SelectedRequestRows_BindsPatientVisitAndPayType()
    {
        string[] expected = [PatientBind, VisitBind, PayTypeBind];

        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            Binds(InvoiceQueries.GetSelectedRequestRowsSql).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void SelectedRequestRows_ReturnsTheD26ApprovalColumns()
    {
        string squashed = Squash(InvoiceQueries.GetSelectedRequestRowsSql);
        int fromIndex = squashed.IndexOf("FROMV_SERVICES_REQ", StringComparison.Ordinal);

        Assert.True(fromIndex > 0);

        string selectList = squashed[..fromIndex];
        foreach (string column in RequestRowApprovalColumns)
        {
            Assert.Contains(column, selectList, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ClaimPreload_SelectsTheT015CardFields()
    {
        string squashed = Squash(InvoiceQueries.GetClaimPreloadSql);
        int fromIndex = squashed.IndexOf("FROMT_INV" + WhereKeyword, StringComparison.Ordinal);

        Assert.StartsWith("SELECT", squashed, StringComparison.Ordinal);
        Assert.True(fromIndex > 0);

        string[] selectList = squashed["SELECT".Length..fromIndex].Split(',');
        foreach (string column in ClaimPreloadCardColumns)
        {
            Assert.Contains(column, selectList);
        }
    }

    [Fact]
    public void ClaimPreload_WhereClauseKeepsTheT015FirstInvoiceCursor()
    {
        Assert.Equal(
            Squash("INV_NO = (SELECT MIN(INV_NO) FROM T_INV WHERE CLAIM_NO = :" + ClaimBind + " AND :" + ClaimBind + " NOT IN ('1','2'))"),
            WhereClause(InvoiceQueries.GetClaimPreloadSql));
        Assert.Equal(new[] { ClaimBind }, Binds(InvoiceQueries.GetClaimPreloadSql));
    }

    [Fact]
    public void HeaderInputBinder_BindsNoInputForTheClaimPreloadCardFields()
    {
        var header = new InvoiceHeaderDraft
        {
            DraftDate = new DateTime(2026, 9, 28, 10, 0, 0),
            PatientNo = "1001",
            PayType = 2,
            ClinicId = 14,
            ClaimNo = "C-7788",
            CompCode = "205",
            SubCompCode = "305",
            ClassCode = 4,
        };
        var withCardFields = header with { InsNumber = "INS-55", CardEnd = new DateTime(2027, 1, 31), PatPolicyNo = "POL-9" };
        var operatorContext = new OperatorContext
        {
            UserNo = 501,
            UserName = "cashier1",
            InfoCenterId = "7",
            MachineName = "WS-FRONT-01",
            SessionId = "7D3F2C1B9A8E4F6D8C2B1A0F9E8D7C6B",
        };

        var expected = HeaderInputBinder.Bind(header, operatorContext).Select(p => (p.ParameterName, p.Value)).ToArray();
        var actual = HeaderInputBinder.Bind(withCardFields, operatorContext).Select(p => (p.ParameterName, p.Value)).ToArray();

        Assert.Equal(expected, actual);
        Assert.DoesNotContain(actual, p => ClaimPreloadCardColumns.Any(
            column => p.ParameterName.Contains(column, StringComparison.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(null, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    public void ApprovalCheckMode_IsZeroOnlyForX422ApprovCheckTwo(int? x422ApprovCheck, int expectedMode)
    {
        var gateway = new BilImportGateway(new InvoicingDataOptions());

        Assert.Equal(expectedMode, gateway.ApprovalCheckMode(x422ApprovCheck));
    }

    [Fact]
    [Trait("Decision", "D-12")]
    public void SelectedRequestRow_CompleteRow_MapsToThePortTuple()
    {
        Assert.Equal((7001L, "S100", (int?)3, (int?)1, (string?)"REF1"), MapSelectedRequestRow(7001m, "S100", 3m, 1m, "REF1"));
    }

    [Theory]
    [Trait("Decision", "D-12")]
    [InlineData(null)]
    [InlineData("")]
    public void SelectedRequestRow_WithoutServiceId_IsRejectedNamingTheColumn(string? serviceId)
    {
        var failure = Assert.Throws<InvalidCastException>(() => MapSelectedRequestRow(7002m, serviceId, null, null, null));

        Assert.Contains(ServiceIdColumn, failure.Message, StringComparison.Ordinal);
        Assert.Contains(RequestView, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(RequestRowIdColumn, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "D-12")]
    public void SelectedRequestRow_WithoutRequestRowId_IsRejectedNamingTheColumn()
    {
        var failure = Assert.Throws<InvalidCastException>(() => MapSelectedRequestRow(null, "S100", 3m, 1m, "REF1"));

        Assert.Contains(RequestRowIdColumn, failure.Message, StringComparison.Ordinal);
        Assert.Contains(RequestView, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "D-12")]
    public void SelectedRequestRow_WithOutOfRangeStatus_IsRejected()
    {
        Assert.Throws<OverflowException>(() => MapSelectedRequestRow(7003m, "S100", 3000000000m, 1m, null));
    }

    [Fact]
    [Trait("Decision", "D-12")]
    public void SelectedRequestRows_SelectListIsTheRequestRowPropertiesInDeclarationOrder()
    {
        Type? rowType = typeof(InvoiceQueries).GetNestedType(RequestRowTypeName, BindingFlags.NonPublic);
        Assert.NotNull(rowType);

        string squashed = Squash(InvoiceQueries.GetSelectedRequestRowsSql);
        int fromIndex = squashed.IndexOf("FROM" + RequestView, StringComparison.Ordinal);

        Assert.StartsWith("SELECT", squashed, StringComparison.Ordinal);
        Assert.True(fromIndex > 0);
        Assert.Equal(PropertyNames(rowType), squashed["SELECT".Length..fromIndex].Split(','));
    }

    /// <summary>Maps a private RequestRow holding the given columns with the private ToSelectedRequestRow.</summary>
    /// <param name="patServReqRowId">PAT_SERV_REQ_ROW_ID of the row.</param>
    /// <param name="serviceId">SERVICEID of the row.</param>
    /// <param name="reqAStatus">REQ_A_STATUS of the row.</param>
    /// <param name="reqNeedA">REQ_NEED_A of the row.</param>
    /// <param name="approvRefNo">APPROV_REF_NO of the row.</param>
    /// <returns>The port tuple of the selected row.</returns>
    private static (long, string, int?, int?, string?) MapSelectedRequestRow(
        decimal? patServReqRowId,
        string? serviceId,
        decimal? reqAStatus,
        decimal? reqNeedA,
        string? approvRefNo)
    {
        Type? rowType = typeof(InvoiceQueries).GetNestedType(RequestRowTypeName, BindingFlags.NonPublic);
        MethodInfo? mapper = typeof(InvoiceQueries).GetMethod(RequestRowMapperName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(rowType);
        Assert.NotNull(mapper);

        object row = NewRow(rowType);
        SetProperty(row, RequestRowIdColumn, patServReqRowId);
        SetProperty(row, ServiceIdColumn, serviceId);
        SetProperty(row, ReqAStatusColumn, reqAStatus);
        SetProperty(row, ReqNeedAColumn, reqNeedA);
        SetProperty(row, ApprovRefNoColumn, approvRefNo);

        object? mapped = mapper.Invoke(null, BindingFlags.DoNotWrapExceptions, null, [row], CultureInfo.InvariantCulture);
        return Assert.IsType<(long, string, int?, int?, string?)>(mapped);
    }

    [Fact]
    public void ServiceProfiles_SelectsTheSingleServiceColumnsFromServices()
    {
        string batched = Squash(LookupQueries.GetServiceProfilesSql);
        string single = Squash(LookupQueries.GetServiceProfileSql);
        int batchedFrom = batched.IndexOf(ServicesSource, StringComparison.Ordinal);
        int singleFrom = single.IndexOf(ServicesSource, StringComparison.Ordinal);

        Assert.True(batchedFrom > 0);
        Assert.True(singleFrom > 0);
        Assert.Equal(single[..singleFrom], batched[..batchedFrom]);
    }

    [Fact]
    public void ServiceProfiles_WhereClauseFiltersOneListAndTheServiceIdList()
    {
        string where = WhereClause(LookupQueries.GetServiceProfilesSql);

        Assert.Contains("LIST_ID=:" + Squash(ListBind), where, StringComparison.Ordinal);
        Assert.Contains(Squash("SERVICEID IN :" + ServiceIdsBind), where, StringComparison.Ordinal);
        Assert.Empty(OrKeyword.Matches(LookupQueries.GetServiceProfilesSql));
    }

    [Fact]
    public void ServiceProfiles_BindsOnlyTheListAndTheServiceIds()
    {
        string[] expected = [ListBind, ServiceIdsBind];

        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            Binds(LookupQueries.GetServiceProfilesSql).Order(StringComparer.Ordinal));
    }

    [Theory]
    [Trait("Decision", "D-108")]
    [InlineData("C-7788")]
    [InlineData(null)]
    public void BoundedInput_AtOrWithinWidth_IsAnAnsiVarchar2SizedToTheWidth(string? claimNo)
    {
        var parameters = new DynamicParameters();

        BoundedVarchar2.AddInput(parameters, ClaimBind, claimNo, BoundedVarchar2.ClaimNoBytes, ClaimBind);

        DbString bound = parameters.Get<DbString>(ClaimBind);
        Assert.Equal(claimNo, bound.Value);
        Assert.True(bound.IsAnsi);
        Assert.False(bound.IsFixedLength);
        Assert.Equal(BoundedVarchar2.ClaimNoBytes, bound.Length);

        using var command = new OracleCommand();
        bound.AddParameter(command, ClaimBind);

        OracleParameter parameter = Assert.Single(command.Parameters.Cast<OracleParameter>());
        Assert.Equal(ClaimBind, parameter.ParameterName);
        Assert.Equal(OracleDbType.Varchar2, parameter.OracleDbType);
        Assert.Equal(ParameterDirection.Input, parameter.Direction);
        Assert.Equal(BoundedVarchar2.ClaimNoBytes, parameter.Size);
        Assert.Equal((object?)claimNo ?? DBNull.Value, parameter.Value);
    }

    [Theory]
    [Trait("Decision", "D-108")]
    [InlineData('C', 41, "claimNo has 41 characters; at most 40 can be bound.")]
    [InlineData('\u00E9', 21, "claimNo has 42 bytes in UTF-8; at most 40 can be bound.")]
    public void BoundedInput_OverWidth_IsRejectedAndNotBound(char character, int length, string expectedText)
    {
        var parameters = new DynamicParameters();

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => BoundedVarchar2.AddInput(parameters, ClaimBind, new string(character, length), BoundedVarchar2.ClaimNoBytes, ClaimBind));

        Assert.Equal(ClaimBind, error.ParamName);
        Assert.Equal(expectedText, error.Data[OracleFailureTranslator.BindingRejectionKey]);
        Assert.Empty(parameters.ParameterNames);
    }

    [Fact]
    [Trait("Decision", "D-108")]
    public void BoundedInputList_ExpandsIntoAnsiVarchar2ElementsSizedToTheServiceIdWidth()
    {
        string atWidth = new('S', BoundedVarchar2.ServiceIdBytes);

        DbString[] elements = BoundedVarchar2.InputList(ServiceIdsBind, ["S1", atWidth], BoundedVarchar2.ServiceIdBytes, ServiceIdsBind);

        Assert.Equal(new[] { "S1", atWidth }, elements.Select(element => element.Value));
        Assert.All(elements, element =>
        {
            Assert.True(element.IsAnsi);
            Assert.Equal(BoundedVarchar2.ServiceIdBytes, element.Length);
        });

        MethodInfo? packList = typeof(SqlMapper).GetMethod(PackListParametersName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(packList);

        using var command = new OracleCommand(LookupQueries.GetServiceQueueFlagsSql);
        packList.Invoke(null, BindingFlags.DoNotWrapExceptions, null, [command, ServiceIdsBind, elements], CultureInfo.InvariantCulture);

        OracleParameter[] expanded = command.Parameters.Cast<OracleParameter>().ToArray();
        Assert.Equal(new[] { ServiceIdsBind + "1", ServiceIdsBind + "2" }, expanded.Select(parameter => parameter.ParameterName));
        Assert.Equal(new object[] { "S1", atWidth }, expanded.Select(parameter => parameter.Value));
        Assert.All(expanded, parameter =>
        {
            Assert.Equal(OracleDbType.Varchar2, parameter.OracleDbType);
            Assert.Equal(BoundedVarchar2.ServiceIdBytes, parameter.Size);
        });
        Assert.EndsWith($"SERVICEID IN (:{ServiceIdsBind}1,:{ServiceIdsBind}2)", command.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "D-108")]
    public void BoundedInputList_OverWidthElement_IsRejected()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => BoundedVarchar2.InputList(ServiceIdsBind, ["S1", new string('S', 21)], BoundedVarchar2.ServiceIdBytes, ServiceIdsBind));

        Assert.Equal(ServiceIdsBind, error.ParamName);
        Assert.Equal("serviceIds has 21 characters; at most 20 can be bound.", error.Data[OracleFailureTranslator.BindingRejectionKey]);
    }

    [Theory]
    [Trait("Decision", "D-108")]
    [InlineData("visit-doctor", VisitBind, "visitUnique has 40 characters; at most 39 can be bound.")]
    [InlineData("visit-doctor-bytes", VisitBind, "visitUnique has 40 bytes in UTF-8; at most 39 can be bound.")]
    [InlineData("company-type", "compCode", "compCode has 11 characters; at most 10 can be bound.")]
    [InlineData("company-is-direct", "compCode", "compCode has 11 characters; at most 10 can be bound.")]
    [InlineData("class-advanced-mode", "subCompCode", "subCompCode has 11 characters; at most 10 can be bound.")]
    [InlineData("requested-services", ClaimBind, "claimNo has 41 characters; at most 40 can be bound.")]
    [InlineData("service-profile", "serviceId", "serviceId has 21 characters; at most 20 can be bound.")]
    [InlineData("package-component-flags", "packageServiceId", "packageServiceId has 21 characters; at most 20 can be bound.")]
    [InlineData("service-profiles", ServiceIdsBind, "serviceIds has 21 characters; at most 20 can be bound.")]
    [InlineData("service-queue-flags", ServiceIdsBind, "serviceIds has 21 characters; at most 20 can be bound.")]
    [InlineData("claim-preload", ClaimBind, "claimNo has 41 characters; at most 40 can be bound.")]
    [InlineData("claim-preload-bytes", ClaimBind, "claimNo has 41 bytes in UTF-8; at most 40 can be bound.")]
    [InlineData("patient-coverage", PatientBind, "patientNo has 13 characters; at most 12 can be bound.")]
    [InlineData("patient-coverage-bytes", PatientBind, "patientNo has 13 bytes in UTF-8; at most 12 can be bound.")]
    [InlineData("clinic-profile", PatientBind, "patientNo has 13 characters; at most 12 can be bound.")]
    [InlineData("patient-card-id", PatientBind, "patientNo has 13 characters; at most 12 can be bound.")]
    [InlineData("selected-request-rows-patient", PatientBind, "patientNo has 13 characters; at most 12 can be bound.")]
    [InlineData("selected-request-rows-visit", VisitBind, "visitUnique has 40 characters; at most 39 can be bound.")]
    [InlineData("selected-request-rows-visit-bytes", VisitBind, "visitUnique has 40 bytes in UTF-8; at most 39 can be bound.")]
    [InlineData("last-invoice-no", InfoCenterBind, "infoCenterId has 11 characters; at most 10 can be bound.")]
    [InlineData("last-invoice-no-bytes", InfoCenterBind, "infoCenterId has 11 bytes in UTF-8; at most 10 can be bound.")]
    [InlineData("create-request", "requestId", "requestId has 65 characters; at most 64 can be bound.")]
    [InlineData("create-request-bytes", "requestId", "requestId has 65 bytes in UTF-8; at most 64 can be bound.")]
    public async Task QueryBind_OverWidthIsRefusedBeforeTheConnectionOpensAndAtWidthReachesIt(string queryCase, string paramName, string expectedText)
    {
        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => InvokeQuery(queryCase, overWidth: true));

        Assert.Equal(paramName, error.ParamName);
        Assert.Equal(expectedText, error.Data[OracleFailureTranslator.BindingRejectionKey]);

        DataFailure? failure = new OracleFailureTranslator().Translate(error);
        Assert.NotNull(failure);
        Assert.Equal(422, failure.Status);
        Assert.Equal("field-validation", failure.Type);
        Assert.Equal(expectedText, failure.Message);
        Assert.Null(failure.Field);

        InvalidOperationException open = await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeQuery(queryCase, overWidth: false));

        Assert.True(open.Data[OracleFailureTranslator.ConfigurationFaultKey] is true);
    }

    /// <summary>Calls one width-checked query, over a blank connection string, with its checked bind one past or exactly at its width.</summary>
    /// <param name="queryCase">Query and value kind to call.</param>
    /// <param name="overWidth">True for a value one past the width.</param>
    /// <returns>The query's task.</returns>
    private static Task InvokeQuery(string queryCase, bool overWidth)
    {
        var options = new InvoicingDataOptions();
        var lookups = new LookupQueries(options);
        string Wide(int width) => new('9', overWidth ? width + 1 : width);
        string WideInBytes(int width)
        {
            int bytes = overWidth ? width + 1 : width;
            return new string('\u00E9', bytes / 2) + new string('9', bytes % 2);
        }

        return queryCase switch
        {
            "visit-doctor" => lookups.GetVisitDoctor(Wide(BoundedVarchar2.VisitUniqueBytes)),
            "visit-doctor-bytes" => lookups.GetVisitDoctor(WideInBytes(BoundedVarchar2.VisitUniqueBytes)),
            "company-type" => lookups.GetCompanyType(Wide(BoundedVarchar2.CompCodeBytes)),
            "company-is-direct" => lookups.GetCompanyIsDirect(Wide(BoundedVarchar2.CompCodeBytes)),
            "class-advanced-mode" => lookups.GetClassAdvancedMode(Wide(BoundedVarchar2.SubCompCodeBytes), "4"),
            "requested-services" => lookups.GetRequestedServices(Wide(BoundedVarchar2.ClaimNoBytes), 1m),
            "service-profile" => lookups.GetServiceProfile(Wide(BoundedVarchar2.ServiceIdBytes), 1m),
            "package-component-flags" => lookups.GetPackageComponentFlags(Wide(BoundedVarchar2.ServiceIdBytes), 1m),
            "service-profiles" => lookups.GetServiceProfiles(["S1", Wide(BoundedVarchar2.ServiceIdBytes)], 1m),
            "service-queue-flags" => lookups.GetServiceQueueFlags(["S1", Wide(BoundedVarchar2.ServiceIdBytes)], 1m),
            "claim-preload" => new InvoiceQueries(options).GetClaimPreload(Wide(BoundedVarchar2.ClaimNoBytes)),
            "claim-preload-bytes" => new InvoiceQueries(options).GetClaimPreload(WideInBytes(BoundedVarchar2.ClaimNoBytes)),
            "patient-coverage" => lookups.GetPatientCoverage(Wide(BoundedVarchar2.PatientNoBytes)),
            "patient-coverage-bytes" => lookups.GetPatientCoverage(WideInBytes(BoundedVarchar2.PatientNoBytes)),
            "clinic-profile" => lookups.GetClinicProfile(5, Wide(BoundedVarchar2.PatientNoBytes)),
            "patient-card-id" => lookups.GetPatientCardId(Wide(BoundedVarchar2.PatientNoBytes)),
            "selected-request-rows-patient" => new InvoiceQueries(options).GetSelectedRequestRows(Wide(BoundedVarchar2.PatientNoBytes), "41", 1),
            "selected-request-rows-visit" => new InvoiceQueries(options).GetSelectedRequestRows(PatientHolding, Wide(BoundedVarchar2.VisitUniqueBytes), 1),
            "selected-request-rows-visit-bytes" => new InvoiceQueries(options).GetSelectedRequestRows(PatientHolding, WideInBytes(BoundedVarchar2.VisitUniqueBytes), 1),
            "last-invoice-no" => new InvoiceQueries(options).GetLastInvoiceNo(Wide(BoundedVarchar2.InfoCenterIdBytes)),
            "last-invoice-no-bytes" => new InvoiceQueries(options).GetLastInvoiceNo(WideInBytes(BoundedVarchar2.InfoCenterIdBytes)),
            "create-request" => new InvoiceQueries(options).GetCreateRequest(Wide(BoundedVarchar2.RequestIdBytes)),
            "create-request-bytes" => new InvoiceQueries(options).GetCreateRequest(WideInBytes(BoundedVarchar2.RequestIdBytes)),
            _ => throw new ArgumentOutOfRangeException(nameof(queryCase), queryCase, null),
        };
    }

    [Theory]
    [Trait("Decision", "D-93")]
    [InlineData(nameof(InvoiceQueries.GetInvoiceLinesSql))]
    [InlineData(nameof(InvoiceQueries.GetMoreDetailsLinesSql))]
    public void SavedLineRead_SelectsTheRowIdAsTextInItsNumericOrder(string statementName)
    {
        string squashed = Squash(Statement(statementName));
        int fromIndex = squashed.IndexOf(Squash(SavedLinesFrom), StringComparison.Ordinal);

        Assert.StartsWith("SELECT", squashed, StringComparison.Ordinal);
        Assert.True(fromIndex > 0);

        string selectList = squashed["SELECT".Length..fromIndex];
        Assert.Contains(Squash(RowIdAsText), selectList.Split(','));
        Assert.Single(Regex.Matches(selectList, Regex.Escape(Squash(RowIdSource)), RegexOptions.CultureInvariant));
        Assert.EndsWith(Squash("ORDER BY " + RowIdSource), squashed, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "D-93")]
    public void SavedLine_FortyDigitRowId_IsTheClientIdAndLineDisplayTextExactly()
    {
        Type? rowType = typeof(InvoiceQueries).GetNestedType(LineRowTypeName, BindingFlags.NonPublic);
        MethodInfo? toDraft = typeof(InvoiceQueries).GetMethod(LineDraftMapperName, BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo? toDisplay = typeof(InvoiceQueries).GetMethod(LineDisplayMapperName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(rowType);
        Assert.NotNull(toDraft);
        Assert.NotNull(toDisplay);

        PropertyInfo? rowId = rowType.GetProperty(RowIdColumn);
        Assert.NotNull(rowId);
        Assert.Equal(typeof(string), rowId.PropertyType);

        object row = NewRow(rowType);
        SetProperty(row, RowIdColumn, FortyDigitRowId);
        SetProperty(row, ServiceIdColumn, "S100");

        var draft = Assert.IsType<InvoiceLineDraft>(
            toDraft.Invoke(null, BindingFlags.DoNotWrapExceptions, null, [row], CultureInfo.InvariantCulture));
        var display = Assert.IsType<Dictionary<string, object?>>(
            toDisplay.Invoke(null, BindingFlags.DoNotWrapExceptions, null, [row], CultureInfo.InvariantCulture));

        Assert.Equal(FortyDigitRowId, draft.ClientId);
        Assert.Equal(FortyDigitRowId, Assert.IsType<string>(display[RowIdColumn]));
    }

    [Fact]
    [Trait("Decision", "D-93")]
    public void MoreDetailsLine_FortyDigitRowId_IsTheRowIdTextExactly()
    {
        MethodInfo? toRows = typeof(InvoiceQueries).GetMethod(RowsMapperName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(toRows);

        object[] rows =
        [
            new Dictionary<string, object?>(StringComparer.Ordinal) { [RowIdColumn] = FortyDigitRowId, [ServiceIdColumn] = "S100" },
        ];

        var mapped = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(
            toRows.Invoke(null, BindingFlags.DoNotWrapExceptions, null, [rows], CultureInfo.InvariantCulture));

        Assert.Equal(FortyDigitRowId, Assert.IsType<string>(Assert.Single(mapped)[RowIdColumn]));
    }

    [Fact]
    [Trait("Decision", "D-93")]
    public void InvoiceTotals_SelectThePriceDiscountAndNetSumsEachZeroWithoutLines()
    {
        Type? totalsType = typeof(InvoiceQueries).GetNestedType(TotalsRowTypeName, BindingFlags.NonPublic);
        Assert.NotNull(totalsType);

        (string Column, string Alias)[] totals =
        [
            (LinePriceColumn, TotalGrossColumn),
            (LineDiscountColumn, TotalDiscountColumn),
            (LineNetColumn, TotalNetColumn),
        ];
        string squashed = Squash(InvoiceQueries.GetInvoiceTotalsSql);
        int fromIndex = squashed.IndexOf(Squash(SavedLinesFrom), StringComparison.Ordinal);

        Assert.StartsWith("SELECT", squashed, StringComparison.Ordinal);
        Assert.True(fromIndex > 0);
        Assert.Equal(
            Squash(string.Join(", ", totals.Select(total => TotalsTerm(total.Column, total.Alias)))),
            squashed["SELECT".Length..fromIndex]);
        Assert.All(totals, total => Assert.NotNull(totalsType.GetProperty(total.Alias)));
    }

    [Fact]
    [Trait("Decision", "D-93")]
    public void InvoiceTotals_ReadOnlyTheInvoicesLinesNotFlaggedDeletedAndBindOnlyTheInvoice()
    {
        Assert.Contains(Squash(SavedLinesFrom), Squash(InvoiceQueries.GetInvoiceTotalsSql), StringComparison.Ordinal);
        Assert.Equal(
            Squash(LinesAlias + ".INV_NO = :" + InvoiceBind + " AND COALESCE(" + LinesAlias + ".IS_DELETED, 0) = 0"),
            WhereClause(InvoiceQueries.GetInvoiceTotalsSql));
        Assert.Equal(new[] { InvoiceBind }, Binds(InvoiceQueries.GetInvoiceTotalsSql));
        Assert.Empty(OrKeyword.Matches(InvoiceQueries.GetInvoiceTotalsSql));
    }

    [Fact]
    [Trait("Decision", "D-93")]
    public void SavedDisplay_CarriesAmountDueTotalCollectedAndTheLineTotalsAsRead()
    {
        Type? headerType = typeof(InvoiceQueries).GetNestedType(HeaderRowTypeName, BindingFlags.NonPublic);
        Type? totalsType = typeof(InvoiceQueries).GetNestedType(TotalsRowTypeName, BindingFlags.NonPublic);
        MethodInfo? toDisplay = typeof(InvoiceQueries).GetMethod(DisplayMapperName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(headerType);
        Assert.NotNull(totalsType);
        Assert.NotNull(toDisplay);

        object header = NewRow(headerType);
        SetProperty(header, Amount1Column, 60m);
        SetProperty(header, Amount2Column, null);
        SetProperty(header, AmountDueColumn, 100m);
        object totals = NewRow(totalsType);
        SetProperty(totals, TotalGrossColumn, 150.25m);
        SetProperty(totals, TotalDiscountColumn, 5.5m);
        SetProperty(totals, TotalNetColumn, 144.75m);

        var display = Assert.IsType<Dictionary<string, object?>>(toDisplay.Invoke(
            null,
            BindingFlags.DoNotWrapExceptions,
            null,
            [header, Array.Empty<IReadOnlyDictionary<string, object?>>(), totals],
            CultureInfo.InvariantCulture));

        Assert.Equal(100m, Assert.IsType<decimal>(display[AmountDueColumn]));
        Assert.Equal(60m, Assert.IsType<decimal>(display[TotalCollectedKey]));
        Assert.Equal(150.25m, Assert.IsType<decimal>(display[TotalGrossColumn]));
        Assert.Equal(5.5m, Assert.IsType<decimal>(display[TotalDiscountColumn]));
        Assert.Equal(144.75m, Assert.IsType<decimal>(display[TotalNetColumn]));
    }

    [Fact]
    [Trait("Decision", "D-93")]
    public void SavedHeader_WithoutInvDate_IsRejectedNamingTheColumn()
    {
        var failure = Assert.Throws<InvalidCastException>(() => MapHeaderDraft(null));

        Assert.Contains(InvDateColumn, failure.Message, StringComparison.Ordinal);
        Assert.Contains(InvoiceSource, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "D-93")]
    public void SavedHeader_WithInvDate_IsTheDraftDateAndTheInvoiceDate()
    {
        var invDate = new DateTime(2026, 9, 28, 10, 15, 0);

        InvoiceHeaderDraft header = MapHeaderDraft(invDate);

        Assert.Equal(invDate, header.DraftDate);
        Assert.Equal(invDate, header.InvDate);
    }

    [Theory]
    [Trait("Decision", "D-105")]
    [InlineData(LovQueries.PayTypesSql, "SELECTPAY_TYPE_ID,PAY_TYPE_NAMEFROM(")]
    [InlineData(LovQueries.ReservNoSql, "SELECTRESERV_NO,THE_TIME,PATAINTNOFROM(")]
    public void PaymentAndReservationLovs_ProjectOnlyTheLovMappedColumns(string sql, string projection)
    {
        Assert.StartsWith(projection, Squash(sql), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Decision", "D-105")]
    [Trait("Decision", "D-106")]
    [InlineData(LovQueries.PayTypesSql, "PAY_TYPE")]
    [InlineData(LovQueries.ReservNoSql, "RESERV_NO")]
    [InlineData(LovQueries.SubCompanySql, "SUB_COMP")]
    [InlineData(LovQueries.TheClassSql, "THE_CLASS")]
    public void WrappedLov_KeepsItsRecordGroupSelectVerbatimExceptForBindNames(string sql, string recordGroup)
    {
        Assert.Contains(RecordGroupSelect(recordGroup), sql, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Decision", "D-106")]
    [InlineData(LovQueries.SubCompanySql, "compCode,infoCenterId")]
    [InlineData(LovQueries.TheClassSql, "subCompCode,infoCenterId")]
    [InlineData(LovQueries.ReservNoSql, "invDate,docId,reservSystem500,patientNo,infoCenterId")]
    public void DependentLov_BindsItsRecordGroupItemsAndTheInformationCentre(string sql, string binds)
    {
        Assert.Equal(binds.Split(','), Binds(sql));
    }

    [Fact]
    [Trait("Decision", "D-106")]
    [Trait("Decision", "D-129")]
    public async Task SubCompanySql_InSqlite_ListsOnlySubCompaniesOfACompanyOfTheCentre()
    {
        var own = await LovRows(LovQueries.SubCompanySql, new { compCode = OwnCompany, infoCenterId = OwnCentre });
        var foreign = await LovRows(LovQueries.SubCompanySql, new { compCode = ForeignCompany, infoCenterId = OwnCentre });
        var foreignAtItsCentre = await LovRows(LovQueries.SubCompanySql, new { compCode = ForeignCompany, infoCenterId = ForeignCentre });

        Assert.Equal(new object?[] { "S1", "S2" }, own.Select(row => row["COMP_CODE"]));
        Assert.All(own, row => Assert.Equal(new[] { "COMP_CODE", "COMP_NAME" }, row.Keys));
        Assert.Empty(foreign);
        Assert.Equal(new object?[] { "S9" }, foreignAtItsCentre.Select(row => row["COMP_CODE"]));
    }

    [Fact]
    [Trait("Decision", "D-106")]
    [Trait("Decision", "D-129")]
    public async Task TheClassSql_InSqlite_ListsOnlyClassesOfASubCompanyOfACompanyOfTheCentre()
    {
        var own = await LovRows(LovQueries.TheClassSql, new { subCompCode = "S1", infoCenterId = OwnCentre });
        var foreign = await LovRows(LovQueries.TheClassSql, new { subCompCode = "S9", infoCenterId = OwnCentre });
        var notASubCompany = await LovRows(LovQueries.TheClassSql, new { subCompCode = OwnCompany, infoCenterId = OwnCentre });

        Assert.Equal(new object?[] { 1L, 2L }, own.Select(row => row["CLASS_CODE"]).Order());
        Assert.All(own, row => Assert.Equal(new[] { "CLASS_CODE", "CLASS_NAME" }, row.Keys));
        Assert.Empty(foreign);
        Assert.Empty(notASubCompany);
    }

    [Fact]
    [Trait("Decision", "D-105")]
    [Trait("Decision", "D-106")]
    [Trait("Decision", "D-129")]
    public async Task ReservNoSql_InSqlite_ListsOnlyTheProjectedColumnsForAnActiveDoctorOfTheCentre()
    {
        var own = await LovRows(LovQueries.ReservNoSql, ReservationBinds(ActiveDoctor, OwnCentre));
        var inactive = await LovRows(LovQueries.ReservNoSql, ReservationBinds(InactiveDoctor, OwnCentre));
        var foreign = await LovRows(LovQueries.ReservNoSql, ReservationBinds(ForeignDoctor, OwnCentre));
        var foreignAtItsCentre = await LovRows(LovQueries.ReservNoSql, ReservationBinds(ForeignDoctor, ForeignCentre));

        Assert.Equal(new object?[] { 1L, 2L }, own.Select(row => row["RESERV_NO"]).Order());
        Assert.All(own, row => Assert.Equal(new[] { "RESERV_NO", "THE_TIME", "PATAINTNO" }, row.Keys));
        Assert.Empty(inactive);
        Assert.Empty(foreign);
        Assert.Equal(new object?[] { 5L }, foreignAtItsCentre.Select(row => row["RESERV_NO"]));
    }

    [Fact]
    [Trait("Decision", "D-105")]
    [Trait("Decision", "D-129")]
    public async Task PayTypesSql_InSqlite_ListsOnlyTheTypeIdAndEnglishName()
    {
        var rows = await LovRows(LovQueries.PayTypesSql, new { lang = "E" });

        Assert.Equal(new object?[] { 1L, 2L }, rows.Select(row => row["PAY_TYPE_ID"]).Order());
        Assert.Equal(new object?[] { "Mada", "Visa" }, rows.Select(row => row["PAY_TYPE_NAME"]).Order());
        Assert.All(rows, row => Assert.Equal(new[] { "PAY_TYPE_ID", "PAY_TYPE_NAME" }, row.Keys));
    }

    [Theory]
    [Trait("Decision", "D-49")]
    [InlineData(0, 0, 0, 0, DateTimeKind.Unspecified)]
    [InlineData(10, 15, 0, 0, DateTimeKind.Unspecified)]
    [InlineData(23, 59, 59, 999, DateTimeKind.Utc)]
    public async Task LovDateBind_DraftDateWithATimeOfDay_IsADateInputOfItsDatePart(int hour, int minute, int second, int millisecond, DateTimeKind kind)
    {
        var draftDate = new DateTime(2026, 3, 31, hour, minute, second, millisecond, kind);

        DynamicParameters parameters = LovDateParameters(draftDate);

        DateTime bound = parameters.Get<DateTime>(InvDateBind);
        Assert.Equal(ReservationDate, bound);
        Assert.Equal(kind, bound.Kind);

        SqliteParameter added = await AddedInvDateParameter(parameters);
        Assert.Equal(DbType.Date, added.DbType);
        Assert.Equal(ParameterDirection.Input, added.Direction);
        Assert.Equal(ReservationDate, Assert.IsType<DateTime>(added.Value));
    }

    [Theory]
    [Trait("Decision", "D-49")]
    [Trait("Decision", "D-129")]
    [InlineData(10, 15, 0)]
    [InlineData(23, 59, 59)]
    public async Task ReservNoSql_InSqlite_ListsTheDayReservationsForADraftDateWithATimeOfDay(int hour, int minute, int second)
    {
        DateTime draftDate = ReservationDate.Add(new TimeSpan(hour, minute, second));

        var dateOnly = await LovRows(LovQueries.ReservNoSql, WithReservationBinds(LovDateParameters(draftDate)));
        var timed = await LovRows(LovQueries.ReservNoSql, WithReservationBinds(TimedDateParameters(draftDate)));

        Assert.Equal(new object?[] { 1L, 2L }, dateOnly.Select(row => row["RESERV_NO"]).Order());
        Assert.Empty(timed);
    }

    [Theory]
    [Trait("Decision", "D-49")]
    [Trait("Decision", "D-129")]
    [InlineData(10, 15, 0)]
    [InlineData(23, 59, 59)]
    public async Task OffersSql_InSqlite_ListsAnOfferOnItsLastDayForADraftDateWithATimeOfDay(int hour, int minute, int second)
    {
        DateTime draftDate = ReservationDate.Add(new TimeSpan(hour, minute, second));

        var dateOnly = await LovRows(LovQueries.OffersSql, WithOfferBinds(LovDateParameters(draftDate)));
        var timed = await LovRows(LovQueries.OffersSql, WithOfferBinds(TimedDateParameters(draftDate)));

        Assert.Equal(new object?[] { 21L, 22L }, dateOnly.Select(row => row["OFERID"]).Order());
        Assert.All(dateOnly, row => Assert.Equal(new[] { "OFERID", "OFFER_NAME" }, row.Keys));
        Assert.Equal(new object?[] { 22L }, timed.Select(row => row["OFERID"]));
    }

    [Theory]
    [Trait("Decision", "D-107")]
    [InlineData("12345678901", "compCode has 11 characters; at most 10 can be bound.")]
    [InlineData("\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9", "compCode has 12 bytes in UTF-8; at most 10 can be bound.")]
    public void SubCompany_CompCodeOverTenBytes_IsRefusedBeforeAConnectionOpens(string compCode, string text)
    {
        var queries = new LovQueries(new InvoicingDataOptions { ConnectionString = string.Empty });

        var failure = Assert.Throws<ArgumentException>(() => { _ = queries.SubCompany(compCode, OwnCentre); });

        Assert.Equal(nameof(compCode), failure.ParamName);
        Assert.Equal(text, failure.Data[OracleFailureTranslator.BindingRejectionKey]);
    }

    [Theory]
    [Trait("Decision", "D-107")]
    [InlineData(nameof(LovQueries.Company), "infoCenterId", "infoCenterId has 11 characters; at most 10 can be bound.")]
    [InlineData(nameof(LovQueries.SubCompany), "infoCenterId", "infoCenterId has 11 characters; at most 10 can be bound.")]
    [InlineData(nameof(LovQueries.TheClass), "subCompCode", "subCompCode has 11 characters; at most 10 can be bound.")]
    [InlineData(nameof(LovQueries.Doc), "infoCenterId", "infoCenterId has 11 characters; at most 10 can be bound.")]
    [InlineData(nameof(LovQueries.ReservNo), "patientNo", "patientNo has 13 characters; at most 12 can be bound.")]
    [InlineData(nameof(LovQueries.Offers), "infoCenterId", "infoCenterId has 11 characters; at most 10 can be bound.")]
    public void LovQuery_TextBindOverItsWidth_IsRefusedBeforeAConnectionOpens(string method, string paramName, string text)
    {
        var queries = new LovQueries(new InvoicingDataOptions { ConnectionString = string.Empty });
        const string ElevenCharacters = "12345678901";
        Action call = method switch
        {
            nameof(LovQueries.Company) => () => _ = queries.Company(ElevenCharacters),
            nameof(LovQueries.SubCompany) => () => _ = queries.SubCompany(OwnCompany, ElevenCharacters),
            nameof(LovQueries.TheClass) => () => _ = queries.TheClass(ElevenCharacters, OwnCentre),
            nameof(LovQueries.Doc) => () => _ = queries.Doc(ElevenCharacters),
            nameof(LovQueries.ReservNo) => () => _ = queries.ReservNo(ReservationDate, ActiveDoctor, "P123456789012", OwnCentre),
            nameof(LovQueries.Offers) => () => _ = queries.Offers(1, ReservationDate, ElevenCharacters),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
        };

        var failure = Assert.Throws<ArgumentException>(call);

        Assert.Equal(paramName, failure.ParamName);
        Assert.Equal(text, failure.Data[OracleFailureTranslator.BindingRejectionKey]);
    }

    [Fact]
    [Trait("Decision", "D-138")]
    public void DoctorClinic_SelectsTheClinicIdClinicNameAndDoctorNameAndBindsOnlyTheDoctor()
    {
        Assert.StartsWith("SELECTC.CLINICID,C.CLINICNAME,D.DOC_NAMEFROM", Squash(LookupQueries.GetDoctorClinicSql), StringComparison.Ordinal);
        Assert.Equal(new[] { "docId" }, Binds(LookupQueries.GetDoctorClinicSql));
        Assert.Empty(OrKeyword.Matches(LookupQueries.GetDoctorClinicSql));
    }

    [Fact]
    [Trait("Decision", "D-138")]
    public async Task DoctorClinicSql_InSqlite_ReturnsTheDoctorsClinicWithBothNamesAndNoRowForAnUnknownDoctor()
    {
        var known = await LovRows(LookupQueries.GetDoctorClinicSql, new { docId = ActiveDoctor });
        var unknown = await LovRows(LookupQueries.GetDoctorClinicSql, new { docId = 99 });

        var row = Assert.Single(known);
        Assert.Equal(new[] { "CLINICID", "CLINICNAME", "DOC_NAME" }, row.Keys);
        Assert.Equal(5L, row["CLINICID"]);
        Assert.Equal("Clinic five", row["CLINICNAME"]);
        Assert.Equal("Own active", row["DOC_NAME"]);
        Assert.Empty(unknown);
    }

    /// <summary>Maps a private InvoiceHeaderRow holding only INVDATE with the private ToHeaderDraft.</summary>
    /// <param name="invDate">INVDATE of the row.</param>
    /// <returns>The header draft.</returns>
    private static InvoiceHeaderDraft MapHeaderDraft(DateTime? invDate)
    {
        Type? rowType = typeof(InvoiceQueries).GetNestedType(HeaderRowTypeName, BindingFlags.NonPublic);
        MethodInfo? mapper = typeof(InvoiceQueries).GetMethod(HeaderDraftMapperName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(rowType);
        Assert.NotNull(mapper);

        object row = NewRow(rowType);
        SetProperty(row, InvDateColumn, invDate);

        return Assert.IsType<InvoiceHeaderDraft>(
            mapper.Invoke(null, BindingFlags.DoNotWrapExceptions, null, [row], CultureInfo.InvariantCulture));
    }

    /// <summary>Totals select-list term: the zero-defaulted sum of a D_INV line column under the given alias.</summary>
    private static string TotalsTerm(string column, string alias) =>
        "COALESCE(SUM(COALESCE(" + LinesAlias + "." + column + ", 0)), 0) AS " + alias;

    /// <summary>New instance of a private row type.</summary>
    private static object NewRow(Type rowType)
    {
        object? row = Activator.CreateInstance(rowType, nonPublic: true);
        Assert.NotNull(row);

        return row;
    }

    /// <summary>Public instance property names of a row type in declaration order.</summary>
    private static string[] PropertyNames(Type rowType) =>
    [
        .. rowType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(property => property.MetadataToken)
            .Select(property => property.Name),
    ];

    /// <summary>Sets a row property by name.</summary>
    private static void SetProperty(object row, string name, object? value)
    {
        PropertyInfo? property = row.GetType().GetProperty(name);
        Assert.NotNull(property);

        property.SetValue(row, value);
    }

    /// <summary>Text of a public SQL constant of <see cref="InvoiceQueries"/>.</summary>
    private static string Statement(string name)
    {
        FieldInfo? field = typeof(InvoiceQueries).GetField(name, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);

        return Assert.IsType<string>(field.GetRawConstantValue());
    }

    /// <summary>Removes all whitespace and upper-cases invariantly.</summary>
    private static string Squash(string text) => WhitespaceRun.Replace(text, string.Empty).ToUpper(CultureInfo.InvariantCulture);

    /// <summary>Squashed text after the first WHERE keyword.</summary>
    private static string WhereClause(string sql)
    {
        string squashed = Squash(sql);
        int whereIndex = squashed.IndexOf(WhereKeyword, StringComparison.Ordinal);

        Assert.True(whereIndex > 0, "The statement has no WHERE clause.");

        return squashed[(whereIndex + WhereKeyword.Length)..];
    }

    /// <summary>Distinct bind names in order of first appearance.</summary>
    private static string[] Binds(string sql) =>
        BindReference.Matches(sql).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>RecordGroupQuery of a record group of the Form export, with its encoded newlines decoded and its Form bind names mapped to the LovQueries bind names.</summary>
    private static string RecordGroupSelect(string recordGroup)
    {
        XDocument export = XDocument.Load(Path.Combine(FindRepositoryRoot(), FormExportPath));
        XElement group = Assert.Single(
            export.Descendants(),
            element => element.Name.LocalName == "RecordGroup" && (string?)element.Attribute("Name") == recordGroup);
        string? query = (string?)group.Attribute("RecordGroupQuery");
        Assert.NotNull(query);

        string select = query.Replace(EncodedNewline, "\n", StringComparison.Ordinal);
        foreach (var (form, target) in RecordGroupBindNames)
        {
            select = select.Replace(form, target, StringComparison.Ordinal);
        }

        return select;
    }

    /// <summary>Runs a LOV SELECT unchanged over the seeded rows in an in-memory SQLite database with DECODE registered (D-129).</summary>
    /// <returns>The rows keyed by upper-case column name in column order.</returns>
    private static async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LovRows(string sql, object binds)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction(
            "decode",
            (string? value, string first, string? firstResult, string second, string? secondResult) =>
                value == first ? firstResult : value == second ? secondResult : null);

        var rows = await connection.QueryAsync(LovSeed + sql, binds);

        return rows
            .Cast<IDictionary<string, object?>>()
            .Select(row => (IReadOnlyDictionary<string, object?>)row.ToDictionary(
                column => column.Key.ToUpperInvariant(),
                column => column.Value,
                StringComparer.Ordinal))
            .ToArray();
    }

    /// <summary>RESERV_NO binds for the seeded reservation date and patient P1, as <see cref="LovQueries.ReservNo"/> binds them.</summary>
    private static object ReservationBinds(int docId, string infoCenterId) => new
    {
        invDate = ReservationDate,
        docId,
        reservSystem500 = 0,
        patientNo = PatientHolding,
        infoCenterId,
    };

    /// <summary>Parameters holding the invDate bind the private <see cref="LovQueries"/> AddDate builds from a draft date.</summary>
    private static DynamicParameters LovDateParameters(DateTime draftDate)
    {
        MethodInfo? addDate = typeof(LovQueries).GetMethod(AddDateName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(addDate);

        var parameters = new DynamicParameters();
        addDate.Invoke(null, BindingFlags.DoNotWrapExceptions, null, [parameters, InvDateBind, draftDate], CultureInfo.InvariantCulture);

        return parameters;
    }

    /// <summary>Parameters holding invDate as a DATE input that keeps the draft date's time of day.</summary>
    private static DynamicParameters TimedDateParameters(DateTime draftDate)
    {
        var parameters = new DynamicParameters();
        parameters.Add(InvDateBind, draftDate, DbType.Date, ParameterDirection.Input);

        return parameters;
    }

    /// <summary>Adds the RESERV_NO binds other than invDate for the active doctor of the centre and patient P1.</summary>
    private static DynamicParameters WithReservationBinds(DynamicParameters parameters)
    {
        parameters.Add(DocBind, ActiveDoctor);
        parameters.Add(ReservationSystemBind, 0);
        parameters.Add(PatientBind, PatientHolding);
        parameters.Add(InfoCenterBind, OwnCentre);

        return parameters;
    }

    /// <summary>Adds the OFFERS binds other than invDate for pay type 1 at the centre.</summary>
    private static DynamicParameters WithOfferBinds(DynamicParameters parameters)
    {
        parameters.Add(PayTypeBind, 1);
        parameters.Add(InfoCenterBind, OwnCentre);

        return parameters;
    }

    /// <summary>The invDate parameter Dapper adds to the SQLite command of a SELECT of that bind.</summary>
    private static async Task<SqliteParameter> AddedInvDateParameter(DynamicParameters parameters)
    {
        await using var connection = new CommandRecordingConnection();
        await connection.OpenAsync();

        _ = await connection.ExecuteScalarAsync(InvDateEchoSql, parameters);

        SqliteCommand command = Assert.Single(connection.Commands);
        return Assert.Single(command.Parameters.Cast<SqliteParameter>(), parameter => parameter.ParameterName == InvDateBind);
    }

    /// <summary>Nearest directory at or above the test output directory that holds the solution file.</summary>
    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No directory containing {SolutionFileName} was found at or above {AppContext.BaseDirectory}.");
    }

    /// <summary>In-memory SQLite connection that keeps every command created on it.</summary>
    private sealed class CommandRecordingConnection() : SqliteConnection("Data Source=:memory:")
    {
        /// <summary>Commands created on the connection, in creation order.</summary>
        public List<SqliteCommand> Commands { get; } = [];

        /// <summary>Creates a command on the connection and records it.</summary>
        /// <returns>The new command.</returns>
        public override SqliteCommand CreateCommand()
        {
            SqliteCommand command = base.CreateCommand();
            Commands.Add(command);

            return command;
        }
    }
}
