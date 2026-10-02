using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Domain.Rules;

/// <summary>DR-05: restricts the ER / department-wise flag to ER clinics (T032, T033).</summary>
public static class ErClinicRule
{
    private const string GpAtErOnly = "This open for GP at ER Clinic Only";

    private const string ErCategory = "ER";

    /// <summary>Checks the draft's DEPT_WISE flag against the clinic's category.</summary>
    /// <param name="header">Draft header supplying DEPT_WISE.</param>
    /// <param name="clinic">Profile of the draft's clinic; null, or a SYS_CAT_TYPE other than 'ER', is a non-ER clinic.</param>
    /// <returns>A blocking DEPT_WISE message when DEPT_WISE is 1 and the clinic is not an ER clinic; otherwise <see cref="RuleResult.Empty"/>.</returns>
    public static RuleResult Validate(InvoiceHeaderDraft header, ClinicProfile? clinic)
    {
        ArgumentNullException.ThrowIfNull(header);

        var isEr = string.Equals(clinic?.SysCatType, ErCategory, StringComparison.Ordinal);

        if ((header.DeptWise ?? 0) == 1 && !isEr)
        {
            return new RuleResult
            {
                Messages = new[]
                {
                    new ValidationMessage("DEPT_WISE", GpAtErOnly, ValidationMessage.Blocking, "DR-05"),
                },
            };
        }

        return RuleResult.Empty;
    }
}
