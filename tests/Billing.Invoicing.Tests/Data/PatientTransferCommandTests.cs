using System.Globalization;
using System.Text.RegularExpressions;
using Billing.Invoicing.Data.Commands;

namespace Billing.Invoicing.Tests.Data;

/// <summary>Statement shape of the DR-21 reception-transfer clear in <see cref="PatientTransferCommand"/> (D-44).</summary>
[Trait("Category", "DataUnit")]
public sealed class PatientTransferCommandTests
{
    private const string SetKeyword = " SET ";
    private const string WhereKeyword = " WHERE ";

    private static readonly string[] ClearedColumns = ["NEW_INV_CATID", "NEW_INV_CLINICID", "NEW_INV_DOCID", "NEW_INV_SERVICEID"];

    private static readonly string[] GuardPredicates = ["NEW_INV_DOCIDISNOTNULL", "PATIENTNO=:PATIENTNO"];

    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.CultureInvariant);

    private static readonly Regex BindReference = new(@":(\w+)", RegexOptions.CultureInvariant);

    private static readonly Regex UpdateKeyword = new(@"\bUPDATE\b", RegexOptions.CultureInvariant);

    private static readonly Regex OtherStatementKeyword = new(@"\b(?:SELECT|INSERT|DELETE|MERGE)\b", RegexOptions.CultureInvariant);

    private static readonly Regex AndKeyword = new(@"\bAND\b", RegexOptions.CultureInvariant);

    private static readonly Regex OrKeyword = new(@"\bOR\b", RegexOptions.CultureInvariant);

    [Fact]
    public void Statement_IsASingleUpdateOfPatient()
    {
        string sql = Normalize(PatientTransferCommand.ClearReceptionTransferSql);
        string body = sql.EndsWith(';') ? sql[..^1] : sql;

        Assert.StartsWith("UPDATE PATIENT SET ", sql, StringComparison.Ordinal);
        Assert.Single(UpdateKeyword.Matches(sql));
        Assert.Empty(OtherStatementKeyword.Matches(sql));
        Assert.DoesNotContain(";", body, StringComparison.Ordinal);
    }

    [Fact]
    public void SetList_AssignsNullToExactlyTheFourNewInvColumns()
    {
        string[] assignments = SetList().Split(',');

        Assert.Equal(4, assignments.Length);

        var columns = new List<string>();
        foreach (string assignment in assignments)
        {
            string[] sides = assignment.Split('=');

            Assert.Equal(2, sides.Length);
            Assert.Equal("NULL", sides[1].Trim());
            columns.Add(sides[0].Trim());
        }

        Assert.Equal(ClearedColumns, columns.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void WhereClause_MatchesThePatientAndRequiresNewInvDocId()
    {
        string where = WhereClause();
        string stripped = StripWhitespace(where);

        Assert.Contains("PATIENTNO=:PATIENTNO", stripped, StringComparison.Ordinal);
        Assert.Contains("NEW_INV_DOCIDISNOTNULL", stripped, StringComparison.Ordinal);
        Assert.Empty(OrKeyword.Matches(where));

        string[] predicates = AndKeyword.Split(where).Select(StripWhitespace).ToArray();

        Assert.Equal(GuardPredicates, predicates.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Binds_OnlyPatientNo()
    {
        string[] binds = BindReference
            .Matches(PatientTransferCommand.ClearReceptionTransferSql)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "patientNo" }, binds);
    }

    /// <summary>Collapses whitespace runs to one space, trims, and upper-cases invariantly.</summary>
    private static string Normalize(string text) =>
        WhitespaceRun.Replace(text, " ").Trim().ToUpper(CultureInfo.InvariantCulture);

    /// <summary>Removes every whitespace character.</summary>
    private static string StripWhitespace(string text) => WhitespaceRun.Replace(text, string.Empty);

    /// <summary>Normalised text between the SET and WHERE keywords.</summary>
    private static string SetList()
    {
        string sql = Normalize(PatientTransferCommand.ClearReceptionTransferSql);
        int setIndex = sql.IndexOf(SetKeyword, StringComparison.Ordinal);
        int whereIndex = sql.IndexOf(WhereKeyword, StringComparison.Ordinal);

        Assert.True(setIndex >= 0, "SET keyword not found.");
        Assert.True(whereIndex > setIndex, "WHERE keyword not found after SET.");

        return sql[(setIndex + SetKeyword.Length)..whereIndex];
    }

    /// <summary>Normalised text after the WHERE keyword, without a trailing semicolon.</summary>
    private static string WhereClause()
    {
        string sql = Normalize(PatientTransferCommand.ClearReceptionTransferSql);
        int whereIndex = sql.IndexOf(WhereKeyword, StringComparison.Ordinal);

        Assert.True(whereIndex >= 0, "WHERE keyword not found.");

        return sql[(whereIndex + WhereKeyword.Length)..].TrimEnd(';').Trim();
    }
}
