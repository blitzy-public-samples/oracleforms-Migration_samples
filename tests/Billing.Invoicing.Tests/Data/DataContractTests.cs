using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Data.Queries;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Tests.Data;

/// <summary>T_INV block, T082 and T085 query predicates, the D-12 selected-row mapping (D-26) and the D-93 saved-invoice projection of <see cref="InvoiceQueries"/>, and the D-12 approval check mode of <see cref="BilImportGateway"/>.</summary>
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
    private const string ReqAStatusColumn = "REQ_A_STATUS";
    private const string ReqNeedAColumn = "REQ_NEED_A";
    private const string ApprovRefNoColumn = "APPROV_REF_NO";
    private const string ClaimBind = "claimNo";

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
}
