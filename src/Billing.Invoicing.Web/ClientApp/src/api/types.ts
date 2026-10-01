/** JSON shapes sent to and received from the Billing.Invoicing.Api host; dates are ISO 8601 strings. */

/** A decimal returned as a number or entered as numeric text. */
export type DecimalValue = number | string;

/** Invoice header draft: the T_INV fields of BIL_INVOICE_ENGINE.t_header_input in spec order, then the Form-only header items. */
export interface InvoiceHeaderDraft {
  /** T_INV.PATIENTNO. */
  patientNo: string | null;
  /** T_INV.INVDATE as stored. */
  invDate: string | null;
  /** T_INV.INVTYPEID. */
  invTypeId: number | null;
  /** T_INV.PAYTYPE: 1 cash, 2 credit. */
  payType: number | null;
  /** T_INV.SUB_PAYTYPE, payment method 1. */
  subPayType: number | null;
  /** T_INV.SUB_PAYTYPE2, payment method 2. */
  subPayType2: number | null;
  /** T_INV.CLINICID. */
  clinicId: number | null;
  /** T_INV.DOCIDX (column DOCID). */
  docId: number | null;
  /** T_INV.CURR_CODE. */
  currCode: string | null;
  /** T_INV.PRE_AUTHORIZATION; always null. Absent from a DraftDto header. */
  preAuthorization?: null;
  /** T_INV.CLAIM_NO. */
  claimNo: string | null;
  /** T_INV.CLAIM_FLAG. */
  claimFlag: string | null;
  /** T_INV.NOTE_NO. */
  noteNo: string | null;
  /** T_INV.FINALDISC_PERC. */
  finalDiscPerc: DecimalValue | null;
  /** T_INV.FINALDISC. */
  finalDisc: DecimalValue | null;
  /** T_INV.AMOUNT_1. */
  amount1: DecimalValue | null;
  /** T_INV.AMOUNT_2. */
  amount2: DecimalValue | null;
  /** T_INV.ADD_TO_LIST. */
  addToList: number | null;
  /** T_INV.USER_NO. */
  userNo: number | null;
  /** T_INV.MACHINE_N. */
  machineN: string | null;
  /** T_INV.INFO_CENTER_ID. */
  infoCenterId: string | null;
  /** Database time read when the draft was created; bound as INVDATE. */
  draftDate: string;
  /** T_INV.INV_NO; null on an unsaved draft. */
  invNo: number | null;
  /** T_INV.DEPT_WISE. */
  deptWise: number | null;
  /** T_INV.CALL. */
  call: number | null;
  /** T_INV.DISC_T: 1 percent, 0 value. */
  discT: number | null;
  /** T_INV.CASH_PAYED. */
  cashPayed: DecimalValue | null;
  /** T_INV.COMP_CODE; '0' is the cash company. */
  compCode: string | null;
  /** T_INV.SUB_COMP_CODE. */
  subCompCode: string | null;
  /** T_INV.CLASS_CODE. */
  classCode: number | null;
  /** T_INV.OFERID. Display only; not saved (OI-33). Absent from a DraftDto header. */
  oferId?: number | null;
  /** T_INV.DOCID1. Display only; not saved (OI-33). Absent from a DraftDto header. */
  docId1?: number | null;
  /** T_INV.SEQ_NO. Display only; not saved (OI-33). Absent from a DraftDto header. */
  seqNo?: number | null;
  /** T_INV.INS_NUMBER, copied from the claim's first invoice; not saved from the draft. */
  insNumber: string | null;
  /** T_INV.CARD_END, copied from the claim's first invoice; not saved from the draft. */
  cardEnd: string | null;
  /** T_INV.PAT_POLICY_NO, copied from the claim's first invoice; not saved from the draft. */
  patPolicyNo: string | null;
}

