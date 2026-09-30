using System.Globalization;
using System.Text.RegularExpressions;
using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Data.Queries;

namespace Billing.Invoicing.Tests.Data;

/// <summary>T082 and T085 query predicates of <see cref="InvoiceQueries"/> (D-26) and the D-12 approval check mode of <see cref="BilImportGateway"/>.</summary>
[Trait("Category", "DataUnit")]
public sealed class DataContractTests
{
    private const string WhereKeyword = "WHERE";

    private const string InfoCenterBind = "infoCenterId";
    private const string PatientBind = "patientNo";
    private const string VisitBind = "visitUnique";
    private const string PayTypeBind = "payType";

    private static readonly string[] RequestRowApprovalColumns = ["REQ_A_STATUS", "REQ_NEED_A", "APPROV_REF_NO"];

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
