namespace Billing.Invoicing.Domain.Model;

/// <summary>Patient coverage as read from <c>V_PAT_DATA</c> and the sub-company's <c>COMPANYS</c> row.</summary>
public sealed record PatientCoverageSnapshot
{
    /// <summary>Patient number.</summary>
    public string? PatientNo { get; init; }

    /// <summary>Patient name.</summary>
    public string? PatientName { get; init; }

    /// <summary>Company code; '0' denotes the cash company.</summary>
    public string? CompCode { get; init; }

    /// <summary>Company name.</summary>
    public string? CompName { get; init; }

    /// <summary>Sub-company (policy) code.</summary>
    public string? SubCompCode { get; init; }

    /// <summary>Sub-company (policy) name.</summary>
    public string? SubCompName { get; init; }

    /// <summary>Discount class code.</summary>
    public int? ClassCode { get; init; }

    /// <summary>Discount class name.</summary>
    public string? ClassName { get; init; }

    /// <summary>Patient VAT flag.</summary>
    public string? PayVat { get; init; }

    /// <summary>Maximum deductible amount of the coverage.</summary>
    public decimal? MaxDeductable { get; init; }

    /// <summary>Approval level of the coverage.</summary>
    public decimal? ApprovLvl { get; init; }

    /// <summary>Patient policy number.</summary>
    public string? PatPolicyNo { get; init; }

    /// <summary>Insurance number.</summary>
    public string? InsNumber { get; init; }

    /// <summary>Insurance card end date.</summary>
    public DateTime? CardEnd { get; init; }

    /// <summary>Company contract end date.</summary>
    public DateTime? ContractEnd { get; init; }

    /// <summary>Company active status; 2 means on hold.</summary>
    public int? CompanyIsActive { get; init; }

    /// <summary>Company type; 1 is direct, 2 is card.</summary>
    public int? CompanyType { get; init; }

    /// <summary>Patient's class reference; null when the patient has no class.</summary>
    public int? MyClass { get; init; }

    /// <summary>Class referral flag; 1 means a referral is required.</summary>
    public int? ClassWithRef { get; init; }

    /// <summary>Class active status; 2 means on hold.</summary>
    public int? ClassIsActive { get; init; }

    /// <summary>Sub-company contract end date.</summary>
    public DateTime? SubCompanyContractEnd { get; init; }

    /// <summary>Sub-company active status; 2 means on hold.</summary>
    public int? SubCompanyIsActive { get; init; }
}