/** Invoice line draft: the D_INV fields of BIL_INVOICE_ENGINE.t_line_input in spec order, then the client and display items. */
export interface InvoiceLineDraft {
  /** D_INV.SERVICEID. */
  serviceId: string | null;
  /** D_INV.QTY. */
  qty: DecimalValue | null;
  /** Operator- or import-supplied price override; t_line_input.price_override. */
  priceOverride: DecimalValue | null;
  /** 'Y' or 'N'; t_line_input.use_price_override. */
  usePriceOverride: string | null;
  /** D_INV.LDISCT: 'N' none, 'R' rate, 'V' value. */
  discountType: string | null;
  /** D_INV.DISC, line discount rate. */
  disc: DecimalValue | null;
  /** D_INV.MY_DISC, line discount value. */
  myDisc: DecimalValue | null;
  /** D_INV.TEETH_NO. */
  teethNo: string | null;
  /** D_INV.TOOTH_SURFACE. */
  toothSurface: string | null;
  /** D_INV.TEETH_NO2. */
  teethNo2: string | null;
  /** D_INV.PAT_SERV_REQ_ROW_ID. */
  patServReqRowId: number | null;
  /** D_INV.APPROV_DATE. */
  approvDate: string | null;
  /** D_INV.APPROV_VALIDITY. */
  approvValidity: number | null;
  /** D_INV.APPROV_REF_NO. */
  approvRefNo: string | null;
  /** D_INV.CLAIM_NO. */
  claimNo: string | null;
  /** D_INV.REQ_NEED_A. */
  reqNeedA: number | null;
  /** D_INV.REQ_A_STATUS. */
  reqAStatus: number | null;
  /** D_INV.PACKAGE_SERVICE_ID. */
  packageServiceId: string | null;
  /** D_INV.PACKAGE_INSTANCE_ID. */
  packageInstanceId: string | null;
  /** D_INV.PACKAGE_LINE_ROLE. */
  packageLineRole: string | null;
  /** D_INV.PACKAGE_COMPONENT_ORDER. */
  packageComponentOrder: number | null;
  /** D_INV.PACKAGE_PARENT_LINE_ID. */
  packageParentLineId: number | null;
  /** SERVICES.PACKAGE_PRICING_METHOD; t_line_input.package_pricing_method. */
  packagePricingMethod: string | null;
  /** t_line_input.package_definition_token. */
  packageDefinitionToken: string | null;
  /** D_INV.OFFER_ID. */
  offerId: number | null;
  /** D_INV.OFFER_DTL_ID. */
  offerDtlId: number | null;
  /** D_INV.OFFER_TYPE. */
  offerType: number | null;
  /** D_INV.OFFER_INSTANCE_ID. */
  offerInstanceId: string | null;
  /** D_INV.OFFER_LINE_ROLE. */
  offerLineRole: string | null;
  /** D_INV.OFFER_PARENT_LINE_ID. */
  offerParentLineId: number | null;
  /** D_INV.OFFER_PRICE_APPLIED. */
  offerPriceApplied: number | null;
  /** D_INV.OFFER_DIS_APPLIED. */
  offerDisApplied: number | null;
  /** D_INV.OFFER_NAME_SNAPSHOT. */
  offerNameSnapshot: string | null;
  /** D_INV.OFFER_OBJECT_VERSION_NUMBER. */
  offerObjectVersionNumber: number | null;
  /** D_INV.OFFER_DTL_OBJECT_VERSION_NUMBER. */
  offerDtlObjectVersionNumber: number | null;
  /** Client-side line id; t_client_id_tab entry. */
  clientId: string | null;
  /** D_INV.PRICE as displayed from an LOV, an import or a preview. */
  price: number | null;
  /** D_INV.CATID; selectable in a draft and returned by preview, but omitted from package input (OI-33). */
  catId?: number | null;
  /** D_INV.FIXPAY. Display only; not saved (OI-33). Absent from a DraftDto line. */
  fixPay?: number | null;
  /** D_INV.PAYRATE. Display only; not saved (OI-33). Absent from a DraftDto line. */
  payRate?: number | null;
  /** D_INV.REGULAR_LENSES_TYPE. Display only; not saved (OI-33). Absent from a DraftDto line. */
  regularLensesType?: string | null;
  /** D_INV.LENS_SPECIFICATIONS. Display only; not saved (OI-33). Absent from a DraftDto line. */
  lensSpecifications?: string | null;
  /** D_INV.CONTACT_LENSES_TYPE. Display only; not saved (OI-33). Absent from a DraftDto line. */
  contactLensesType?: string | null;
  /** D_INV.F_L_INDICATOR. Display only; not saved (OI-33). Absent from a DraftDto line. */
  flIndicator?: string | null;
  /** D_INV.NUMBER_OF_PAIRS. Display only; not saved (OI-33). Absent from a DraftDto line. */
  numberOfPairs?: string | null;
  /** D_INV.INS_EMP. Display only; not saved (OI-33). Absent from a DraftDto line. */
  insEmp?: number | null;
}

