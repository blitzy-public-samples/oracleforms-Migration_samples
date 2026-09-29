namespace Billing.Invoicing.Domain.Model;

/// <summary>Invoice preview totals as returned by <c>BIL_INVOICE_API.t_preview_totals</c>.</summary>
public sealed record PreviewTotals
{
    /// <summary>Number of lines in the calculated preview (<c>line_count</c>).</summary>
    public int? LineCount { get; init; }

    /// <summary>Sum of line gross amounts (<c>total_gross</c>).</summary>
    public decimal? TotalGross { get; init; }

    /// <summary>Sum of line discount amounts (<c>total_discount</c>).</summary>
    public decimal? TotalDiscount { get; init; }

    /// <summary>Sum of line net amounts (<c>total_net</c>).</summary>
    public decimal? TotalNet { get; init; }

    /// <summary>Patient share before VAT (<c>pat_pay</c>).</summary>
    public decimal? PatPay { get; init; }

    /// <summary>Company share before VAT (<c>comp_pay</c>).</summary>
    public decimal? CompPay { get; init; }

    /// <summary>VAT charged to the patient (<c>vat_total_pat</c>).</summary>
    public decimal? VatTotalPat { get; init; }

    /// <summary>VAT charged to the company (<c>vat_total_co</c>).</summary>
    public decimal? VatTotalCo { get; init; }

    /// <summary>Amount due from the patient (<c>cash_collected</c>).</summary>
    public decimal? CashCollected { get; init; }

    /// <summary>Amount paid by payment method 1 (<c>amount_1</c>).</summary>
    public decimal? Amount1 { get; init; }

    /// <summary>Amount paid by payment method 2 (<c>amount_2</c>).</summary>
    public decimal? Amount2 { get; init; }

    /// <summary>Amount due minus amounts 1 and 2; negative when overpaid (<c>remaining_amount</c>).</summary>
    public decimal? RemainingAmount { get; init; }

    /// <summary>Payment status: No Amount Due, Unpaid, Partial, Paid or Overpaid (<c>payment_status</c>).</summary>
    public string? PaymentStatus { get; init; }
}
