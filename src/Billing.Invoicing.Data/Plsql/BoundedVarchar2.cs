using System.Data;
using System.Text;
using Billing.Invoicing.Data.Errors;
using Oracle.ManagedDataAccess.Client;

namespace Billing.Invoicing.Data.Plsql;

/// <summary>Checks scalar VARCHAR2 inputs against their destination widths in bytes and binds them sized to those widths. UNVERIFIED against Oracle.</summary>
internal static class BoundedVarchar2
{
    /// <summary>Width of t_inv.patientno (Form item T_INV.PATIENTNO).</summary>
    internal const int PatientNoBytes = 12;

    /// <summary>Width of t_inv.info_center_id (Form item T_INV.INFO_CENTER_ID).</summary>
    internal const int InfoCenterIdBytes = 10;

    /// <summary>Width of d_inv.serviceid (Form item D_INV.SERVICEID).</summary>
    internal const int ServiceIdBytes = 20;

    /// <summary>Largest request id, in bytes, that CREATE_FULL_INVOICE accepts.</summary>
    internal const int RequestIdBytes = 64;

    /// <summary>Width of bil_import.t_import_line.source_id, passed as the parent source id.</summary>
    internal const int SourceIdBytes = 100;

    /// <summary>Longest text of pat_visit_m.visit_unique, a NUMBER(38) of scale 0: a sign and 38 digits.</summary>
    internal const int VisitUniqueBytes = 39;

    /// <summary>Width of the new visit type BIL_IMPORT.GET_VISIT_LINE holds.</summary>
    internal const int NewVisitTypeBytes = 30;

    /// <summary>Largest SQL VARCHAR2 value; the ceiling for the unsized application session id and user.</summary>
    internal const int SqlVarchar2Bytes = 4000;

    /// <summary>Width of a Y/N flag.</summary>
    internal const int FlagBytes = 1;

    /// <summary>Rejects a value longer than <paramref name="maxBytes"/> in characters or UTF-8 bytes; a null value passes.</summary>
    /// <param name="bindName">Bind name the rejection text names.</param>
    /// <param name="value">Value to check.</param>
    /// <param name="maxBytes">Destination width in bytes.</param>
    /// <param name="paramName">Argument the rejection names.</param>
    /// <exception cref="ArgumentException">The value exceeds <paramref name="maxBytes"/>; <see cref="Exception.Data"/> holds the text under <see cref="OracleFailureTranslator.BindingRejectionKey"/>.</exception>
    internal static void Validate(string bindName, string? value, int maxBytes, string paramName)
    {
        if (value is null)
        {
            return;
        }

        // A value over the destination width in characters or UTF-8 bytes is rejected, never truncated to fit.
        string? rejection = null;
        if (value.Length > maxBytes)
        {
            rejection = $"{bindName} has {value.Length} characters; at most {maxBytes} can be bound.";
        }
        else if (Encoding.UTF8.GetByteCount(value) is var bytes && bytes > maxBytes)
        {
            rejection = $"{bindName} has {bytes} bytes in UTF-8; at most {maxBytes} can be bound.";
        }

        if (rejection is not null)
        {
            var error = new ArgumentException(rejection, paramName);
            error.Data[OracleFailureTranslator.BindingRejectionKey] = rejection;
            throw error;
        }
    }

    /// <summary>Creates a VARCHAR2 IN parameter of size <paramref name="maxBytes"/> after <see cref="Validate"/>, sending a null value as <see cref="DBNull.Value"/>.</summary>
    /// <param name="bindName">Bind name of the parameter.</param>
    /// <param name="value">Value to bind.</param>
    /// <param name="maxBytes">Destination width in bytes.</param>
    /// <param name="paramName">Argument a rejection names.</param>
    /// <returns>The sized input parameter.</returns>
    /// <exception cref="ArgumentException">The value exceeds <paramref name="maxBytes"/>; <see cref="Exception.Data"/> holds the text under <see cref="OracleFailureTranslator.BindingRejectionKey"/>.</exception>
    internal static OracleParameter Input(string bindName, string? value, int maxBytes, string paramName)
    {
        Validate(bindName, value, maxBytes, paramName);
        return new OracleParameter(bindName, OracleDbType.Varchar2)
        {
            Direction = ParameterDirection.Input,
            Size = maxBytes,
            Value = (object?)value ?? DBNull.Value,
        };
    }
}