/** The 32 INV_SMALL_CASH module parameters in Form declaration order. */
export interface InvoiceEntryParameters {
  /** PARAMETER.IS_HOME_CARE. */
  isHomeCare: string | null;
  /** PARAMETER.NEW_PAT_INV. */
  newPatInv: string | null;
  /** PARAMETER.THE_DOC. */
  theDoc: number | null;
  /** PARAMETER.COMP_TYPE. */
  compType: number | null;
  /** PARAMETER.PAY_VAT_CO. */
  payVatCo: string | null;
  /** PARAMETER.FROM_CHK. */
  fromChk: string | null;
  /** PARAMETER.CLAIM_FLAG. */
  claimFlag: string | null;
  /** PARAMETER.CASH_OR_CREDIT: 1 cash, 2 credit. */
  cashOrCredit: number | null;
  /** PARAMETER.DO_REVIEW. */
  doReview: string | null;
  /** PARAMETER.THE_COUNTRY. */
  theCountry: string | null;
  /** PARAMETER.CLAIM_DATE. */
  claimDate: string | null;
  /** PARAMETER.PAY_VAT. */
  payVat: string | null;
  /** PARAMETER.ONE_VISIT_960. */
  oneVisit960: number | null;
  /** PARAMETER.DIRECT_CALL. */
  directCall: string | null;
  /** PARAMETER.X422_APPROV_CHECK. */
  x422ApprovCheck: number | null;
  /** PARAMETER.DEDUCT_RATE. */
  deductRate: number | null;
  /** PARAMETER.DEDUCT_FIXED. */
  deductFixed: number | null;
  /** PARAMETER.DIRECT_COMP_SHARE. */
  directCompShare: number | null;
  /** PARAMETER.LOCAL_DOC_TYPE. */
  localDocType: number;
  /** PARAMETER.VISIT_UNIQUE. */
  visitUnique: string | null;
  /** PARAMETER.NEW_DOC. */
  newDoc: number | null;
  /** PARAMETER.WILL_DO_IMP. */
  willDoImp: string | null;
  /** PARAMETER.INV_ADMIN. */
  invAdmin: number | null;
  /** PARAMETER.INV_DATE_ADMIN. */
  invDateAdmin: number | null;
  /** PARAMETER.OPEN_FROM_ACC. */
  openFromAcc: string | null;
  /** PARAMETER.PKG_INV. */
  pkgInv: number | null;
  /** PARAMETER.CLAIM_NO: '0', '1', '2' or a claim number. */
  claimNo: string;
  /** PARAMETER.PHARAMACY_INSTALL_601. */
  pharamacyInstall601: string | null;
  /** PARAMETER.RESERV_BY_TIME_801. */
  reservByTime801: string | null;
  /** PARAMETER.SHIFT_CONYTOL_901. */
  shiftConytol901: string | null;
  /** PARAMETER.LESS_PAYMENT_970. */
  lessPayment970: string | null;
  /** PARAMETER.P_USER. */
  pUser: number | null;
}

/** Patient coverage read from V_PAT_DATA and the sub-company's COMPANYS row. */
export interface PatientCoverageSnapshot {
  /** Patient number. */
  patientNo: string | null;
  /** Patient name. */
  patientName: string | null;
  /** Company code; '0' is the cash company. */
  compCode: string | null;
  /** Company name. */
  compName: string | null;
  /** Sub-company (policy) code. */
  subCompCode: string | null;
  /** Sub-company (policy) name. */
  subCompName: string | null;
  /** Discount class code. */
  classCode: number | null;
  /** Discount class name. */
  className: string | null;
  /** Patient VAT flag. */
  payVat: string | null;
  /** Maximum deductible amount. */
  maxDeductable: number | null;
  /** Approval level. */
  approvLvl: number | null;
  /** Patient policy number (PAT_POLICY_NO). */
  patPolicyNo: string | null;
  /** Insurance number (INS_NUMBER). */
  insNumber: string | null;
  /** Insurance card end date (CARD_END). */
  cardEnd: string | null;
  /** Company contract end date. */
  contractEnd: string | null;
  /** Company active status; 2 is on hold. */
  companyIsActive: number | null;
  /** Company type; 1 direct, 2 card. */
  companyType: number | null;
  /** Patient's class reference; null when the patient has no class. */
  myClass: number | null;
  /** Class referral flag; 1 requires a referral. */
  classWithRef: number | null;
  /** Class active status; 2 is on hold. */
  classIsActive: number | null;
  /** Sub-company contract end date. */
  subCompanyContractEnd: string | null;
  /** Sub-company active status; 2 is on hold. */
  subCompanyIsActive: number | null;
}

