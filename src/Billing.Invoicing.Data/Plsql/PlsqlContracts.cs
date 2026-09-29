namespace Billing.Invoicing.Data.Plsql;

/// <summary>One engine line as returned in <c>BIL_INVOICE_ENGINE.t_line_input</c>; UNVERIFIED against Oracle.</summary>
public sealed record EngineLineInput
{
    /// <summary>Service id (<c>serviceid</c>).</summary>
    public string? ServiceId { get; init; }

    /// <summary>Quantity (<c>qty</c>).</summary>
    public decimal? Qty { get; init; }

    /// <summary>Price override (<c>price_override</c>).</summary>
    public decimal? PriceOverride { get; init; }

    /// <summary>Price-override flag 'Y' or 'N' (<c>use_price_override</c>).</summary>
    public string? UsePriceOverride { get; init; }

    /// <summary>Line discount type 'N', 'R' or 'V' (<c>discount_type</c>).</summary>
    public string? DiscountType { get; init; }

    /// <summary>Line discount rate (<c>disc</c>).</summary>
    public decimal? Disc { get; init; }

    /// <summary>Line discount value (<c>my_disc</c>).</summary>
    public decimal? MyDisc { get; init; }

    /// <summary>Tooth number (<c>teeth_no</c>).</summary>
    public string? TeethNo { get; init; }

    /// <summary>Tooth surface (<c>tooth_surface</c>).</summary>
    public string? ToothSurface { get; init; }

    /// <summary>Second tooth number (<c>teeth_no2</c>).</summary>
    public string? TeethNo2 { get; init; }

    /// <summary>Linked PAT_SERV_REQ row id (<c>pat_serv_req_row_id</c>).</summary>
    public long? PatServReqRowId { get; init; }

    /// <summary>Approval date (<c>approv_date</c>).</summary>
    public DateTime? ApprovDate { get; init; }

    /// <summary>Approval validity (<c>approv_validity</c>).</summary>
    public decimal? ApprovValidity { get; init; }

    /// <summary>Approval reference number (<c>approv_ref_no</c>).</summary>
    public string? ApprovRefNo { get; init; }

    /// <summary>Line claim number (<c>claim_no</c>).</summary>
    public string? ClaimNo { get; init; }

    /// <summary>Approval-needed flag (<c>req_need_a</c>).</summary>
    public int? ReqNeedA { get; init; }

    /// <summary>Approval status (<c>req_a_status</c>).</summary>
    public int? ReqAStatus { get; init; }

    /// <summary>Owning package service id (<c>package_service_id</c>).</summary>
    public string? PackageServiceId { get; init; }

    /// <summary>Package instance id (<c>package_instance_id</c>).</summary>
    public string? PackageInstanceId { get; init; }

    /// <summary>Package line role (<c>package_line_role</c>).</summary>
    public string? PackageLineRole { get; init; }

    /// <summary>Package component order (<c>package_component_order</c>).</summary>
    public int? PackageComponentOrder { get; init; }

    /// <summary>Package parent line id (<c>package_parent_line_id</c>).</summary>
    public long? PackageParentLineId { get; init; }

    /// <summary>Package pricing method (<c>package_pricing_method</c>).</summary>
    public string? PackagePricingMethod { get; init; }

    /// <summary>Package definition token (<c>package_definition_token</c>).</summary>
    public string? PackageDefinitionToken { get; init; }

    /// <summary>Offer id (<c>offer_id</c>).</summary>
    public int? OfferId { get; init; }

    /// <summary>Offer detail id (<c>offer_dtl_id</c>).</summary>
    public long? OfferDtlId { get; init; }

    /// <summary>Offer type (<c>offer_type</c>).</summary>
    public int? OfferType { get; init; }

    /// <summary>Offer instance id (<c>offer_instance_id</c>).</summary>
    public string? OfferInstanceId { get; init; }

    /// <summary>Offer line role (<c>offer_line_role</c>).</summary>
    public string? OfferLineRole { get; init; }

    /// <summary>Offer parent line id (<c>offer_parent_line_id</c>).</summary>
    public long? OfferParentLineId { get; init; }

    /// <summary>Offer price applied (<c>offer_price_applied</c>).</summary>
    public decimal? OfferPriceApplied { get; init; }

    /// <summary>Offer discount applied (<c>offer_dis_applied</c>).</summary>
    public decimal? OfferDisApplied { get; init; }

