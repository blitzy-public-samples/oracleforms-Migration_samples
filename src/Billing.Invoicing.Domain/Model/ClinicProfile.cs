namespace Billing.Invoicing.Domain.Model;

/// <summary>Clinic restrictions from <c>CLINICS</c> and the draft patient's sex from <c>PATIENT</c>, as read by T031, T032 and T033.</summary>
public sealed record ClinicProfile
{
    /// <summary>Clinic id, <c>CLINICS.CLINICID</c>.</summary>
    public int? ClinicId { get; init; }

    /// <summary>Sex the clinic is restricted to, <c>CLINICS.SIX</c>; null when unrestricted.</summary>
    public int? Six { get; init; }

    /// <summary>Minimum patient age in years, <c>CLINICS.AGE_MIN</c>.</summary>
    public decimal? AgeMin { get; init; }

    /// <summary>Maximum patient age in years, <c>CLINICS.AGE_MAX</c>.</summary>
    public decimal? AgeMax { get; init; }

    /// <summary>Clinic category type, <c>CLINICS.SYS_CAT_TYPE</c> (for example 'ER' or 'DNT').</summary>
    public string? SysCatType { get; init; }

    /// <summary>Sex of the draft's patient, <c>PATIENT.SIX</c>.</summary>
    public int? PatientSex { get; init; }
}
