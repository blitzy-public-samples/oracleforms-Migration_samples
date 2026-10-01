namespace Billing.Invoicing.Domain.Model;

/// <summary>One draft invoice line (D_INV record); the first 35 properties follow BIL_INVOICE_ENGINE.t_line_input in order.</summary>
public sealed record InvoiceLineDraft
{
    /// <summary>Service id (SERVICEID); engine field serviceid.</summary>
    public string? ServiceId { get; init; }

    /// <summary>Quantity (QTY); engine field qty.</summary>
    public decimal? Qty { get; init; }

    /// <summary>Operator- or import-supplied price override; engine field price_override.</summary>
    public decimal? PriceOverride { get; init; }

    /// <summary>Price-override flag 'Y' or 'N'; engine field use_price_override.</summary>
    public string? UsePriceOverride { get; init; }

    /// <summary>Line discount type 'N' (none), 'R' (rate) or 'V' (value) (LDISCT); engine field discount_type.</summary>
    public string? DiscountType { get; init; } = "R";

    /// <summary>Line discount rate (DISC); engine field disc.</summary>
    public decimal? Disc { get; init; }

    /// <summary>Line discount value (MY_DISC); engine field my_disc.</summary>
    public decimal? MyDisc { get; init; }

    /// <summary>Tooth number (TEETH_NO); engine field teeth_no.</summary>
    public string? TeethNo { get; init; }

    /// <summary>Tooth surface (TOOTH_SURFACE); engine field tooth_surface.</summary>
    public string? ToothSurface { get; init; }

    /// <summary>Second tooth number (TEETH_NO2); engine field teeth_no2.</summary>
    public string? TeethNo2 { get; init; }

    /// <summary>Linked PAT_SERV_REQ row id (PAT_SERV_REQ_ROW_ID); engine field pat_serv_req_row_id.</summary>
    public long? PatServReqRowId { get; init; }

    /// <summary>Approval date (APPROV_DATE); engine field approv_date.</summary>
    public DateTime? ApprovDate { get; init; }

    /// <summary>Approval validity (APPROV_VALIDITY); engine field approv_validity.</summary>
    public decimal? ApprovValidity { get; init; }

    /// <summary>Approval reference number (APPROV_REF_NO); engine field approv_ref_no.</summary>
    public string? ApprovRefNo { get; init; }

    /// <summary>Line claim number (CLAIM_NO); engine field claim_no.</summary>
    public string? ClaimNo { get; init; }

    /// <summary>Approval-needed flag (REQ_NEED_A); engine field req_need_a.</summary>
    public int? ReqNeedA { get; init; }

    /// <summary>Approval status (REQ_A_STATUS); engine field req_a_status.</summary>
    public int? ReqAStatus { get; init; }

    /// <summary>Owning package service id; engine field package_service_id.</summary>
    public string? PackageServiceId { get; init; }

    /// <summary>Package instance id; engine field package_instance_id.</summary>
    public string? PackageInstanceId { get; init; }

    /// <summary>Package line role; engine field package_line_role.</summary>
    public string? PackageLineRole { get; init; }

    /// <summary>Package component order; engine field package_component_order.</summary>
    public int? PackageComponentOrder { get; init; }

    /// <summary>Package parent line id; engine field package_parent_line_id.</summary>
    public long? PackageParentLineId { get; init; }

    /// <summary>Package pricing method; engine field package_pricing_method.</summary>
    public string? PackagePricingMethod { get; init; }

    /// <summary>Package definition token; engine field package_definition_token.</summary>
    public string? PackageDefinitionToken { get; init; }

    /// <summary>Offer id; engine field offer_id.</summary>
    public int? OfferId { get; init; }

    /// <summary>Offer detail id; engine field offer_dtl_id.</summary>
    public long? OfferDtlId { get; init; }

    /// <summary>Offer type; engine field offer_type.</summary>
    public int? OfferType { get; init; }

    /// <summary>Offer instance id; engine field offer_instance_id.</summary>
    public string? OfferInstanceId { get; init; }

    /// <summary>Offer line role; engine field offer_line_role.</summary>
    public string? OfferLineRole { get; init; }

    /// <summary>Offer parent line id; engine field offer_parent_line_id.</summary>
    public long? OfferParentLineId { get; init; }

    /// <summary>Offer price applied; engine field offer_price_applied.</summary>
    public decimal? OfferPriceApplied { get; init; }

    /// <summary>Offer discount applied; engine field offer_dis_applied.</summary>
    public decimal? OfferDisApplied { get; init; }

    /// <summary>Offer name snapshot; engine field offer_name_snapshot.</summary>
    public string? OfferNameSnapshot { get; init; }

    /// <summary>Offer object version number; engine field offer_object_version_number.</summary>
    public long? OfferObjectVersionNumber { get; init; }

    /// <summary>Offer detail object version number; engine field offer_dtl_object_version_number.</summary>
    public long? OfferDtlObjectVersionNumber { get; init; }

    /// <summary>Client-side line id, bound through t_client_id_tab.</summary>
    public string? ClientId { get; init; }

    /// <summary>Displayed line price from an LOV, an import or a preview.</summary>
    public decimal? Price { get; init; }

    /// <summary>Service category (CATID); selectable in a draft and returned by preview, but omitted from package input (OI-33).</summary>
    public int? CatId { get; init; }

    /// <summary>Fixed payer amount (FIXPAY); display only (OI-33).</summary>
    public decimal? FixPay { get; init; }

    /// <summary>Payer rate (PAYRATE); display only (OI-33).</summary>
    public decimal? PayRate { get; init; }

    /// <summary>Regular lenses type (REGULAR_LENSES_TYPE); display only (OI-33).</summary>
    public string? RegularLensesType { get; init; }

    /// <summary>Lens specifications (LENS_SPECIFICATIONS); display only (OI-33).</summary>
    public string? LensSpecifications { get; init; }

    /// <summary>Contact lenses type (CONTACT_LENSES_TYPE); display only (OI-33).</summary>
    public string? ContactLensesType { get; init; }

    /// <summary>F L indicator (F_L_INDICATOR); display only (OI-33).</summary>
    public string? FLIndicator { get; init; }

    /// <summary>Number of pairs (NUMBER_OF_PAIRS); display only (OI-33).</summary>
    public string? NumberOfPairs { get; init; }

    /// <summary>Insurance employee number (INS_EMP); display only (OI-33).</summary>
    public int? InsEmp { get; init; }
}
