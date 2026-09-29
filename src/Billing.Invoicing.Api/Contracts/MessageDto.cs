namespace Billing.Invoicing.Api.Contracts;

/// <summary>Validation or warning message returned to the client, carrying the legacy <c>MESSAG</c> text.</summary>
public sealed record MessageDto
{
    /// <summary>Legacy item name in upper case, such as <c>PATIENTNO</c> or <c>QTY</c>; <c>null</c> for a form-level message.</summary>
    public string? Field { get; init; }

    /// <summary>Legacy message text, verbatim.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary><c>Blocking</c> or <c>Warning</c>.</summary>
    public string Severity { get; init; } = string.Empty;

    /// <summary>Domain rule id, such as <c>DR-01</c>; <c>null</c> when the message is not from a domain rule.</summary>
    public string? Rule { get; init; }
}