/** Payment status values of BIL_INVOICE_API.t_preview_totals.payment_status. */
export type PaymentStatus = 'No Amount Due' | 'Unpaid' | 'Partial' | 'Paid' | 'Overpaid';

/** Invoice preview totals as returned by BIL_INVOICE_API.t_preview_totals. */
export interface PreviewTotals {
  /** line_count. */
  lineCount: number | null;
  /** total_gross. */
  totalGross: number | null;
  /** total_discount. */
  totalDiscount: number | null;
  /** total_net. */
  totalNet: number | null;
  /** pat_pay, patient share before VAT. */
  patPay: number | null;
  /** comp_pay, company share before VAT. */
  compPay: number | null;
  /** vat_total_pat. */
  vatTotalPat: number | null;
  /** vat_total_co. */
  vatTotalCo: number | null;
  /** cash_collected, amount due from the patient. */
  cashCollected: number | null;
  /** amount_1. */
  amount1: number | null;
  /** amount_2. */
  amount2: number | null;
  /** remaining_amount; negative when overpaid. */
  remainingAmount: number | null;
  /** payment_status. */
  paymentStatus: PaymentStatus | string | null;
}

/** Kinds of automatic visit line chosen after a doctor validation. */
export type VisitLineKind = 'None' | 'Consultation' | 'Review' | 'FixedService';

/** Automatic visit line chosen after a doctor validation. */
export interface VisitLineChoice {
  /** None, Consultation, Review or FixedService. */
  kind: VisitLineKind | string;
  /** Service of a FixedService line; null for every other kind. */
  serviceId: string | null;
}

/** DISC_ALERT buttons: the operator's answer to the maximum-discount alert. */
export type DiscountLimitChoice = 'MaximumDiscount' | 'Cancel';

/** One calculated line of BIL_INVOICE_API.t_editable_preview_line. */
export interface EditablePreviewLine {
  /** client_id. */
  clientId: string | null;
  /** line_no. */
  lineNo: number | null;
  /** serviceid. */
  serviceId: string | null;
  /** servicedesc. */
  serviceDesc: string | null;
  /** catid. */
  catId: number | null;
  /** list_id, the line's price list. */
  listId: number | null;
  /** curr_code. */
  currCode: string | null;
  /** qty. */
  qty: number | null;
  /** price. */
  price: number | null;
  /** plan_discount_pct. */
  planDiscountPct: number | null;
  /** plan_discount_amount. */
  planDiscountAmount: number | null;
  /** manual_discount_type. */
  manualDiscountType: string | null;
  /** manual_discount_pct. */
  manualDiscountPct: number | null;
  /** manual_discount_amount. */
  manualDiscountAmount: number | null;
  /** discount_source. */
  discountSource: string | null;
  /** disc. */
  disc: number | null;
  /** my_disc. */
  myDisc: number | null;
  /** my_price. */
  myPrice: number | null;
  /** my_net. */
  myNet: number | null;
  /** the_pay, patient share. */
  thePay: number | null;
  /** the_comp, company share. */
  theComp: number | null;
  /** vat_rate. */
  vatRate: number | null;
  /** vat_val_pat. */
  vatValPat: number | null;
  /** vat_val_co. */
  vatValCo: number | null;
  /** vat_val_pat_ex. */
  vatValPatEx: number | null;
  /** req_need_a. */
  reqNeedA: number | null;
  /** req_a_status. */
  reqAStatus: number | null;
  /** allow_manual_discount, 'Y' or 'N'. */
  allowManualDiscount: string | null;
  /** allow_price_override, 'Y' or 'N'. */
  allowPriceOverride: string | null;
  /** package_service_id. */
  packageServiceId: string | null;
  /** package_instance_id. */
  packageInstanceId: string | null;
  /** package_line_role. */
  packageLineRole: string | null;
  /** package_component_order. */
  packageComponentOrder: number | null;
  /** package_parent_line_id. */
  packageParentLineId: number | null;
  /** package_pricing_method. */
  packagePricingMethod: string | null;
  /** package_definition_token. */
  packageDefinitionToken: string | null;
  /** offer_id. */
  offerId: number | null;
  /** offer_dtl_id. */
  offerDtlId: number | null;
  /** offer_type. */
  offerType: number | null;
  /** offer_instance_id. */
  offerInstanceId: string | null;
  /** offer_line_role. */
  offerLineRole: string | null;
  /** offer_parent_line_id. */
  offerParentLineId: number | null;
  /** offer_price_applied. */
  offerPriceApplied: number | null;
  /** offer_dis_applied. */
  offerDisApplied: number | null;
  /** offer_name_snapshot. */
  offerNameSnapshot: string | null;
  /** offer_object_version_number. */
  offerObjectVersionNumber: number | null;
  /** offer_dtl_object_version_number. */
  offerDtlObjectVersionNumber: number | null;
}

