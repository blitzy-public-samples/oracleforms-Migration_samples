using System.Collections.ObjectModel;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Domain.Rules;

/// <summary>DR-11: checks the selected doctor against the doctor the entry parameters lock the draft to (T029).</summary>
public static class DoctorSelectionRules
{
    private const string MustSelectDoctor = "You Must Select Doctor";
    private const string CantChangeDoctor = "You Cant Change doctor";
    private const string DoctorField = "DOCIDX";
    private const string RuleId = "DR-11";

    /// <summary>Validates the draft's doctor (DOCIDX).</summary>
    /// <param name="header">Draft header supplying <see cref="InvoiceHeaderDraft.DocId"/>.</param>
    /// <param name="parameters">Entry parameters supplying <see cref="InvoiceEntryParameters.TheDoc"/>.</param>
    /// <returns>A warning when no doctor is selected; a warning with DOCIDX reset to THE_DOC when THE_DOC is set,
    /// non-zero and differs from the selected doctor; otherwise <see cref="RuleResult.Empty"/>.</returns>
    public static RuleResult Validate(InvoiceHeaderDraft header, InvoiceEntryParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(parameters);

        if (header.DocId is null)
        {
            return new RuleResult
            {
                Messages = new[] { new ValidationMessage(DoctorField, MustSelectDoctor, ValidationMessage.Warning, RuleId) },
            };
        }

        if (parameters.TheDoc is int theDoc && theDoc != 0 && header.DocId.Value != theDoc)
        {
            return new RuleResult
            {
                Messages = new[] { new ValidationMessage(DoctorField, CantChangeDoctor, ValidationMessage.Warning, RuleId) },
                Adjusted = new ReadOnlyDictionary<string, object?>(
                    new Dictionary<string, object?>(StringComparer.Ordinal) { [DoctorField] = theDoc }),
            };
        }

        return RuleResult.Empty;
    }
}
