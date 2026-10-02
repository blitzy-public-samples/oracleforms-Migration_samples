using System.Collections.ObjectModel;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Domain.Rules;

/// <summary>DR-07, DR-08, DR-09: splits the amount due between payment methods 1 and 2 and computes the refund and total collected (T039, T041, T042, T065, PU30, CASH_COLLECTED).</summary>
public static class PaymentAllocationRules
{
    private const string Amount1Item = "AMOUNT_1";
    private const string Amount2Item = "AMOUNT_2";
    private const int CashPaymentMethod = 1;

    /// <summary>Returns amount 2 after amount 1 is edited: amount due minus amount 1, nulls read as 0.</summary>
    /// <param name="amountDue">Amount due for the invoice (the package's <c>cash_collected</c>).</param>
    /// <param name="amount1">Amount on payment method 1 (<c>AMOUNT_1</c>).</param>
    /// <returns>The new <c>AMOUNT_2</c>, unrounded; negative when amount 1 exceeds the amount due.</returns>
    public static decimal AllocateSecondAmount(decimal? amountDue, decimal? amount1) =>
        (amountDue ?? 0m) - (amount1 ?? 0m);

    /// <summary>Resets the payment split after a final-discount change: amount 1 to the amount due rounded to 2 places, amount 2 to 0.</summary>
    /// <param name="amountDue">Amount due for the invoice (the package's <c>cash_collected</c>).</param>
    /// <returns>A result with no messages whose <see cref="RuleResult.Adjusted"/> holds <c>AMOUNT_1</c> (null when the amount due is null) and <c>AMOUNT_2</c> = 0.</returns>
    public static RuleResult ResetAfterDiscountChange(decimal? amountDue)
    {
        decimal? amount1 = amountDue is null ? null : Money.Round2(amountDue.Value);

        var adjusted = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [Amount1Item] = amount1,
            [Amount2Item] = 0m,
        };

        return new RuleResult { Adjusted = new ReadOnlyDictionary<string, object?>(adjusted) };
    }

    /// <summary>Returns the default amount 1 once the invoice lines are complete: the amount due, unchanged.</summary>
    /// <param name="amountDue">Amount due for the invoice (the package's <c>cash_collected</c>).</param>
    /// <returns>The new <c>AMOUNT_1</c>; null when the amount due is null.</returns>
    public static decimal? DefaultFirstAmount(decimal? amountDue) => amountDue;

    /// <summary>Returns the refund due to the patient: cash tendered minus the positive amounts on cash payment methods, or 0 when either is not positive.</summary>
    /// <param name="allocation">Payment split with cash tendered and the two payment methods.</param>
    /// <returns>The <c>REUND</c> value; negative when the cash tendered is below the cash amounts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="allocation"/> is null.</exception>
    public static decimal Refund(PaymentAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);

        var cashAmounts = CashAmount(allocation.SubPayType, allocation.Amount1)
                          + CashAmount(allocation.SubPayType2, allocation.Amount2);
        var cashPayed = allocation.CashPayed ?? 0m;

        return cashPayed > 0m && cashAmounts > 0m ? cashPayed - cashAmounts : 0m;
    }

    /// <summary>Returns the total collected across both payment methods, nulls read as 0.</summary>
    /// <param name="amount1">Amount on payment method 1 (<c>AMOUNT_1</c>).</param>
    /// <param name="amount2">Amount on payment method 2 (<c>AMOUNT_2</c>).</param>
    /// <returns>Amount 1 plus amount 2.</returns>
    public static decimal TotalCollected(decimal? amount1, decimal? amount2) =>
        (amount1 ?? 0m) + (amount2 ?? 0m);

    private static decimal CashAmount(int? paymentMethod, decimal? amount)
    {
        var value = amount ?? 0m;
        return paymentMethod == CashPaymentMethod && value > 0m ? value : 0m;
    }
}