/** Import counts and message of BIL_IMPORT.t_import_result. */
export interface ImportResultRow {
  /** source_type. */
  sourceType: string | null;
  /** source_count. */
  sourceCount: number | null;
  /** imported_count. */
  importedCount: number | null;
  /** skipped_rejected_count. */
  skippedRejectedCount: number | null;
  /** skipped_need_approval_count. */
  skippedNeedApprovalCount: number | null;
  /** skipped_invalid_count. */
  skippedInvalidCount: number | null;
  /** has_price_overrides, 'Y' or 'N'. */
  hasPriceOverrides: string | null;
  /** message. */
  message: string | null;
}


/** Message severities: Blocking stops the action, Warning lets it proceed. */
export type MessageSeverity = 'Blocking' | 'Warning';

/** Validation or warning message carrying the legacy MESSAG text. */
export interface MessageDto {
  /** Upper-case legacy item name, such as PATIENTNO or QTY; null for a form-level message. */
  field: string | null;
  /** Legacy message text, verbatim. */
  text: string;
  /** Blocking or Warning. */
  severity: MessageSeverity;
  /** Domain rule id, such as DR-01; null when not from a domain rule. */
  rule: string | null;
}

/** Unsaved invoice draft exchanged between client and server. */
export interface DraftDto {
  /** 32-character upper-case hexadecimal request id, kept for the life of the draft. */
  requestId: string;
  /** Database time read when the draft was created. */
  draftDate: string;
  /** Seal of the request id and draft date issued with the draft. */
  draftSeal: string | null;
  /** The T_INV header. */
  header: InvoiceHeaderDraft;
  /** The D_INV lines in grid order; a line's zero-based position is its line index. */
  lines: InvoiceLineDraft[];
  /** The Form entry parameters. */
  parameters: InvoiceEntryParameters;
  /** The operator's answer to the maximum-discount alert; null when none was given. */
  discountLimitChoice: DiscountLimitChoice | null;
}

/** Unsupplied or display-only header members omitted from request bodies. */
export type RequestOmittedHeaderMember = 'preAuthorization' | 'oferId' | 'docId1' | 'seqNo';

/** Line members omitted from request bodies, including selectable CATID and display-only fields. */
export type RequestOmittedLineMember =
  | 'catId'
  | 'fixPay'
  | 'payRate'
  | 'regularLensesType'
  | 'lensSpecifications'
  | 'contactLensesType'
  | 'flIndicator'
  | 'numberOfPairs'
  | 'insEmp';

/** Header as sent in a request body. */
export type DraftRequestHeader = Omit<InvoiceHeaderDraft, RequestOmittedHeaderMember>;

/** Line as sent in a request body. */
export type DraftRequestLine = Omit<InvoiceLineDraft, RequestOmittedLineMember>;

/** Draft request containing only members accepted by its request contract. */
export interface DraftRequestDto {
  /** 32-character upper-case hexadecimal request id, kept for the life of the draft. */
  requestId: string;
  /** Database time read when the draft was created. */
  draftDate: string;
  /** Seal of the request id and draft date issued with the draft. */
  draftSeal: string | null;
  /** The T_INV header. */
  header: DraftRequestHeader;
  /** The D_INV lines in grid order; a line's zero-based position is its line index. */
  lines: DraftRequestLine[];
  /** The Form entry parameters. */
  parameters: InvoiceEntryParameters;
  /** The operator's answer to the maximum-discount alert; null when none was given. */
  discountLimitChoice: DiscountLimitChoice | null;
}

