using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Queries;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Billing.Invoicing.Tests.Data;

/// <summary>ROW_TYPE restriction of <see cref="InvoiceQueries.GetInvoice"/> and <see cref="InvoiceQueries.GetMoreDetails"/> (D-111).</summary>
[Trait("Category", "DataUnit")]
[Trait("Decision", "D-111")]
public sealed class InvoiceReadRowTypeTests
{
    private const string RowTypePredicate = "t.ROW_TYPE = :rowType";
    private const string OptionalRowType = ":rowType IS NULL";
    private const string LocalDocTypeParameter = "localDocType";
    private const long SavedInvoiceNo = 5L;

    private const string Seed =
        "WITH T_INV (INV_NO, INS_NUMBER, CARD_END, PAT_POLICY_NO, INVTYPEID, PHARMACY_INV_NO, ROW_TYPE) AS (VALUES "
        + "(5, 'INS-5', NULL, 'POL-5', 1, NULL, 1), "
        + "(6, 'INS-6', NULL, 'POL-6', 1, NULL, 2)) ";

    /// <summary>Each header query requires the bound ROW_TYPE and has no branch that drops it.</summary>
    [Theory]
    [InlineData(InvoiceQueries.GetInvoiceSql)]
    [InlineData(InvoiceQueries.GetMoreDetailsSql)]
    public void HeaderQuery_RequiresTheBoundRowType(string sql)
    {
        Assert.Contains(RowTypePredicate, sql, StringComparison.Ordinal);
        Assert.DoesNotContain(OptionalRowType, sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The more-details header query returns the invoice of the bound ROW_TYPE and not the invoice of the other.</summary>
    [Theory]
    [InlineData(1, 5L, 6L)]
    [InlineData(2, 6L, 5L)]
    public async Task MoreDetailsHeader_ReturnsOnlyTheBoundRowType(int rowType, long matching, long other)
    {
        Assert.Equal(matching, await MoreDetailsInvoiceNo(matching, rowType));
        Assert.Null(await MoreDetailsInvoiceNo(other, rowType));
    }

    /// <summary>A LOCAL_DOC_TYPE other than 505, 532 or 783 is rejected before a connection opens.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(999)]
    public async Task UnsupportedLocalDocType_IsRejectedBeforeTheConnectionOpens(int localDocType)
    {
        var queries = new InvoiceQueries(new InvoicingDataOptions());

        var invoice = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => queries.GetInvoice(SavedInvoiceNo, localDocType));
        var details = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => queries.GetMoreDetails(SavedInvoiceNo, localDocType));

        Assert.Equal(LocalDocTypeParameter, invoice.ParamName);
        Assert.Equal(LocalDocTypeParameter, details.ParamName);
    }

    /// <summary>LOCAL_DOC_TYPE 505, 532 and 783 pass the mapping and reach the connection open, which refuses the blank connection string.</summary>
    [Theory]
    [InlineData(505)]
    [InlineData(532)]
    [InlineData(783)]
    public async Task SupportedLocalDocType_ReachesTheConnectionOpen(int localDocType)
    {
        var queries = new InvoiceQueries(new InvoicingDataOptions());

        await Assert.ThrowsAsync<InvalidOperationException>(() => queries.GetInvoice(SavedInvoiceNo, localDocType));
        await Assert.ThrowsAsync<InvalidOperationException>(() => queries.GetMoreDetails(SavedInvoiceNo, localDocType));
    }

    /// <summary>Returns the INV_NO the seeded more-details header query yields for <c>:invNo</c> and <c>:rowType</c>, or null.</summary>
    private static async Task<long?> MoreDetailsInvoiceNo(long invNo, int rowType)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        return await connection.QueryFirstOrDefaultAsync<long?>(Seed + InvoiceQueries.GetMoreDetailsSql, new { invNo, rowType });
    }
}
