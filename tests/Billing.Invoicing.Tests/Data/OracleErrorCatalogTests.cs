using Billing.Invoicing.Data.Errors;

namespace Billing.Invoicing.Tests.Data;

/// <summary>Shape and uniqueness tests for <see cref="OracleErrorCatalog.Rows"/>.</summary>
[Trait("Category", "DataUnit")]
public sealed class OracleErrorCatalogTests
{
    private const int MinApplicationErrorNumber = -20999;
    private const int MaxApplicationErrorNumber = -20000;
    private const int ReusedImportNumber = -20771;
    private const string RequestImportPrefix = "Request import failed: selected request line";
    private const string PackageExpansionPrefix = "Request package expansion failed: multiplied quantity";

    [Fact]
    public void Rows_AreNotEmpty()
    {
        Assert.NotEmpty(OracleErrorCatalog.Rows);
    }

    [Fact]
    public void Rows_AreUniqueOnPackageNumberAndPrefix()
    {
        var duplicates = OracleErrorCatalog.Rows
            .GroupBy(row => (row.Package, row.Number, row.MessagePrefix))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.Package}|{group.Key.Number}|{group.Key.MessagePrefix} (x{group.Count()})")
            .ToList();

        Assert.True(
            duplicates.Count == 0,
            "Duplicate (Package, Number, MessagePrefix) keys: " + string.Join("; ", duplicates));
    }

    [Fact]
    public void Rows_AreUniqueOnNumberAndPrefix()
    {
        var duplicates = OracleErrorCatalog.Rows
            .GroupBy(row => (row.Number, row.MessagePrefix))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.Number}|{group.Key.MessagePrefix} in "
                + string.Join(", ", group.Select(row => row.Package)))
            .ToList();

        Assert.True(
            duplicates.Count == 0,
            "Duplicate (Number, MessagePrefix) keys: " + string.Join("; ", duplicates));
    }

    [Fact]
    public void Rows_UseSignedApplicationErrorRange()
    {
        var outOfRange = OracleErrorCatalog.Rows
            .Where(row => row.Number < MinApplicationErrorNumber || row.Number > MaxApplicationErrorNumber)
            .Select(Describe)
            .ToList();

        Assert.True(
            outOfRange.Count == 0,
            $"Numbers outside {MinApplicationErrorNumber} to {MaxApplicationErrorNumber}: " + string.Join("; ", outOfRange));
    }

    [Fact]
    public void Rows_NameKnownPackages()
    {
        Assert.Equal("BIL_INVOICE_ENGINE", OracleErrorCatalog.EnginePackage);
        Assert.Equal("BIL_INVOICE_API", OracleErrorCatalog.ApiPackage);
        Assert.Equal("BIL_IMPORT", OracleErrorCatalog.ImportPackage);

        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            OracleErrorCatalog.EnginePackage,
            OracleErrorCatalog.ApiPackage,
            OracleErrorCatalog.ImportPackage,
        };

        var unknown = OracleErrorCatalog.Rows
            .Where(row => row.Package is null || !known.Contains(row.Package))
            .Select(Describe)
            .ToList();

        Assert.True(unknown.Count == 0, "Rows naming an unknown package: " + string.Join("; ", unknown));
    }

    [Fact]
    public void Rows_CarryMessagePrefix()
    {
        var blank = OracleErrorCatalog.Rows
            .Where(row => string.IsNullOrWhiteSpace(row.MessagePrefix))
            .Select(Describe)
            .ToList();

        Assert.True(blank.Count == 0, "Rows without a message prefix: " + string.Join("; ", blank));
    }

    [Fact]
    public void ImportMinus20771_RowsCarryDistinctPrefixes()
    {
        var prefixes = OracleErrorCatalog.Rows
            .Where(row => string.Equals(row.Package, OracleErrorCatalog.ImportPackage, StringComparison.Ordinal)
                && row.Number == ReusedImportNumber)
            .Select(row => row.MessagePrefix)
            .ToList();

        Assert.True(prefixes.Count >= 2, $"Expected at least two BIL_IMPORT {ReusedImportNumber} rows, found {prefixes.Count}.");
        Assert.Equal(prefixes.Count, prefixes.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(prefixes, prefix => prefix.StartsWith(RequestImportPrefix, StringComparison.Ordinal));
        Assert.Contains(prefixes, prefix => prefix.StartsWith(PackageExpansionPrefix, StringComparison.Ordinal));
    }

    private static string Describe(
        (string Package, int Number, string MessagePrefix, string? Kind, string? Field, string? LegacyText) row) =>
        $"{row.Package}|{row.Number}|{row.MessagePrefix}";
}
