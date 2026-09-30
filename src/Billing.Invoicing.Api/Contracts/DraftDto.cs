using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Api.Contracts;

/// <summary>Unsaved invoice draft exchanged between client and server.</summary>
public sealed record DraftDto : IValidatableObject
{
    private const int ValueMode = 0;
    private const int PercentMode = 1;
    private const string UnsupportedDiscountMode = "DISC_T must be 0 (Value Disc) or 1 (Rate Disc)";
    private const string UndefinedDiscountLimitChoice =
        $"{nameof(DiscountLimitChoice)} must be {nameof(Billing.Invoicing.Domain.Model.DiscountLimitChoice.MaximumDiscount)} or {nameof(Billing.Invoicing.Domain.Model.DiscountLimitChoice.Cancel)}";
    private const string DiscTItem = "DISC_T";
    private const string FinalDiscPercItem = "FINALDISC_PERC";
    private const string FinalDiscItem = "FINALDISC";
    private const string Amount1Item = "AMOUNT_1";
    private const string Amount2Item = "AMOUNT_2";
    private const string CashPayedItem = "CASH_PAYED";

    private static readonly decimal MaxAmount = decimal.MaxValue / 4m;
    private static readonly string MaxAmountText = MaxAmount.ToString(CultureInfo.InvariantCulture);

    /// <summary>32-character upper-case hexadecimal request id, kept for the life of the draft.</summary>
    public string RequestId { get; init; } = string.Empty;

    /// <summary>Database time read when the draft was created.</summary>
    public DateTime DraftDate { get; init; }

    /// <summary>The <c>T_INV</c> header.</summary>
    public InvoiceHeaderDraft Header { get; init; } = new();

    /// <summary>The <c>D_INV</c> lines in grid order; a line's zero-based position is its line index.</summary>
    public IReadOnlyList<InvoiceLineDraft> Lines { get; init; } = [];

    /// <summary>The Form entry parameters set by the calling module.</summary>
    public InvoiceEntryParameters Parameters { get; init; } = new();

    /// <summary>The operator's answer to the maximum-discount prompt; null when none was given.</summary>
    public Billing.Invoicing.Domain.Model.DiscountLimitChoice? DiscountLimitChoice { get; init; }

    /// <summary>Rejects an unsupported DISC_T or DiscountLimitChoice and an out-of-bound AMOUNT_1, AMOUNT_2 or CASH_PAYED, naming the legacy item.</summary>
    IEnumerable<ValidationResult> IValidatableObject.Validate(ValidationContext validationContext)
    {
        var header = Header;

        if (header?.DiscT is { } mode && mode is not (ValueMode or PercentMode))
        {
            yield return new ValidationResult(UnsupportedDiscountMode, [DiscTItem]);
        }

        if (DiscountLimitChoice is { } choice && !Enum.IsDefined(choice))
        {
            var discountItem = header?.DiscT == PercentMode ? FinalDiscPercItem : FinalDiscItem;
            yield return new ValidationResult(UndefinedDiscountLimitChoice, [discountItem]);
        }

        if (header is null)
        {
            yield break;
        }

        (decimal? Value, string Item)[] amounts =
        [
            (header.Amount1, Amount1Item),
            (header.Amount2, Amount2Item),
            (header.CashPayed, CashPayedItem),
        ];

        foreach (var (value, item) in amounts)
        {
            if (value is { } amount && Math.Abs(amount) > MaxAmount)
            {
                yield return new ValidationResult($"{item} must be between -{MaxAmountText} and {MaxAmountText}", [item]);
            }
        }
    }
}