/** Items, lines and the record accepted as validation targets. */
export type ValidateTarget =
  | 'PATIENTNO'
  | 'COMP_CODE'
  | 'DOCIDX'
  | 'CLINICID'
  | 'DEPT_WISE'
  | 'CALL'
  | 'FINALDISC_PERC'
  | 'FINALDISC'
  | 'AMOUNT_1'
  | 'AMOUNT_2'
  | 'SUB_PAYTYPE'
  | 'SERVICEID'
  | 'QTY'
  | 'LDISCT'
  | 'APPROV_REF_NO'
  | 'PRICE'
  | 'DISC'
  | 'MY_DISC'
  | 'LINE'
  | 'RECORD';

/** Body of POST /api/drafts/validate. */
export interface ValidateDraftRequest {
  /** The current draft. */
  draft: DraftRequestDto;
  /** Upper-case legacy item name, LINE or RECORD. */
  target: ValidateTarget;
  /** Zero-based index into the draft lines for a line target; null for a header target. */
  lineIndex: number | null;
}

/** Body of POST /api/invoices. */
export interface CreateInvoiceRequest {
  /** The draft to save, carrying its request id and any discount-limit choice. */
  draft: DraftRequestDto;
}

/** Body of POST /api/imports/requests. */
export interface ImportRequestsRequest {
  /** The current draft. */
  draft: DraftRequestDto;
}

/** Body of POST /api/imports/visit-line. */
export interface VisitLineRequest {
  /** The current draft. */
  draft: DraftRequestDto;
}

/** Body of POST /api/imports/package. */
export interface PackageImportRequest {
  /** The current draft. */
  draft: DraftRequestDto;
  /** Service id of the package to expand (SERVICEID). */
  packageServiceId: string;
  /** Parent source id passed as p_parent_source_id; null when none. */
  parentSourceId: string | null;
}

/** Body of POST /api/imports/bundled-offer. */
export interface BundledOfferRequest {
  /** The current draft. */
  draft: DraftRequestDto;
  /** OFERID as the exact decimal text of the OFFERS row. */
  offerId: string;
  /** Number of bundles to load. */
  bundleQty: DecimalValue;
}

/** Upper-case legacy item names used as keys of adjusted values. */
export type AdjustedKey =
  | 'AMOUNT_1'
  | 'AMOUNT_2'
  | 'REUND'
  | 'CLAIM_NO'
  | 'ADD_TO_LIST'
  | 'SUB_PAYTYPE'
  | 'PAYTYPE'
  | 'FINALDISC_PERC'
  | 'FINALDISC'
  | 'DISC_T'
  | 'DOCIDX'
  | 'LDISCT';

/** Item values changed by the server, keyed by upper-case legacy item name. */
export type AdjustedValues = Partial<Record<AdjustedKey, unknown>> & Record<string, unknown>;

/** Response of GET /api/drafts/new. */
export interface NewDraftResponse {
  /** The new draft with its defaults applied. */
  draft: DraftDto;
  /** Warnings raised while defaulting the draft. */
  messages: MessageDto[];
  /** Advisory open-item ids. */
  openItems: string[];
}

/** Response of POST /api/drafts/validate. */
export interface ValidateDraftResponse {
  /** Blocking and warning messages together. */
  messages: MessageDto[];
  /** Item values the rules changed. */
  adjusted: AdjustedValues;
  /** Advisory open-item ids. */
  openItems: string[];
  /** Automatic visit line chosen for a doctor validation; null otherwise. */
  visitLine: VisitLineChoice | null;
  /** For a line target, whether an operator-entered PRICE is accepted on the validated line; null otherwise. */
  priceEditable: boolean | null;
  /** Service id of the validated line that priceEditable was judged on; null when priceEditable is null. */
  priceJudgedServiceId: string | null;
  /** Patient number of the request that priceEditable was judged on; null when priceEditable is null. */
  priceJudgedPatientNo: string | null;
  /** Company code of the request that priceEditable was judged on; null when priceEditable is null. */
  priceJudgedCompCode: string | null;
  /** Coverage of the validated patient for a PATIENTNO target with a patient number; null otherwise. */
  coverage: CoverageResponse | null;
}

