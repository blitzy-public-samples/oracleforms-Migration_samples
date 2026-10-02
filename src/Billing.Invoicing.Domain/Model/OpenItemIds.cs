namespace Billing.Invoicing.Domain.Model;

/// <summary>Open-item register ids.</summary>
public static class OpenItemIds
{
    /// <summary><c>BIL_CASHIER_SHIFT.ASSERT_CAN_CREATE_INVOICE</c>.</summary>
    public const string OI01 = "OI-01";

    /// <summary><c>BIL_TYPES</c>.</summary>
    public const string OI02 = "OI-02";

    /// <summary><c>BIL_PATIENT_CONTEXT</c>.</summary>
    public const string OI03 = "OI-03";

    /// <summary><c>BIL_SERVICE_CONTEXT</c>.</summary>
    public const string OI04 = "OI-04";

    /// <summary><c>BIL_PRICE_RULE</c>.</summary>
    public const string OI05 = "OI-05";

    /// <summary><c>BIL_CLASS_RULE</c>.</summary>
    public const string OI06 = "OI-06";

    /// <summary><c>BIL_OFFER_RULE</c>.</summary>
    public const string OI07 = "OI-07";

    /// <summary><c>BIL_PAYMENT</c>.</summary>
    public const string OI08 = "OI-08";

    /// <summary><c>BIL_QUEUE_POSTING</c>.</summary>
    public const string OI09 = "OI-09";

    /// <summary><c>BIL_STOCK_POSTING</c>.</summary>
    public const string OI10 = "OI-10";

    /// <summary><c>BIL_REPORTS_PRINT</c>.</summary>
    public const string OI11 = "OI-11";

    /// <summary><c>BIL_MESSAGE</c>.</summary>
    public const string OI12 = "OI-12";

    /// <summary><c>BIL_AUDIT</c>.</summary>
    public const string OI13 = "OI-13";

    /// <summary><c>INS_COMP_UTIL</c>.</summary>
    public const string OI14 = "OI-14";

    /// <summary>Schema DDL, group 1: objects the Data layer queries or updates.</summary>
    public const string OI15 = "OI-15";

    /// <summary>Deployment state of the three <c>BIL_*</c> packages.</summary>
    public const string OI16 = "OI-16";

    /// <summary><c>FND_APP_SECURITY</c> and <c>APEX_UTIL.GET_SESSION_STATE</c> fallbacks.</summary>
    public const string OI17 = "OI-17";

    /// <summary><c>APEX_COLLECTION</c> in <c>BUILD_IMPORT_PREVIEW_COLLECTION</c>.</summary>
    public const string OI18 = "OI-18";

    /// <summary><c>GET_NEXT_INVOICE_NO</c>.</summary>
    public const string OI19 = "OI-19";

    /// <summary><c>VALIDATE_TOTAL_INV</c>.</summary>
    public const string OI20 = "OI-20";

    /// <summary><c>GET_ELLIGABILTY</c>.</summary>
    public const string OI21 = "OI-21";

    /// <summary><c>DAY_TO_DAYES</c>.</summary>
    public const string OI22 = "OI-22";

    /// <summary><c>GET_PAYID_VALUE</c> and the paid-before deductible release.</summary>
    public const string OI23 = "OI-23";

    /// <summary><c>GET_PRICE_PLAN</c>.</summary>
    public const string OI24 = "OI-24";

    /// <summary><c>GET_U_PREV20</c> permissions.</summary>
    public const string OI25 = "OI-25";

    /// <summary><c>SEND_MESSAG</c>.</summary>
    public const string OI26 = "OI-26";

    /// <summary>Report object <c>XX</c> (<c>nat.rdf</c>) and the <c>rwservlet</c> URL base.</summary>
    public const string OI27 = "OI-27";

    /// <summary>Form <c>INVOICE_PAYMENT</c>.</summary>
    public const string OI28 = "OI-28";

    /// <summary>Object library <c>HMISTEXT.olb</c>.</summary>
    public const string OI29 = "OI-29";

    /// <summary>Globals and parameters set by the calling module.</summary>
    public const string OI30 = "OI-30";

    /// <summary>Package consumption (<c>PKG_INV</c>, <c>PACKAGE_CONS_M</c> / <c>PACKAGE_CONS</c>).</summary>
    public const string OI31 = "OI-31";

    /// <summary><c>OKA</c> / <c>CHK_ADV_CLASS</c> sub-rules with no package trace.</summary>
    public const string OI32 = "OI-32";

    /// <summary>Legacy columns the package inputs do not carry.</summary>
    public const string OI33 = "OI-33";

    /// <summary>APEX-only check that the two payment methods differ.</summary>
    public const string OI34 = "OI-34";

    /// <summary>APEX-only: amounts 1 and 2 non-negative.</summary>
    public const string OI35 = "OI-35";

    /// <summary>APEX-only: amount and payment-method pairing.</summary>
    public const string OI36 = "OI-36";

    /// <summary>APEX-only: specialty required.</summary>
    public const string OI37 = "OI-37";

    /// <summary>APEX-only JavaScript safeguards.</summary>
    public const string OI38 = "OI-38";

    /// <summary>APEX-only draft persistence and request-selection UI.</summary>
    public const string OI39 = "OI-39";

    /// <summary>Schema DDL, group 2: objects only the retained packages use.</summary>
    public const string OI40 = "OI-40";

    /// <summary>Schema DDL, group 3: objects only unexecuted Form constructs use.</summary>
    public const string OI41 = "OI-41";

    /// <summary><c>GET_HTFN2</c>.</summary>
    public const string OI42 = "OI-42";

    /// <summary><c>FIND_PROMPT</c>.</summary>
    public const string OI43 = "OI-43";

    /// <summary><c>SILENT_COMMET00</c>.</summary>
    public const string OI44 = "OI-44";

    /// <summary>Report <c>inv_small_cash.jsp</c>.</summary>
    public const string OI45 = "OI-45";

    /// <summary>Report <c>inv_form2.jsp</c>.</summary>
    public const string OI46 = "OI-46";

    /// <summary>Report <c>PAT_CARD_INV.jsp</c>.</summary>
    public const string OI47 = "OI-47";

    /// <summary>Report <c>iqama_check.jsp</c>.</summary>
    public const string OI48 = "OI-48";

    /// <summary>Report <c>LIST1111.jsp</c>.</summary>
    public const string OI49 = "OI-49";

    /// <summary>Form <c>phy_req_note</c>.</summary>
    public const string OI50 = "OI-50";

    /// <summary>Form <c>translate</c>.</summary>
    public const string OI51 = "OI-51";

    /// <summary>Form <c>st_trans</c>.</summary>
    public const string OI52 = "OI-52";

    /// <summary>Object library <c>HMISBUTTON.olb</c>.</summary>
    public const string OI53 = "OI-53";

    /// <summary>Object library <c>BUSINESSXP.olb</c>.</summary>
    public const string OI54 = "OI-54";

    /// <summary>Object library <c>HMISCANVAS.olb</c>.</summary>
    public const string OI55 = "OI-55";

    /// <summary>Package operation that edits a saved invoice or its lines.</summary>
    public const string OI56 = "OI-56";

    /// <summary>APEX-only whole-occurrence package and bundle removal.</summary>
    public const string OI57 = "OI-57";

    /// <summary>APEX-only per-row editability locks.</summary>
    public const string OI58 = "OI-58";
}
