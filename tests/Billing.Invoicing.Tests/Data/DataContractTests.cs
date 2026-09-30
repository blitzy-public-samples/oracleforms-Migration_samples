using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Data.Queries;
using Billing.Invoicing.Domain.Model;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Billing.Invoicing.Tests.Data;

/// <summary>T_INV block, T082 and T085 query predicates and the D-12 selected-row mapping of <see cref="InvoiceQueries"/> (D-26), and the D-12 approval check mode of <see cref="BilImportGateway"/>.</summary>
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

    private const string ServicesSource = "FROMSERVICES" + WhereKeyword;

    private const string InvoiceBlockFrom = "FROM T_INV t ";
    private const string InvoiceBlockWhere = "(t.INVTYPEID <> 8 and t.INVTYPEID <> 9) and t.PHARMACY_INV_NO is null";
    private const string InvoiceBlockOrderBy = "ORDER BY t.INV_NO";

    private const string RequestRowTypeName = "RequestRow";
    private const string RequestRowMapperName = "ToSelectedRequestRow";
    private const string RequestRowIdColumn = "PAT_SERV_REQ_ROW_ID";
    private const string ServiceIdColumn = "SERVICEID";
    private const string RequestView = "V_SERVICES_REQ";

    private const string RequestRowSeedHead =
        "WITH V_SERVICES_REQ (PAT_SERV_REQ_ROW_ID, SERVICEID, REQ_A_STATUS, REQ_NEED_A, APPROV_REF_NO, "
        + "PATIENTNO, D_INV_ROW_ID, SELECT_TO_INV, VISIT_UNIQUE, PAY_TYPE) AS (VALUES (";

    private const string RequestRowSeedTail = ", 'P1', NULL, 1, 'V1', 'Cash')) ";
    private const string ClaimBind = "claimNo";

    private static readonly string[] RequestRowApprovalColumns = ["REQ_A_STATUS", "REQ_NEED_A", "APPROV_REF_NO"];

    private static readonly string[] ClaimPreloadCardColumns = ["INS_NUMBER", "CARD_END", "PAT_POLICY_NO"];

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
        Assert.Equal((7001L, "S100", (int?)3, (int?)1, (string?)"REF1"), MapSelectedRequestRow("7001, 'S100', 3, 1, 'REF1'"));
    }

    [Theory]
    [Trait("Decision", "D-12")]
    [InlineData("NULL")]
    [InlineData("''")]
    public void SelectedRequestRow_WithoutServiceId_IsRejectedNamingTheColumn(string serviceId)
    {
        var failure = Assert.Throws<InvalidCastException>(() => MapSelectedRequestRow($"7002, {serviceId}, NULL, NULL, NULL"));

        Assert.Contains(ServiceIdColumn, failure.Message, StringComparison.Ordinal);
        Assert.Contains(RequestView, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(RequestRowIdColumn, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "D-12")]
    public void SelectedRequestRow_WithoutRequestRowId_IsRejectedNamingTheColumn()
    {
        var failure = Assert.Throws<InvalidCastException>(() => MapSelectedRequestRow("NULL, 'S100', 3, 1, 'REF1'"));

        Assert.Contains(RequestRowIdColumn, failure.Message, StringComparison.Ordinal);
        Assert.Contains(RequestView, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "D-12")]
    public void SelectedRequestRow_WithOutOfRangeStatus_IsRejected()
    {
        Assert.Throws<OverflowException>(() => MapSelectedRequestRow("7003, 'S100', 3000000000, 1, NULL"));
    }

    /// <summary>Runs <see cref="InvoiceQueries.GetSelectedRequestRowsSql"/> unchanged in SQLite over one seeded row and maps it with the private ToSelectedRequestRow.</summary>
    /// <param name="requestColumns">SQL literals of PAT_SERV_REQ_ROW_ID, SERVICEID, REQ_A_STATUS, REQ_NEED_A and APPROV_REF_NO.</param>
    /// <returns>The port tuple of the selected row.</returns>
    private static (long, string, int?, int?, string?) MapSelectedRequestRow(string requestColumns)
    {
        Type? rowType = typeof(InvoiceQueries).GetNestedType(RequestRowTypeName, BindingFlags.NonPublic);
        MethodInfo? mapper = typeof(InvoiceQueries).GetMethod(RequestRowMapperName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(rowType);
        Assert.NotNull(mapper);

        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        connection.CreateFunction<string?, string, long, string, long, long?>("DECODE", Decode, isDeterministic: true);

        object row = Assert.Single(connection.Query(
            rowType,
            RequestRowSeedHead + requestColumns + RequestRowSeedTail + InvoiceQueries.GetSelectedRequestRowsSql,
            new { patientNo = "P1", visitUnique = "V1", payType = 1 }));

        object? mapped = mapper.Invoke(null, BindingFlags.DoNotWrapExceptions, null, [row], CultureInfo.InvariantCulture);
        return Assert.IsType<(long, string, int?, int?, string?)>(mapped);
    }

    /// <summary>Oracle DECODE of a value over two search and result pairs; null when neither search matches.</summary>
    private static long? Decode(string? value, string firstSearch, long firstResult, string secondSearch, long secondResult) =>
        string.Equals(value, firstSearch, StringComparison.Ordinal) ? firstResult
        : string.Equals(value, secondSearch, StringComparison.Ordinal) ? secondResult
        : null;

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
}
