using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Domain.Rules;

/// <summary>DR-04: warns when the patient's sex or age does not suit the selected clinic (T031).</summary>
public static class ClinicSuitabilityRules
{
    private const string SexNotSuitable = "Patient sex not suitable for this clinic";
    private const string AgeNotSuitable = "Patient age  not suitable for this clinic";
    private const string ClinicField = "CLINICID";
    private const string RuleId = "DR-04";
    private const decimal MinimumAgeYears = 1m;

    /// <summary>Checks the patient's sex against the clinic's sex restriction.</summary>
    /// <param name="clinic">Clinic restrictions and the patient's sex; null when no clinic is selected.</param>
    /// <returns>A warning on CLINICID when both sexes are known and differ; otherwise <see cref="RuleResult.Empty"/>.</returns>
    public static RuleResult CheckSex(ClinicProfile? clinic)
    {
        if (clinic?.Six is not { } clinicSex || clinic.PatientSex is not { } patientSex)
        {
            return RuleResult.Empty;
        }

        return clinicSex != patientSex ? Warn(SexNotSuitable) : RuleResult.Empty;
    }

    /// <summary>Checks the patient's age against the clinic's age range.</summary>
    /// <param name="clinic">Clinic restrictions; null when no clinic is selected.</param>
    /// <param name="ageYears">Patient age in years, as computed by the caller; values below 1 count as 1.</param>
    /// <returns>A warning on CLINICID when the age is below a set AGE_MIN or above a set AGE_MAX; otherwise <see cref="RuleResult.Empty"/>.</returns>
    public static RuleResult CheckAge(ClinicProfile? clinic, decimal ageYears)
    {
        if (clinic is null)
        {
            return RuleResult.Empty;
        }

        var age = ageYears < MinimumAgeYears ? MinimumAgeYears : ageYears;
        var belowMinimum = clinic.AgeMin is { } ageMin && age < ageMin;
        var aboveMaximum = clinic.AgeMax is { } ageMax && age > ageMax;

        return belowMinimum || aboveMaximum ? Warn(AgeNotSuitable) : RuleResult.Empty;
    }

    private static RuleResult Warn(string text) => new()
    {
        Messages = new[] { new ValidationMessage(ClinicField, text, ValidationMessage.Warning, RuleId) },
    };
}