    /// <summary>Offer name snapshot (<c>offer_name_snapshot</c>).</summary>
    public string? OfferNameSnapshot { get; init; }

    /// <summary>Offer object version number (<c>offer_object_version_number</c>).</summary>
    public long? OfferObjectVersionNumber { get; init; }

    /// <summary>Offer detail object version number (<c>offer_dtl_object_version_number</c>).</summary>
    public long? OfferDtlObjectVersionNumber { get; init; }
}

/// <summary>One calculated preview line as returned in <c>BIL_INVOICE_API.t_editable_preview_line</c>; UNVERIFIED against Oracle.</summary>
public sealed record EditablePreviewLine
{
    /// <summary>Client-side line id (<c>client_id</c>).</summary>
    public string? ClientId { get; init; }

    /// <summary>Line number (<c>line_no</c>).</summary>
    public int? LineNo { get; init; }

    /// <summary>Service id (<c>serviceid</c>).</summary>
    public string? ServiceId { get; init; }

    /// <summary>Service description (<c>servicedesc</c>).</summary>
    public string? ServiceDesc { get; init; }

    /// <summary>Service category (<c>catid</c>).</summary>
    public int? CatId { get; init; }

    /// <summary>Price list id (<c>list_id</c>).</summary>
    public decimal? ListId { get; init; }

    /// <summary>Currency code (<c>curr_code</c>).</summary>
    public string? CurrCode { get; init; }

    /// <summary>Quantity (<c>qty</c>).</summary>
    public decimal? Qty { get; init; }

    /// <summary>Unit price (<c>price</c>).</summary>
    public decimal? Price { get; init; }

    /// <summary>Price-plan discount percent (<c>plan_discount_pct</c>).</summary>
    public decimal? PlanDiscountPct { get; init; }

    /// <summary>Price-plan discount amount (<c>plan_discount_amount</c>).</summary>
    public decimal? PlanDiscountAmount { get; init; }

    /// <summary>Manual discount type (<c>manual_discount_type</c>).</summary>
    public string? ManualDiscountType { get; init; }

    /// <summary>Manual discount percent (<c>manual_discount_pct</c>).</summary>
    public decimal? ManualDiscountPct { get; init; }

    /// <summary>Manual discount amount (<c>manual_discount_amount</c>).</summary>
    public decimal? ManualDiscountAmount { get; init; }

    /// <summary>Source of the applied discount (<c>discount_source</c>).</summary>
    public string? DiscountSource { get; init; }

    /// <summary>Line discount rate (<c>disc</c>).</summary>
    public decimal? Disc { get; init; }

    /// <summary>Line discount value (<c>my_disc</c>).</summary>
    public decimal? MyDisc { get; init; }

    /// <summary>Line gross amount (<c>my_price</c>).</summary>
    public decimal? MyPrice { get; init; }

    /// <summary>Line net amount (<c>my_net</c>).</summary>
    public decimal? MyNet { get; init; }

    /// <summary>Patient share (<c>the_pay</c>).</summary>
    public decimal? ThePay { get; init; }

    /// <summary>Company share (<c>the_comp</c>).</summary>
    public decimal? TheComp { get; init; }

    /// <summary>VAT rate (<c>vat_rate</c>).</summary>
    public decimal? VatRate { get; init; }

    /// <summary>Patient VAT (<c>vat_val_pat</c>).</summary>
    public decimal? VatValPat { get; init; }

    /// <summary>Company VAT (<c>vat_val_co</c>).</summary>
    public decimal? VatValCo { get; init; }

    /// <summary>Exempt patient VAT (<c>vat_val_pat_ex</c>).</summary>
    public decimal? VatValPatEx { get; init; }

    /// <summary>Approval-needed flag (<c>req_need_a</c>).</summary>
    public int? ReqNeedA { get; init; }

    /// <summary>Approval status (<c>req_a_status</c>).</summary>
    public int? ReqAStatus { get; init; }

    /// <summary>Manual-discount flag 'Y' or 'N' (<c>allow_manual_discount</c>).</summary>
    public string? AllowManualDiscount { get; init; }

    /// <summary>Price-override flag 'Y' or 'N' (<c>allow_price_override</c>).</summary>
    public string? AllowPriceOverride { get; init; }

    /// <summary>Owning package service id (<c>package_service_id</c>).</summary>
    public string? PackageServiceId { get; init; }