/** Response of GET /api/patients/{patientNo}/coverage. */
export interface CoverageResponse {
  /** Patient coverage; null when the patient has none. */
  coverage: PatientCoverageSnapshot | null;
  /** Pay type decided for the patient: 0 when undetermined; otherwise 1 cash or 2 credit. */
  payType: number;
  /** Eligibility messages, blocking and warning. */
  messages: MessageDto[];
  /** Advisory open-item ids. */
  openItems: string[];
}

/** Response of POST /api/invoices/preview. */
export interface PreviewResponse {
  /** Calculated lines as returned by the package. */
  lines: EditablePreviewLine[];
  /** Package totals and payment status. */
  totals: PreviewTotals;
  /** Refund to the patient (REUND). */
  refund: number;
  /** Amount 1 plus amount 2 (CASH_COLLECTED item). */
  totalCollected: number;
  /** Messages returned with the preview. */
  messages: MessageDto[];
  /** Advisory open-item ids. */
  openItems: string[];
  /** Client ids of the draft lines on which an operator-entered PRICE is accepted. */
  priceEditableClientIds: string[];
  /** Patient number of the request the PRICE editability was judged on. */
  priceJudgedPatientNo: string | null;
  /** Company code of the request the PRICE editability was judged on. */
  priceJudgedCompCode: string | null;
}

/** Posting-stage flags 'Y' or 'N' of a saved invoice. */
export interface PostingFlags {
  /** Payment posted. */
  payment?: string | null;
  /** Queue posted. */
  queue?: string | null;
  /** Stock posted. */
  stock?: string | null;
  /** Print URL built. */
  printUrl?: string | null;
  /** SMS sent. */
  sms?: string | null;
}

/** Response of POST /api/invoices. */
export interface CreateInvoiceResponse {
  /** Invoice number saved or returned by a replay (INV_NO). */
  invNo: number;
  /** Package result message. */
  message: string | null;
  /** Posting-stage flags. */
  postingFlags: PostingFlags;
  /** Warning messages. */
  messages: MessageDto[];
  /** Open-item ids listed with the result. */
  openItems: string[];
}

/** Response of GET /api/invoices/{invNo}: a read-only saved invoice. */
export interface InvoiceViewResponse {
  /** Saved invoice header. */
  header: InvoiceHeaderDraft;
  /** Saved invoice lines. */
  lines: InvoiceLineDraft[];
  /** Lookup names and saved values by upper-case key, with line totals summed at read time; CASH_COLLECTED is the amount due, TOTAL_COLLECTED amount 1 plus amount 2. */
  display: Record<string, unknown>;
  /** Whether the invoice is read-only. */
  readOnly: boolean;
  /** Open-item ids that apply to this view. */
  openItems: string[];
}

/** Upper-case D_INV item names keying each line of a MoreDetailsResponse. */
export type MoreDetailsLineKey =
  | 'D_INV_ROW_ID'
  | 'SERVICEID'
  | 'TEETH_NO'
  | 'TOOTH_SURFACE'
  | 'APPROV_DATE'
  | 'APPROV_VALIDITY'
  | 'APPROV_REF_NO'
  | 'REQ_NEED_A'
  | 'REQ_A_STATUS'
  | 'REGULAR_LENSES_TYPE'
  | 'LENS_SPECIFICATIONS'
  | 'CONTACT_LENSES_TYPE'
  | 'F_L_INDICATOR'
  | 'NUMBER_OF_PAIRS'
  | 'INS_EMP'
  | 'INS_EMP_NAME';

/** Response of GET /api/invoices/{invNo}/more: read-only MORE fields of a saved invoice. */
export interface MoreDetailsResponse {
  /** Invoice number (INV_NO). */
  invNo: number;
  /** Insurance number (INS_NUMBER). */
  insNumber: string | null;
  /** Insurance card expiry date (CARD_END). */
  cardEnd: string | null;
  /** Insurance policy number (PAT_POLICY_NO). */
  patPolicyNo: string | null;
  /** Per-line fields keyed by upper-case D_INV item name (MoreDetailsLineKey). */
  lines: Record<string, unknown>[];
  /** Store-transfer row ids (TRANS_M_ROW_ID). */
  transMRowIds: string[];
}

