namespace Billing.Invoicing.Domain.Model;

/// <summary>Entry context set by the calling module: the 32 <c>INV_SMALL_CASH</c> module parameters, with the Form's initial values.</summary>
public sealed record InvoiceEntryParameters
{
    /// <summary>PARAMETER.IS_HOME_CARE; 'Y' selects invoice type 7 on a new draft.</summary>
    public string? IsHomeCare { get; init; }

    /// <summary>PARAMETER.NEW_PAT_INV.</summary>
    public string? NewPatInv { get; init; } = "X";

    /// <summary>PARAMETER.THE_DOC; the doctor the draft is locked to.</summary>
    public int? TheDoc { get; init; }

    /// <summary>PARAMETER.COMP_TYPE; company type of the selected company.</summary>
    public int? CompType { get; init; }

    /// <summary>PARAMETER.PAY_VAT_CO; the company's PAY_VAT flag.</summary>
    public string? PayVatCo { get; init; }

    /// <summary>PARAMETER.FROM_CHK.</summary>
    public string? FromChk { get; init; }

    /// <summary>PARAMETER.CLAIM_FLAG; 'R' keeps the given claim number.</summary>
    public string? ClaimFlag { get; init; }

    /// <summary>PARAMETER.CASH_OR_CREDIT; 1 cash, 2 credit.</summary>
    public int? CashOrCredit { get; init; }

    /// <summary>PARAMETER.DO_REVIEW; 'Y' requests a review visit line.</summary>
    public string? DoReview { get; init; } = "N";

    /// <summary>PARAMETER.THE_COUNTRY.</summary>
    public string? TheCountry { get; init; } = "KSA";

    /// <summary>PARAMETER.CLAIM_DATE.</summary>
    public DateTime? ClaimDate { get; init; }

    /// <summary>PARAMETER.PAY_VAT; the patient nationality's PAY_VAT flag.</summary>
    public string? PayVat { get; init; }

    /// <summary>PARAMETER.ONE_VISIT_960; PREF 960 claim-revisit setting.</summary>
    public int? OneVisit960 { get; init; }

    /// <summary>PARAMETER.DIRECT_CALL.</summary>
    public string? DirectCall { get; init; } = "Y";

    /// <summary>PARAMETER.X422_APPROV_CHECK; PREF 422 approval check, 2 bypasses it.</summary>
    public int? X422ApprovCheck { get; init; }

    /// <summary>PARAMETER.DEDUCT_RATE.</summary>
    public decimal? DeductRate { get; init; }

    /// <summary>PARAMETER.DEDUCT_FIXED.</summary>
    public decimal? DeductFixed { get; init; }

    /// <summary>PARAMETER.DIRECT_COMP_SHARE; company direct share of the selected company.</summary>
    public decimal? DirectCompShare { get; init; }

    /// <summary>PARAMETER.LOCAL_DOC_TYPE; the invoice ROW_TYPE queried.</summary>
    public int LocalDocType { get; init; } = 505;

    /// <summary>PARAMETER.VISIT_UNIQUE; the patient visit the draft belongs to.</summary>
    public string? VisitUnique { get; init; }

    /// <summary>PARAMETER.NEW_DOC; doctor for claim parameter '1'.</summary>
    public int? NewDoc { get; init; }

    /// <summary>PARAMETER.WILL_DO_IMP.</summary>
    public string? WillDoImp { get; init; } = "N";

    /// <summary>PARAMETER.INV_ADMIN; 1 for an invoice administrator.</summary>
    public int? InvAdmin { get; init; }

    /// <summary>PARAMETER.INV_DATE_ADMIN; 1 for a user with date-admin privileges.</summary>
    public int? InvDateAdmin { get; init; }

    /// <summary>PARAMETER.OPEN_FROM_ACC.</summary>
    public string? OpenFromAcc { get; init; } = "N";

    /// <summary>PARAMETER.PKG_INV; invoice number of the prepaid package being consumed.</summary>
    public long? PkgInv { get; init; }

    /// <summary>PARAMETER.CLAIM_NO; '0', '1', '2' or a claim number.</summary>
    public string ClaimNo { get; init; } = "0";

    /// <summary>PARAMETER.PHARAMACY_INSTALL_601.</summary>
    public string? PharamacyInstall601 { get; init; }

    /// <summary>PARAMETER.RESERV_BY_TIME_801; PREF 801.</summary>
    public string? ReservByTime801 { get; init; }

    /// <summary>PARAMETER.SHIFT_CONYTOL_901; PREF 901 shift control.</summary>
    public string? ShiftConytol901 { get; init; }

    /// <summary>PARAMETER.LESS_PAYMENT_970; PREF 970.</summary>
    public string? LessPayment970 { get; init; }

    /// <summary>PARAMETER.P_USER.</summary>
    public int? PUser { get; init; }
}