    /// <summary>Package instance id (<c>package_instance_id</c>).</summary>
    public string? PackageInstanceId { get; init; }

    /// <summary>Package line role (<c>package_line_role</c>).</summary>
    public string? PackageLineRole { get; init; }

    /// <summary>Package component order (<c>package_component_order</c>).</summary>
    public int? PackageComponentOrder { get; init; }

    /// <summary>Package parent line id (<c>package_parent_line_id</c>).</summary>
    public long? PackageParentLineId { get; init; }

    /// <summary>Package pricing method (<c>package_pricing_method</c>).</summary>
    public string? PackagePricingMethod { get; init; }

    /// <summary>Package definition token (<c>package_definition_token</c>).</summary>
    public string? PackageDefinitionToken { get; init; }

    /// <summary>Offer id (<c>offer_id</c>).</summary>
    public int? OfferId { get; init; }

    /// <summary>Offer detail id (<c>offer_dtl_id</c>).</summary>
    public long? OfferDtlId { get; init; }

    /// <summary>Offer type (<c>offer_type</c>).</summary>
    public int? OfferType { get; init; }

    /// <summary>Offer instance id (<c>offer_instance_id</c>).</summary>
    public string? OfferInstanceId { get; init; }

    /// <summary>Offer line role (<c>offer_line_role</c>).</summary>
    public string? OfferLineRole { get; init; }

    /// <summary>Offer parent line id (<c>offer_parent_line_id</c>).</summary>
    public long? OfferParentLineId { get; init; }

    /// <summary>Offer price applied (<c>offer_price_applied</c>).</summary>
    public decimal? OfferPriceApplied { get; init; }

    /// <summary>Offer discount applied (<c>offer_dis_applied</c>).</summary>
    public decimal? OfferDisApplied { get; init; }

    /// <summary>Offer name snapshot (<c>offer_name_snapshot</c>).</summary>
    public string? OfferNameSnapshot { get; init; }

    /// <summary>Offer object version number (<c>offer_object_version_number</c>).</summary>
    public long? OfferObjectVersionNumber { get; init; }

    /// <summary>Offer detail object version number (<c>offer_dtl_object_version_number</c>).</summary>
    public long? OfferDtlObjectVersionNumber { get; init; }
}

/// <summary>Invoice preview totals as returned in <c>BIL_INVOICE_API.t_preview_totals</c>; UNVERIFIED against Oracle.</summary>
public sealed record PreviewTotalsRow
{
    /// <summary>Number of calculated lines (<c>line_count</c>).</summary>
    public int? LineCount { get; init; }

    /// <summary>Sum of line gross amounts (<c>total_gross</c>).</summary>
    public decimal? TotalGross { get; init; }

    /// <summary>Sum of line discount amounts (<c>total_discount</c>).</summary>
    public decimal? TotalDiscount { get; init; }

    /// <summary>Sum of line net amounts (<c>total_net</c>).</summary>
    public decimal? TotalNet { get; init; }

    /// <summary>Patient share (<c>pat_pay</c>).</summary>
    public decimal? PatPay { get; init; }

    /// <summary>Company share (<c>comp_pay</c>).</summary>
    public decimal? CompPay { get; init; }

    /// <summary>Patient VAT total (<c>vat_total_pat</c>).</summary>
    public decimal? VatTotalPat { get; init; }

    /// <summary>Company VAT total (<c>vat_total_co</c>).</summary>
    public decimal? VatTotalCo { get; init; }

    /// <summary>Amount due from the patient (<c>cash_collected</c>).</summary>
    public decimal? CashCollected { get; init; }

    /// <summary>Amount by payment method 1 (<c>amount_1</c>).</summary>
    public decimal? Amount1 { get; init; }

    /// <summary>Amount by payment method 2 (<c>amount_2</c>).</summary>
    public decimal? Amount2 { get; init; }

    /// <summary>Amount still due (<c>remaining_amount</c>).</summary>
    public decimal? RemainingAmount { get; init; }

    /// <summary>Payment status text (<c>payment_status</c>).</summary>
    public string? PaymentStatus { get; init; }
}

/// <summary>Import summary as returned in <c>BIL_IMPORT.t_import_result</c>; UNVERIFIED against Oracle.</summary>
public sealed record ImportResultRow
{
    /// <summary>Import source type (<c>source_type</c>).</summary>
    public string? SourceType { get; init; }

    /// <summary>Number of source rows read (<c>source_count</c>).</summary>
    public int? SourceCount { get; init; }

