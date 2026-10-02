namespace Billing.Invoicing.Domain.Model;

/// <summary>Money rounding for domain rules.</summary>
public static class Money
{
    /// <summary>Rounds an amount to 2 decimal places, midpoints away from zero.</summary>
    /// <param name="x">The amount to round.</param>
    /// <returns>The amount rounded to 2 decimal places.</returns>
    public static decimal Round2(decimal x) => Math.Round(x, 2, MidpointRounding.AwayFromZero);
}
