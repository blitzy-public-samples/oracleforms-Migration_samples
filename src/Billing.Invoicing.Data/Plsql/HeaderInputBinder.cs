using System.Data;
using Billing.Invoicing.Domain.Model;
using Oracle.ManagedDataAccess.Client;

namespace Billing.Invoicing.Data.Plsql;

/// <summary>Binds an invoice header draft and its operator as the 21 scalar <c>h_*</c> inputs of <c>BIL_INVOICE_ENGINE.t_header_input</c>. UNVERIFIED against Oracle.</summary>
public static class HeaderInputBinder
{
    private const int PercentDiscountMode = 1;

    /// <summary>Builds the <c>h_*</c> input parameters of the header, in <c>t_header_input</c> field order.</summary>
    /// <param name="header">Invoice header draft.</param>
    /// <param name="operatorContext">Operator whose user number, machine name and information centre are bound.</param>
    /// <returns>21 input parameters named without the colon; a null value is sent as <see cref="DBNull.Value"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="header"/> or <paramref name="operatorContext"/> is null.</exception>
    public static IReadOnlyList<OracleParameter> Bind(InvoiceHeaderDraft header, OperatorContext operatorContext)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(operatorContext);

        // Only the final-discount field of the active entry mode (DISC_T) is bound; the other is null.
        var percentMode = header.DiscT == PercentDiscountMode;

        return
        [
            Text("h_patientno", header.PatientNo),
            // invdate is the draft date, never InvDate.
            Date("h_invdate", header.DraftDate),
            Number("h_invtypeid", header.InvTypeId),
            Number("h_paytype", header.PayType),
            Number("h_sub_paytype", header.SubPayType),
            Number("h_sub_paytype2", header.SubPayType2),
            Number("h_clinicid", header.ClinicId),
            Number("h_docid", header.DocId),
            Text("h_curr_code", header.CurrCode),
            // pre_authorization is always null, whatever the header holds.
            Text("h_pre_authorization", null),
            Text("h_claim_no", header.ClaimNo),
            Text("h_claim_flag", header.ClaimFlag),
            Text("h_note_no", header.NoteNo),
            Number("h_finaldisc_perc", percentMode ? header.FinalDiscPerc : null),
            Number("h_finaldisc", percentMode ? null : header.FinalDisc),
            Number("h_amount_1", header.Amount1),
            Number("h_amount_2", header.Amount2),
            Number("h_add_to_list", header.AddToList),
            // user_no, machine_n and info_center_id come from the operator context, never from the header.
            Number("h_user_no", operatorContext.UserNo),
            Text("h_machine_n", operatorContext.MachineName),
            Text("h_info_center_id", operatorContext.InfoCenterId),
        ];
    }

    /// <summary>Numeric input bound as <see cref="OracleDbType.Decimal"/>.</summary>
    private static OracleParameter Number(string name, int? value) => Input(name, OracleDbType.Decimal, value);

    /// <summary>Numeric input bound as <see cref="OracleDbType.Decimal"/>.</summary>
    private static OracleParameter Number(string name, decimal? value) => Input(name, OracleDbType.Decimal, value);

    /// <summary>Text input bound as <see cref="OracleDbType.Varchar2"/>.</summary>
    private static OracleParameter Text(string name, string? value) => Input(name, OracleDbType.Varchar2, value);

    /// <summary>Date input bound as <see cref="OracleDbType.Date"/>.</summary>
    private static OracleParameter Date(string name, DateTime value) => Input(name, OracleDbType.Date, value);

    /// <summary>Creates one input parameter of the given type, sending a null value as <see cref="DBNull.Value"/>.</summary>
    private static OracleParameter Input(string name, OracleDbType dbType, object? value) =>
        new(name, dbType)
        {
            Direction = ParameterDirection.Input,
            Value = value ?? DBNull.Value,
        };
}
