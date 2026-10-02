using System.Text;
using System.Text.RegularExpressions;
using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Domain.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Billing.Invoicing.Api.Errors;

/// <summary>Maps known model-state members to legacy items, formats other members as upper-snake field names, and builds the messages of rejected model state.</summary>
public static class ModelStateFieldMap
{
    private const string LineItem = "LINE";
    private const string InvalidInputText = "The input was not valid.";

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

    /// <summary>Builds one blocking message per model-state error, leaving out the body parameter's own errors when any other key holds one.</summary>
    /// <param name="context">Context of the rejected action.</param>
    /// <returns>The messages in model-state order, each naming its field through <see cref="FieldOf"/>.</returns>
    public static MessageDto[] MessagesOf(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bodyKeys = context.ActionDescriptor.Parameters
            .Where(parameter => parameter.BindingInfo?.BindingSource == BindingSource.Body)
            .Select(parameter => parameter.BindingInfo!.BinderModelName ?? parameter.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .SelectMany(entry => entry.Value!.Errors.Select(error => (entry.Key, error.ErrorMessage)))
            .ToList();
        var deeperError = errors.Exists(error => !bodyKeys.Contains(error.Key));

        return errors
            .Where(error => !deeperError || !bodyKeys.Contains(error.Key))
            .Select(error => new MessageDto
            {
                Field = FieldOf(error.Key),
                Text = string.IsNullOrEmpty(error.ErrorMessage) ? InvalidInputText : error.ErrorMessage,
                Severity = ValidationMessage.Blocking,
            })
            .ToArray();
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