/** Response of the four POST /api/imports endpoints. */
export interface ImportResponse {
  /** Lines to add to the draft. */
  lines: InvoiceLineDraft[];
  /** Import counts and message; null where the operation has none. */
  result: ImportResultRow | null;
  /** Header values changed by the import, such as ADD_TO_LIST. */
  adjusted: AdjustedValues;
  /** Notices and warnings, with legacy text verbatim. */
  messages: MessageDto[];
  /** Open-item ids that apply to this import. */
  openItems: string[];
}

/** Response of GET /api/lov/{name}. */
export interface LovResponse {
  /** List-of-values name in upper case, such as COMPANY1_2. */
  name: string;
  /** Record-group rows, each keyed by upper-case column name. */
  rows: Record<string, unknown>[];
  /** True when the rows can be viewed but not chosen. */
  viewOnly: boolean;
  /** Messages returned with the list. */
  messages: MessageDto[];
  /** Advisory open-item ids that apply to the list. */
  openItems: string[];
}

/** One entry of GET /api/lookups/invoice-types or /api/lookups/currencies. */
export interface LookupItem {
  /** List value as text. */
  code: string;
  /** Display label. */
  name: string;
}

/** Response of GET /api/invoices/last. */
export interface LastInvoiceNoResponse {
  /** Highest invoice number of the operator's information centre. */
  invNo: number;
}

/** Document kinds of POST /api/invoices/{invNo}/documents/{kind}. */
export type DocumentKind = 'invoice' | 'patient-card' | 'barcode-sms' | 'iqama-check';

/** LOV names routed by GET /api/lov/{name}. */
export type LovName =
  | 'COMPANY1_2'
  | 'SUB_COMPANY'
  | 'THE_CLASS'
  | 'PAY_TYPE1'
  | 'PAY_TYPE2'
  | 'DOC'
  | 'RESERV_NO'
  | 'OFFERS'
  | 'CAT'
  | 'SERVICES'
  | 'DOC1';

/** Query-string binds of GET /api/lov/{name}. */
export interface LovBinds {
  /** T_INV.COMP_CODE. */
  compCode?: string | number | null;
  /** T_INV.SUB_COMP_CODE. */
  subCompCode?: string | number | null;
  /** T_INV.DOCIDX. */
  docIdx?: string | number | null;
  /** T_INV.PATIENTNO. */
  patientNo?: string | number | null;
  /** T_INV.PAYTYPE. */
  payType?: string | number | null;
  /** Draft date, bound as INVDATE. */
  draftDate?: string | null;
}

/** Error-contract type values of an application/problem+json body. */
export type ProblemType =
  | 'field-validation'
  | 'oracle-business-error'
  | 'operator-context-missing'
  | 'open-item'
  | 'oracle-unavailable'
  | 'oracle-error'
  | 'not-found';

/** Oracle error catalogue kinds carried by a problem body. */
export type ProblemKind =
  | 'RequestLinesStale'
  | 'DefinitionStale'
  | 'IdempotencyConflict'
  | 'OperatorContextMissing'
  | 'UnexpandedPackageParent';

/** An application/problem+json error body. */
export interface ProblemPayload {
  /** Error-contract type. */
  type: ProblemType | string;
  /** Short fixed title. */
  title: string;
  /** HTTP status. */
  status: number;
  /** Blocking and warning messages (field-validation, open-item). */
  messages?: MessageDto[];
  /** Open-item ids (field-validation, open-item). */
  openItems?: string[];
  /** Item values changed by the rules (field-validation). */
  adjusted?: AdjustedValues;
  /** Open-item id of a blocked operation (open-item). */
  openItemId?: string | null;
  /** Oracle text after the ORA prefix, or 'The Oracle error text could not be read.' (oracle-business-error, operator-context-missing); the open-item message (open-item); the text naming what was not found (not-found); 'The Oracle connection string is not configured or is not well-formed.' (oracle-error with no number). */
  message?: string | null;
  /** Signed Oracle error number, such as -20931. */
  oracleErrorNumber?: number | null;
  /** Attributed source package, or UNKNOWN. */
  package?: string | null;
  /** Upper-case legacy item name the error maps to. */
  field?: string | null;
  /** Verbatim legacy Form text shown instead of message. */
  legacyText?: string | null;
  /** Oracle error catalogue kind. */
  kind?: ProblemKind | string | null;
  /** Absent operator-context headers (operator-context-missing). */
  missing?: string[];
}