    /// <summary>Number of lines imported (<c>imported_count</c>).</summary>
    public int? ImportedCount { get; init; }

    /// <summary>Number of rejected rows skipped (<c>skipped_rejected_count</c>).</summary>
    public int? SkippedRejectedCount { get; init; }

    /// <summary>Number of rows needing approval skipped (<c>skipped_need_approval_count</c>).</summary>
    public int? SkippedNeedApprovalCount { get; init; }

    /// <summary>Number of invalid rows skipped (<c>skipped_invalid_count</c>).</summary>
    public int? SkippedInvalidCount { get; init; }

    /// <summary>Price-override flag 'Y' or 'N' (<c>has_price_overrides</c>).</summary>
    public string? HasPriceOverrides { get; init; }

    /// <summary>Import message (<c>message</c>).</summary>
    public string? Message { get; init; }
}

/// <summary>Create result as returned in <c>BIL_INVOICE_API.t_full_invoice_result</c>, with <c>invoice_result</c> flattened; UNVERIFIED against Oracle.</summary>
public sealed record FullInvoiceResultRow
{
    /// <summary>Invoice number (<c>invoice_result.inv_no</c>).</summary>
    public long? InvNo { get; init; }

    /// <summary>Invoice date (<c>invoice_result.invdate</c>).</summary>
    public DateTime? InvDate { get; init; }

    /// <summary>Patient number (<c>invoice_result.patientno</c>).</summary>
    public string? PatientNo { get; init; }

    /// <summary>Invoice currency code (<c>invoice_result.curr_code</c>).</summary>
    public string? CurrCode { get; init; }

    /// <summary>Number of invoice lines (<c>invoice_result.line_count</c>).</summary>
    public int? LineCount { get; init; }

    /// <summary>Sum of line gross amounts (<c>invoice_result.total_gross</c>).</summary>
    public decimal? TotalGross { get; init; }

    /// <summary>Sum of line discount amounts (<c>invoice_result.total_discount</c>).</summary>
    public decimal? TotalDiscount { get; init; }

    /// <summary>Sum of line net amounts (<c>invoice_result.total_net</c>).</summary>
    public decimal? TotalNet { get; init; }

    /// <summary>Patient share (<c>invoice_result.pat_pay</c>).</summary>
    public decimal? PatPay { get; init; }

    /// <summary>Company share (<c>invoice_result.comp_pay</c>).</summary>
    public decimal? CompPay { get; init; }

    /// <summary>Patient VAT total (<c>invoice_result.vat_total_pat</c>).</summary>
    public decimal? VatTotalPat { get; init; }

    /// <summary>Company VAT total (<c>invoice_result.vat_total_co</c>).</summary>
    public decimal? VatTotalCo { get; init; }

    /// <summary>VAT total (<c>invoice_result.vat_total</c>).</summary>
    public decimal? VatTotal { get; init; }

    /// <summary>Final discount amount (<c>invoice_result.finaldisc</c>).</summary>
    public decimal? FinalDisc { get; init; }

    /// <summary>Amount due from the patient (<c>invoice_result.cash_collected</c>).</summary>
    public decimal? CashCollected { get; init; }

    /// <summary>Cashier shift id (<c>invoice_result.shift_system_unique</c>).</summary>
    public string? ShiftSystemUnique { get; init; }

    /// <summary>Payment posted flag 'Y' or 'N' (<c>payment_posted</c>).</summary>
    public string? PaymentPosted { get; init; }

    /// <summary>Queue posted flag 'Y' or 'N' (<c>queue_posted</c>).</summary>
    public string? QueuePosted { get; init; }

    /// <summary>Stock posted flag 'Y' or 'N' (<c>stock_posted</c>).</summary>
    public string? StockPosted { get; init; }

    /// <summary>Print URL built flag 'Y' or 'N' (<c>print_url_built</c>).</summary>
    public string? PrintUrlBuilt { get; init; }

    /// <summary>SMS sent flag 'Y' or 'N' (<c>sms_sent</c>).</summary>
    public string? SmsSent { get; init; }

    /// <summary>Create message (<c>message</c>).</summary>
    public string? Message { get; init; }

    /// <summary>Message send status (<c>message_result.send_status</c>).</summary>
    public string? MessageSendStatus { get; init; }

    /// <summary>Message result text (<c>message_result.message</c>).</summary>
    public string? MessageText { get; init; }
}

