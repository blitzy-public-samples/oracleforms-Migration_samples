namespace Billing.Invoicing.Domain.Model;

/// <summary>Invoice payment split across two payment methods, with cash tendered and refund.</summary>
public sealed record PaymentAllocation
{
    /// <summary>Amount on payment method 1 (<c>AMOUNT_1</c>).</summary>
    public decimal? Amount1 { get; init; }

    /// <summary>Amount on payment method 2 (<c>AMOUNT_2</c>).</summary>
    public decimal? Amount2 { get; init; }

    /// <summary>Cash tendered by the patient (<c>CASH_PAYED</c>).</summary>
    public decimal? CashPayed { get; init; }

    /// <summary>Refund due to the patient (<c>REUND</c>).</summary>
    public decimal? Reund { get; init; }

    /// <summary>Payment method 1 (<c>SUB_PAYTYPE</c>); 1 is cash.</summary>
    public int? SubPayType { get; init; }

    /// <summary>Payment method 2 (<c>SUB_PAYTYPE2</c>); 1 is cash.</summary>
    public int? SubPayType2 { get; init; }
}
