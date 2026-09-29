namespace Billing.Invoicing.Domain.Model;

/// <summary>A message raised by a domain rule, carrying the legacy text shown by <c>MESSAG</c>.</summary>
/// <param name="Field">Legacy item name in upper case, such as <c>PATIENTNO</c> or <c>QTY</c>; <c>null</c> for a form-level message.</param>
/// <param name="Text">Legacy message text, verbatim.</param>
/// <param name="Severity"><see cref="Blocking"/> or <see cref="Warning"/>.</param>
/// <param name="Rule">Domain rule id, such as <c>DR-05</c>.</param>
public sealed record ValidationMessage(string? Field, string Text, string Severity, string Rule)
{
    /// <summary>Severity of a message that stops the action.</summary>
    public const string Blocking = "Blocking";

    /// <summary>Severity of a message that lets the action proceed.</summary>
    public const string Warning = "Warning";
}
