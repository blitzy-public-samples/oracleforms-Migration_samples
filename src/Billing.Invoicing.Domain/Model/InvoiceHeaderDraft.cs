namespace Billing.Invoicing.Domain.Model;

/// <summary>Invoice header draft: the <c>T_INV</c> fields of <c>BIL_INVOICE_ENGINE.t_header_input</c>, in spec order, followed by the Form-only header items.</summary>
public sealed record InvoiceHeaderDraft
{
    /// <summary>Patient number (PATIENTNO).</summary>
    public string? PatientNo { get; init; }

    /// <summary>Invoice date as stored; the binder binds <see cref="DraftDate"/>.</summary>
    public DateTime? InvDate { get; init; }

    /// <summary>Invoice type id (INVTYPEID).</summary>
    public int? InvTypeId { get; init; }

    /// <summary>Pay type (PAYTYPE): 1 cash, 2 credit.</summary>
    public int? PayType { get; init; }

    /// <summary>Payment method 1 (SUB_PAYTYPE).</summary>
    public int? SubPayType { get; init; }

    /// <summary>Payment method 2 (SUB_PAYTYPE2).</summary>
    public int? SubPayType2 { get; init; }

    /// <summary>Clinic id (CLINICID).</summary>
    public int? ClinicId { get; init; }

    /// <summary>Doctor id (DOCIDX, column DOCID).</summary>
    public int? DocId { get; init; }

    /// <summary>Currency code (CURR_CODE).</summary>
    public string? CurrCode { get; init; }

    /// <summary>Pre-authorisation; always null in this build.</summary>
    public string? PreAuthorization { get; init; }

    /// <summary>Claim number (CLAIM_NO).</summary>
    public string? ClaimNo { get; init; }

    /// <summary>Claim flag (CLAIM_FLAG).</summary>
    public string? ClaimFlag { get; init; }

    /// <summary>Note number (NOTE_NO).</summary>
    public string? NoteNo { get; init; }

    /// <summary>Final discount percent (FINALDISC_PERC).</summary>
    public decimal? FinalDiscPerc { get; init; }

    /// <summary>Final discount amount (FINALDISC).</summary>
    public decimal? FinalDisc { get; init; }

    /// <summary>Amount paid by payment method 1 (AMOUNT_1).</summary>
    public decimal? Amount1 { get; init; }

    /// <summary>Amount paid by payment method 2 (AMOUNT_2).</summary>
    public decimal? Amount2 { get; init; }

    /// <summary>Add-to-visit-list flag (ADD_TO_LIST).</summary>
    public int? AddToList { get; init; }

    /// <summary>User number of the operator who created the invoice (USER_NO).</summary>
    public int? UserNo { get; init; }

    /// <summary>Machine name of the creating host (MACHINE_N).</summary>
    public string? MachineN { get; init; }

    /// <summary>Information centre id (INFO_CENTER_ID).</summary>
    public string? InfoCenterId { get; init; }

    /// <summary>Database time read when the draft was created; bound as <c>invdate</c>.</summary>
    public DateTime DraftDate { get; init; }

    /// <summary>Invoice number (INV_NO); null on an unsaved draft.</summary>
    public long? InvNo { get; init; }

    /// <summary>ER / department-wise flag (DEPT_WISE); not persisted (OI-33).</summary>
    public int? DeptWise { get; init; }

    /// <summary>Call flag (CALL); not persisted (OI-33).</summary>
    public int? Call { get; init; }

    /// <summary>Final-discount entry mode (DISC_T): 1 percent, 0 value.</summary>
    public int? DiscT { get; init; }

    /// <summary>Cash tendered by the patient (CASH_PAYED).</summary>
    public decimal? CashPayed { get; init; }

    /// <summary>Company code (COMP_CODE); '0' is the cash company.</summary>
    public string? CompCode { get; init; }

    /// <summary>Sub-company code (SUB_COMP_CODE).</summary>
    public string? SubCompCode { get; init; }

    /// <summary>Insurance class code (CLASS_CODE).</summary>
    public int? ClassCode { get; init; }

    /// <summary>Header offer id (OFERID); display-only, never bound (OI-33).</summary>
    public int? OferId { get; init; }

    /// <summary>Referring doctor id (DOCID1); display-only, never bound (OI-33).</summary>
    public int? DocId1 { get; init; }

    /// <summary>Reservation sequence number (SEQ_NO); display-only, never bound (OI-33).</summary>
    public int? SeqNo { get; init; }

    /// <summary>Insurance number (INS_NUMBER) copied from the claim's first invoice; display-only, never bound.</summary>
    public string? InsNumber { get; init; }

    /// <summary>Insurance card end date (CARD_END) copied from the claim's first invoice; display-only, never bound.</summary>
    public DateTime? CardEnd { get; init; }

    /// <summary>Patient policy number (PAT_POLICY_NO) copied from the claim's first invoice; display-only, never bound.</summary>
    public string? PatPolicyNo { get; init; }
}
