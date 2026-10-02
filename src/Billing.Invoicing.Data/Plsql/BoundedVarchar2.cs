using System.Data;
using System.Text;
using Billing.Invoicing.Data.Errors;
using Dapper;
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

    /// <summary>Width of t_inv.claim_no (Form item T_INV.CLAIM_NO).</summary>
    internal const int ClaimNoBytes = 40;

    /// <summary>Width of t_inv.comp_code (Form item T_INV.COMP_CODE).</summary>
    internal const int CompCodeBytes = 10;

    /// <summary>Width of t_inv.sub_comp_code (Form item T_INV.SUB_COMP_CODE).</summary>
    internal const int SubCompCodeBytes = 10;

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

        // A value over the destination width in characters or UTF-8 bytes is rejected, never truncated to fit (D-108).
        if (Rejection(bindName, null, value, maxBytes, paramName) is { } rejection)
        {
            throw rejection;
        }
    }

    /// <summary>Returns the rejection of a value longer than <paramref name="maxBytes"/> in characters, else in UTF-8 bytes, or null when the value fits.</summary>
    /// <param name="subject">Name the rejection text starts with.</param>
    /// <param name="field">Legacy item the rejection is placed on, or null for a form-level rejection.</param>
    /// <param name="value">Value to check.</param>
    /// <param name="maxBytes">Destination width in bytes.</param>
    /// <param name="paramName">Argument the rejection names.</param>
    /// <returns>An <see cref="ArgumentException"/> holding its text under <see cref="OracleFailureTranslator.BindingRejectionKey"/> and a non-null field under <see cref="OracleFailureTranslator.BindingRejectionFieldKey"/>; null when the value fits.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    internal static ArgumentException? Rejection(string subject, string? field, string value, int maxBytes, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value);

        string? text = null;
        if (value.Length > maxBytes)
        {
            text = $"{subject} has {value.Length} characters; at most {maxBytes} can be bound.";
        }
        else if (Encoding.UTF8.GetByteCount(value) is var bytes && bytes > maxBytes)
        {
            text = $"{subject} has {bytes} bytes in UTF-8; at most {maxBytes} can be bound.";
        }

        if (text is null)
        {
            return null;
        }

        var error = new ArgumentException(text, paramName);
        error.Data[OracleFailureTranslator.BindingRejectionKey] = text;
        if (field is not null)
        {
            error.Data[OracleFailureTranslator.BindingRejectionFieldKey] = field;
        }

        return error;
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

    /// <summary>Adds a Dapper VARCHAR2 IN bind of size <paramref name="maxBytes"/> after <see cref="Validate"/>; a null value binds as null.</summary>
    /// <param name="parameters">Dapper parameters receiving the bind.</param>
    /// <param name="bindName">Bind name of the parameter.</param>
    /// <param name="value">Value to bind.</param>
    /// <param name="maxBytes">Destination width in bytes.</param>
    /// <param name="paramName">Argument a rejection names.</param>
    /// <exception cref="ArgumentException">The value exceeds <paramref name="maxBytes"/>; <see cref="Exception.Data"/> holds the text under <see cref="OracleFailureTranslator.BindingRejectionKey"/>.</exception>
    internal static void AddInput(DynamicParameters parameters, string bindName, string? value, int maxBytes, string paramName)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        Validate(bindName, value, maxBytes, paramName);
        parameters.Add(bindName, AnsiString(value, maxBytes));
    }

    /// <summary>Returns the elements of a Dapper <c>IN</c> list bind, each a VARCHAR2 of size <paramref name="maxBytes"/>, after <see cref="Validate"/> of every value.</summary>
    /// <param name="bindName">Bind name of the list.</param>
    /// <param name="values">Values to bind, in list order.</param>
    /// <param name="maxBytes">Destination width in bytes of each element.</param>
    /// <param name="paramName">Argument a rejection names.</param>
    /// <returns>The sized elements in <paramref name="values"/> order.</returns>
    /// <exception cref="ArgumentException">A value exceeds <paramref name="maxBytes"/>; <see cref="Exception.Data"/> holds the text under <see cref="OracleFailureTranslator.BindingRejectionKey"/>.</exception>
    internal static DbString[] InputList(string bindName, IEnumerable<string> values, int maxBytes, string paramName)
    {
        ArgumentNullException.ThrowIfNull(values);

        var elements = new List<DbString>();
        foreach (var value in values)
        {
            Validate(bindName, value, maxBytes, paramName);
            elements.Add(AnsiString(value, maxBytes));
        }

        return elements.ToArray();
    }

    /// <summary>ANSI Dapper string bound at size <paramref name="maxBytes"/>.</summary>
    private static DbString AnsiString(string? value, int maxBytes) => new()
    {
        Value = value,
        IsAnsi = true,
        Length = maxBytes,
    };
}
