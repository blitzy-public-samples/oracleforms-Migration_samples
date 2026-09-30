using System.Data;
using System.Text;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Domain.Model;
using Oracle.ManagedDataAccess.Client;

namespace Billing.Invoicing.Data.Plsql;

/// <summary>Binds an invoice header draft and its operator as the 21 scalar <c>h_*</c> inputs of <c>BIL_INVOICE_ENGINE.t_header_input</c>. UNVERIFIED against Oracle.</summary>
public static class HeaderInputBinder
{
    private const int PercentDiscountMode = 1;

    // t_header_input text field widths in bytes, from the T_INV Form item lengths. UNVERIFIED against the T_INV DDL.
    private const int PatientNoWidth = 12;
    private const int CurrCodeWidth = 3;
    private const int PreAuthorizationWidth = 20;
    private const int ClaimNoWidth = 40;
    private const int ClaimFlagWidth = 2;
    private const int NoteNoWidth = 40;
    private const int MachineNWidth = 15;
    private const int InfoCenterIdWidth = 10;

    /// <summary>Builds the <c>h_*</c> input parameters of the header, in <c>t_header_input</c> field order.</summary>
    /// <param name="header">Invoice header draft.</param>
    /// <param name="operatorContext">Operator whose user number, machine name and information centre are bound.</param>
    /// <returns>21 input parameters named without the colon; a null value is sent as <see cref="DBNull.Value"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="header"/> or <paramref name="operatorContext"/> is null.</exception>
    /// <exception cref="ArgumentException">A text value is longer than its t_header_input field width in characters or UTF-8 bytes.</exception>
    public static IReadOnlyList<OracleParameter> Bind(InvoiceHeaderDraft header, OperatorContext operatorContext)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(operatorContext);

        // Only the final-discount field of the active entry mode (DISC_T) is bound; the other is null.
        var percentMode = header.DiscT == PercentDiscountMode;

        var parameters = new List<OracleParameter>(21);
        try
        {
            parameters.Add(Text("h_patientno", header.PatientNo, PatientNoWidth, nameof(header)));
            // invdate is the draft date, never InvDate.
            parameters.Add(Date("h_invdate", header.DraftDate));
            parameters.Add(Number("h_invtypeid", header.InvTypeId));
            parameters.Add(Number("h_paytype", header.PayType));
            parameters.Add(Number("h_sub_paytype", header.SubPayType));
            parameters.Add(Number("h_sub_paytype2", header.SubPayType2));
            parameters.Add(Number("h_clinicid", header.ClinicId));
            parameters.Add(Number("h_docid", header.DocId));
            parameters.Add(Text("h_curr_code", header.CurrCode, CurrCodeWidth, nameof(header)));
            // pre_authorization is always null, whatever the header holds.
            parameters.Add(Text("h_pre_authorization", null, PreAuthorizationWidth, nameof(header)));
            parameters.Add(Text("h_claim_no", header.ClaimNo, ClaimNoWidth, nameof(header)));
            parameters.Add(Text("h_claim_flag", header.ClaimFlag, ClaimFlagWidth, nameof(header)));
            parameters.Add(Text("h_note_no", header.NoteNo, NoteNoWidth, nameof(header)));
            parameters.Add(Number("h_finaldisc_perc", percentMode ? header.FinalDiscPerc : null));
            parameters.Add(Number("h_finaldisc", percentMode ? null : header.FinalDisc));
            parameters.Add(Number("h_amount_1", header.Amount1));
            parameters.Add(Number("h_amount_2", header.Amount2));
            parameters.Add(Number("h_add_to_list", header.AddToList));
            // user_no, machine_n and info_center_id come from the operator context, never from the header.
            parameters.Add(Number("h_user_no", operatorContext.UserNo));
            parameters.Add(Text("h_machine_n", operatorContext.MachineName, MachineNWidth, nameof(operatorContext)));
            parameters.Add(Text("h_info_center_id", operatorContext.InfoCenterId, InfoCenterIdWidth, nameof(operatorContext)));
        }
        catch
        {
            // A rejected value disposes the parameters built before it.
            foreach (var parameter in parameters)
            {
                parameter.Dispose();
            }

            throw;
        }

        return parameters.AsReadOnly();
    }

    /// <summary>Numeric input bound as <see cref="OracleDbType.Decimal"/>.</summary>
    private static OracleParameter Number(string name, int? value) => Input(name, OracleDbType.Decimal, value);

    /// <summary>Numeric input bound as <see cref="OracleDbType.Decimal"/>.</summary>
    private static OracleParameter Number(string name, decimal? value) => Input(name, OracleDbType.Decimal, value);

    /// <summary>Text input bound as <see cref="OracleDbType.Varchar2"/> with <c>Size</c> set to its destination width, rejecting a longer value.</summary>
    private static OracleParameter Text(string name, string? value, int maxLength, string paramName)
    {
        if (value is not null)
        {
            // A value over the field width in characters or UTF-8 bytes is rejected, never truncated to fit.
            string? rejection = null;
            if (value.Length > maxLength)
            {
                rejection = $"Invoice header: {name} has {value.Length} characters; at most {maxLength} can be bound.";
            }
            else if (Encoding.UTF8.GetByteCount(value) is var bytes && bytes > maxLength)
            {
                rejection = $"Invoice header: {name} has {bytes} bytes in UTF-8; at most {maxLength} can be bound.";
            }

            if (rejection is not null)
            {
                var error = new ArgumentException(rejection, paramName);
                error.Data[OracleFailureTranslator.BindingRejectionKey] = rejection;
                throw error;
            }
        }

        var parameter = Input(name, OracleDbType.Varchar2, value);
        parameter.Size = maxLength;
        return parameter;
    }

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
