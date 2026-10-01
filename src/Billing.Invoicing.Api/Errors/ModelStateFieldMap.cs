using System.Text;
using System.Text.RegularExpressions;

namespace Billing.Invoicing.Api.Errors;

/// <summary>Maps known model-state members to legacy items and formats other members as upper-snake field names.</summary>
public static class ModelStateFieldMap
{
    private const string LineItem = "LINE";

    private static readonly Regex PathToken = new(
        @"\['(?<quoted>(?:[^'\\]|\\.)*)'\]|\[""(?<quoted>(?:[^""\\]|\\.)*)""\]|\[(?<index>[^\]]*)\]|(?<member>[^.\[\]]+)",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

    private static readonly Regex EscapedCharacter = new(@"\\(.)", RegexOptions.CultureInvariant);

    private static readonly HashSet<string> FormLevelMembers = new(StringComparer.OrdinalIgnoreCase)
    {
        "draft",
        "request",
        "header",
        "parameters",
    };

    private static readonly Dictionary<string, string> RecordItems = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ClinicId"] = "CLINICID",
        ["DocId"] = "DOCIDX",
        ["DocIdx"] = "DOCIDX",
        ["DocId1"] = "DOCID1",
        ["DraftDate"] = "INVDATE",
        ["InvDate"] = "INVDATE",
        ["FinalDisc"] = "FINALDISC",
        ["FinalDiscPerc"] = "FINALDISC_PERC",
        ["InvTypeId"] = "INVTYPEID",
        ["OferId"] = "OFERID",
        ["OfferId"] = "OFERID",
        ["PackageServiceId"] = "SERVICEID",
        ["PatientNo"] = "PATIENTNO",
        ["PayType"] = "PAYTYPE",
        ["SubPayType"] = "SUB_PAYTYPE",
        ["SubPayType2"] = "SUB_PAYTYPE2",
        ["Lines"] = LineItem,
        ["LineIndex"] = LineItem,
    };

    private static readonly Dictionary<string, string> LineItems = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CatId"] = "CATID",
        ["DiscountType"] = "LDISCT",
        ["FixPay"] = "FIXPAY",
        ["FLIndicator"] = "F_L_INDICATOR",
        ["PackageParentLineId"] = "REF_SERV_ROW",
        ["PackageServiceId"] = "IMP_FROM_PKG",
        ["PayRate"] = "PAYRATE",
        ["PriceOverride"] = "PRICE",
        ["ServiceId"] = "SERVICEID",
        ["TeethNo2"] = "TEETH_NO2",
    };

    /// <summary>Maps known members to legacy item names and formats unknown members as upper-snake field names.</summary>
    /// <param name="modelStateKey">JSON path, such as <c>$.draft.lines[0].qty</c>, or member path, such as <c>Draft.DISC_T</c> or <c>payType</c>.</param>
    /// <returns>The mapped legacy item name or an upper-snake fallback for unknown members; <c>null</c> for a form-level key.</returns>
    public static string? FieldOf(string? modelStateKey)
    {
        if (string.IsNullOrWhiteSpace(modelStateKey))
        {
            return null;
        }

        string? member = null;
        var memberIndexed = false;
        var parentIndexed = false;

        foreach (Match token in PathToken.Matches(modelStateKey))
        {
            if (token.Groups["index"].Success)
            {
                memberIndexed = true;
                continue;
            }

            parentIndexed = memberIndexed;
            memberIndexed = false;
            member = token.Groups["quoted"].Success
                ? EscapedCharacter.Replace(token.Groups["quoted"].Value, "$1")
                : token.Groups["member"].Value;
        }

        if (memberIndexed)
        {
            return LineItem;
        }

        if (member is null || !IsIdentifier(member) || FormLevelMembers.Contains(member))
        {
            return null;
        }

        if (IsLegacyItem(member))
        {
            return member;
        }

        var items = parentIndexed ? LineItems : RecordItems;
        return items.TryGetValue(member, out var item) ? item : UpperSnake(member);
    }

    /// <summary>Whether a member name is non-empty and holds only ASCII letters, digits and underscores.</summary>
    /// <param name="member">Member name.</param>
    private static bool IsIdentifier(string member) =>
        member.Length > 0 && member.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>Whether a member name is already an upper-case legacy item name.</summary>
    /// <param name="member">Non-empty identifier.</param>
    private static bool IsLegacyItem(string member) =>
        member.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_');

    /// <summary>Converts a Pascal- or camel-case member name to upper snake case.</summary>
    /// <param name="member">Non-empty identifier.</param>
    /// <returns>The member name formatted in upper snake case.</returns>
    private static string UpperSnake(string member)
    {
        var builder = new StringBuilder(member.Length * 2);
        var wordLength = 0;
        for (var i = 0; i < member.Length; i++)
        {
            var current = member[i];
            if (i > 0 && StartsWord(member[i - 1], current, i + 1 < member.Length ? member[i + 1] : '\0', wordLength))
            {
                builder.Append('_');
                wordLength = 0;
            }

            builder.Append(char.ToUpperInvariant(current));
            wordLength = current == '_' ? 0 : wordLength + 1;
        }

        return builder.ToString();
    }

    /// <summary>Whether <paramref name="current"/> starts a new word of a Pascal- or camel-case name.</summary>
    /// <param name="previous">Preceding character.</param>
    /// <param name="current">Character tested.</param>
    /// <param name="next">Following character, or <c>'\0'</c> at the end of the name.</param>
    /// <param name="wordLength">Length of the word <paramref name="previous"/> ends.</param>
    private static bool StartsWord(char previous, char current, char next, int wordLength) =>
        char.IsAsciiLetterUpper(current)
            ? char.IsAsciiLetterLower(previous)
                || char.IsAsciiDigit(previous)
                || (char.IsAsciiLetterUpper(previous) && char.IsAsciiLetterLower(next))
            : char.IsAsciiDigit(current) && char.IsAsciiLetterLower(previous) && wordLength > 1;
}
