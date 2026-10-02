using Billing.Invoicing.Data.Queries;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Billing.Invoicing.Tests.Data;

/// <summary>Executes <see cref="InvoiceQueries.GetLastInvoiceNoSql"/> unchanged over rows seeded in an in-memory SQLite database (D-58).</summary>
[Trait("Category", "DataUnit")]
public sealed class LastInvoiceQueryTests
{
    private const string Seed =
        "WITH T_INV (INV_NO, IN_OUT, ROW_TYPE, PHARMACY_INV_NO, INFO_CENTER_ID) AS (VALUES "
        + "(100, 1, 1, NULL, '7'), "
        + "(105, 1, 1, NULL, '7'), "
        + "(110, 2, 1, NULL, '7'), "
        + "(111, 1, 2, NULL, '7'), "
        + "(112, 1, 1, 'PH-1', '7'), "
        + "(113, 1, 1, NULL, '8')) ";

    [Fact]
    public async Task Centre7_ReturnsHighestQualifyingInvoice()
    {
        Assert.Equal(105L, await LastFor("7"));
    }

    [Fact]
    public async Task Centre8_ReturnsItsOwnInvoice()
    {
        Assert.Equal(113L, await LastFor("8"));
    }

    [Fact]
    public async Task CentreWithoutInvoices_ReturnsNull()
    {
        Assert.Null(await LastFor("9"));
    }

    /// <summary>Returns the seeded query's highest invoice number for the centre bound as <c>:infoCenterId</c>, or null.</summary>
    private static async Task<long?> LastFor(string centre)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        return await connection.ExecuteScalarAsync<long?>(Seed + InvoiceQueries.GetLastInvoiceNoSql, new { infoCenterId = centre });
    }
}
