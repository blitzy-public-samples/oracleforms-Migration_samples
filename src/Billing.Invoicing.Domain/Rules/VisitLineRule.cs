using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Domain.Rules;

/// <summary>DR-25: chooses the automatic visit line added when the doctor is validated (T029).</summary>
public static class VisitLineRule
{
    private const string ReviewFlag = "Y";
    private const string ClaimParamOne = "1";
    private const string ClaimParamTwo = "2";
    private const string FixedServiceId = "2000";
    private const string CompanyWithFixedService = "1059";
    private const int ClinicWithFixedService = 14;

    /// <summary>Returns the automatic visit line for the draft's entry parameters, company and clinic.</summary>
    /// <param name="doReview">The <c>DO_REVIEW</c> entry parameter.</param>
    /// <param name="claimParam">The <c>CLAIM_NO</c> entry parameter.</param>
    /// <param name="compCode">The header company code (<c>COMP_CODE</c>).</param>
    /// <param name="clinicId">The header clinic (<c>CLINICID</c>).</param>
    /// <returns>Review for "Y"; for claim "1" or "2", service "2000" at company "1059" clinic 14, else Consultation; otherwise None.</returns>
    public static VisitLineChoice Choose(string? doReview, string? claimParam, string? compCode, int? clinicId)
    {
        if (string.Equals(doReview, ReviewFlag, StringComparison.Ordinal))
        {
            return VisitLineChoice.Review;
        }

        if (string.Equals(claimParam, ClaimParamOne, StringComparison.Ordinal)
            || string.Equals(claimParam, ClaimParamTwo, StringComparison.Ordinal))
        {
            return string.Equals(compCode, CompanyWithFixedService, StringComparison.Ordinal)
                   && clinicId == ClinicWithFixedService
                ? VisitLineChoice.FixedService(FixedServiceId)
                : VisitLineChoice.Consultation;
        }

        return VisitLineChoice.None;
    }
}
