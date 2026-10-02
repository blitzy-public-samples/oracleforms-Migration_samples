using System.Globalization;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Domain.Rules;

/// <summary>DR-06: limits the final-discount percent to the operator's maximum discount (T039, T041, DISC_ALERT).</summary>
public static class FinalDiscountLimitRule
{
    private const string MaximumDiscountAllowed = "Maximum discount allawed is";
    private const string UnsupportedDiscountMode = "DISC_T must be 0 (Value Disc) or 1 (Rate Disc)";
    private const string RuleId = "DR-06";
    private const string FinalDiscPercItem = "FINALDISC_PERC";
    private const string FinalDiscItem = "FINALDISC";
    private const string DiscTItem = "DISC_T";
    private const int PercentMode = 1;
    private const int ValueMode = 0;

    /// <summary>Checks the entered final discount against the operator's maximum discount.</summary>
    /// <param name="header">Draft header supplying DISC_T, FINALDISC_PERC, FINALDISC and OFERID.</param>
    /// <param name="maxDisc">The operator's <c>USERS_TABLE.MAX_DISC</c>; null counts as 0.</param>
    /// <param name="patPay">Patient share the value-mode percent is derived from; null or not positive skips the value-mode check.</param>
    /// <returns>A blocking DISC_T message when DISC_T is neither 0 nor 1; a blocking 'Maximum discount allawed is' message, which
    /// needs a <see cref="DiscountLimitChoice"/>, when the percent exceeds the limit and the header has no offer; in value mode
    /// otherwise FINALDISC_PERC set to the derived percent, or to 0 when FINALDISC is empty; otherwise no messages and no adjustments.</returns>
    public static RuleResult Evaluate(InvoiceHeaderDraft header, decimal? maxDisc, decimal? patPay)
    {
        ArgumentNullException.ThrowIfNull(header);

        var limit = maxDisc ?? 0m;

        if (header.DiscT is { } mode && mode is not (PercentMode or ValueMode))
        {
            return new RuleResult
            {
                Messages = [new ValidationMessage(DiscTItem, UnsupportedDiscountMode, ValidationMessage.Blocking, RuleId)],
                Adjusted = Adjust(),
            };
        }

        if (IsPercentMode(header))
        {
            return header.FinalDiscPerc is { } percent && limit < percent && header.OferId is null
                ? Blocked(FinalDiscPercItem, limit, Adjust())
                : RuleResult.Empty;
        }

        var finalDisc = header.FinalDisc ?? 0m;

        if (finalDisc == 0m)
        {
            return new RuleResult { Adjusted = Adjust((FinalDiscPercItem, 0m)) };
        }

        if (patPay is not { } share || share <= 0m)
        {
            return RuleResult.Empty;
        }

        var derived = DerivedPercent(finalDisc, share);
        var exceeds = derived is null ? finalDisc > 0m : limit < derived.Value;

        if (header.OferId is null && exceeds)
        {
            return Blocked(FinalDiscItem, limit, Adjust());
        }

        return derived is { } value
            ? new RuleResult { Adjusted = Adjust((FinalDiscPercItem, value)) }
            : RuleResult.Empty;
    }

    /// <summary>Applies the operator's answer to the maximum-discount alert.</summary>
    /// <param name="header">Draft header supplying DISC_T.</param>
    /// <param name="choice">The alert button the operator chose.</param>
    /// <param name="maxDisc">The operator's <c>USERS_TABLE.MAX_DISC</c>; null counts as 0.</param>
    /// <returns><see cref="DiscountLimitChoice.Cancel"/>: the blocking alert message with FINALDISC and FINALDISC_PERC set to 0.
    /// <see cref="DiscountLimitChoice.MaximumDiscount"/>: FINALDISC_PERC set to the limit, FINALDISC cleared and DISC_T set to 1.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="choice"/> is not a defined alert button.</exception>
    public static RuleResult ApplyChoice(InvoiceHeaderDraft header, DiscountLimitChoice choice, decimal? maxDisc)
    {
        ArgumentNullException.ThrowIfNull(header);

        var limit = maxDisc ?? 0m;

        return choice switch
        {
            DiscountLimitChoice.Cancel => Blocked(
                IsPercentMode(header) ? FinalDiscPercItem : FinalDiscItem,
                limit,
                Adjust((FinalDiscItem, 0m), (FinalDiscPercItem, 0m))),
            DiscountLimitChoice.MaximumDiscount => new RuleResult
            {
                Adjusted = Adjust((FinalDiscPercItem, limit), (FinalDiscItem, null), (DiscTItem, PercentMode)),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(choice), choice, "Unknown discount limit choice."),
        };
    }

    private static bool IsPercentMode(InvoiceHeaderDraft header) => (header.DiscT ?? ValueMode) == PercentMode;

    private static decimal? DerivedPercent(decimal finalDisc, decimal patPay)
    {
        try
        {
            return Money.Round2(finalDisc / patPay * 100m);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static RuleResult Blocked(string field, decimal limit, IReadOnlyDictionary<string, object?> adjusted) => new()
    {
        Messages = [new ValidationMessage(field, AlertText(limit), ValidationMessage.Blocking, RuleId)],
        Adjusted = adjusted,
    };

    private static IReadOnlyDictionary<string, object?> Adjust(params (string Item, object? Value)[] values)
    {
        var adjusted = new Dictionary<string, object?>(values.Length, StringComparer.Ordinal);
        foreach (var (item, value) in values)
        {
            adjusted[item] = value;
        }

        return adjusted.AsReadOnly();
    }

    private static string AlertText(decimal limit) => MaximumDiscountAllowed + NumberText(limit);

    private static string NumberText(decimal value)
    {
        if (value == 0m)
        {
            return "0";
        }

        var text = value.ToString("0.############################", CultureInfo.InvariantCulture);

        if (text.StartsWith("0.", StringComparison.Ordinal))
        {
            return text[1..];
        }

        if (text.StartsWith("-0.", StringComparison.Ordinal))
        {
            return "-" + text[2..];
        }

        return text;
    }
}
