namespace Billing.Invoicing.Domain.Model;

/// <summary>The operator's answer to the DISC_ALERT maximum-discount alert.</summary>
public enum DiscountLimitChoice
{
    /// <summary>Set the percent to the user's maximum.</summary>
    MaximumDiscount,

    /// <summary>Zero percent and amount and block.</summary>
    Cancel
}
