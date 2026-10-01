using System.Data;
using Billing.Invoicing.Domain.Model;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

namespace Billing.Invoicing.Data.Plsql;

/// <summary>Binds draft lines as the 35 scalar associative arrays of BIL_INVOICE_ENGINE.t_line_input plus line_count. UNVERIFIED against Oracle.</summary>
public static class LineInputBinder
{
    private const string LineCountParameterName = "line_count";
    private const int MaxVarchar2Length = 4000;

    // t_line_input field widths in bytes; the d_inv.%type ones follow the D_INV Form items and are UNVERIFIED.
    private const int ServiceIdLength = 20;
    private const int UsePriceOverrideLength = 1;
    private const int DiscountTypeLength = 1;
    private const int TeethNoLength = 2;
    private const int ToothSurfaceLength = 7;
    private const int TeethNo2Length = 2;
    private const int ApprovRefNoLength = 20;
    private const int PackageDefinitionTokenLength = 64;

    private const string ComponentLineRole = "COMPONENT";

    /// <summary>Builds the IN associative-array parameters for <c>l_serviceid</c> … <c>l_offer_dtl_object_version_number</c> and the <c>line_count</c> scalar.</summary>
    /// <param name="lines">Draft lines in bind order; element <c>i</c> of every array belongs to <c>lines[i]</c>.</param>
    /// <returns>36 parameters: the 35 <c>l_*</c> arrays in t_line_input order, then <c>line_count</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lines"/> is null.</exception>
    /// <exception cref="ArgumentException">A line is null, or a text value is longer than its t_line_input field width in characters or UTF-8 bytes (4000 where the width is unstated).</exception>
    public static IReadOnlyList<OracleParameter> Bind(IReadOnlyList<InvoiceLineDraft> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i] is null)
            {
                throw new ArgumentException($"Invoice line at index {i} is null.", nameof(lines));
            }
        }

        var count = lines.Count;
        var size = Math.Max(count, 1);

        var parameters = new List<OracleParameter>(36);
        try
        {
            parameters.Add(TextArray("l_serviceid", "SERVICEID", lines, size, ServiceIdLength, line => line.ServiceId));
            parameters.Add(NumberArray("l_qty", lines, size, line => line.Qty));
            parameters.Add(NumberArray("l_price_override", lines, size, line => AllowsPriceOverride(line) ? line.PriceOverride : null));
            parameters.Add(TextArray("l_use_price_override", "USE_PRICE_OVERRIDE", lines, size, UsePriceOverrideLength, line => AllowsPriceOverride(line) ? "Y" : "N"));
            parameters.Add(TextArray("l_discount_type", "LDISCT", lines, size, DiscountTypeLength, line => line.DiscountType));
            parameters.Add(NumberArray("l_disc", lines, size, line => line.Disc));
            parameters.Add(NumberArray("l_my_disc", lines, size, line => line.MyDisc));
            parameters.Add(TextArray("l_teeth_no", "TEETH_NO", lines, size, TeethNoLength, line => line.TeethNo));
            parameters.Add(TextArray("l_tooth_surface", "TOOTH_SURFACE", lines, size, ToothSurfaceLength, line => line.ToothSurface));
            parameters.Add(TextArray("l_teeth_no2", "TEETH_NO2", lines, size, TeethNo2Length, line => line.TeethNo2));
            parameters.Add(NumberArray("l_pat_serv_req_row_id", lines, size, line => line.PatServReqRowId));
            parameters.Add(DateArray("l_approv_date", lines, size, line => line.ApprovDate));
            parameters.Add(NumberArray("l_approv_validity", lines, size, line => line.ApprovValidity));
            parameters.Add(TextArray("l_approv_ref_no", "APPROV_REF_NO", lines, size, ApprovRefNoLength, line => line.ApprovRefNo));
            parameters.Add(TextArray("l_claim_no", "CLAIM_NO", lines, size, MaxVarchar2Length, line => line.ClaimNo));
            parameters.Add(NumberArray("l_req_need_a", lines, size, line => line.ReqNeedA));
            parameters.Add(NumberArray("l_req_a_status", lines, size, line => line.ReqAStatus));
            parameters.Add(TextArray("l_package_service_id", "IMP_FROM_PKG", lines, size, MaxVarchar2Length, line => line.PackageServiceId));
            parameters.Add(TextArray("l_package_instance_id", "PACKAGE_INSTANCE_ID", lines, size, MaxVarchar2Length, line => line.PackageInstanceId));
            parameters.Add(TextArray("l_package_line_role", "PACKAGE_LINE_ROLE", lines, size, MaxVarchar2Length, line => line.PackageLineRole));
            parameters.Add(NumberArray("l_package_component_order", lines, size, line => line.PackageComponentOrder));
            parameters.Add(NumberArray("l_package_parent_line_id", lines, size, line => line.PackageParentLineId));
            parameters.Add(TextArray("l_package_pricing_method", "PACKAGE_PRICING_METHOD", lines, size, MaxVarchar2Length, line => line.PackagePricingMethod));
            parameters.Add(TextArray("l_package_definition_token", "PACKAGE_DEFINITION_TOKEN", lines, size, PackageDefinitionTokenLength, line => line.PackageDefinitionToken));
            parameters.Add(NumberArray("l_offer_id", lines, size, line => line.OfferId));
            parameters.Add(NumberArray("l_offer_dtl_id", lines, size, line => line.OfferDtlId));
            parameters.Add(NumberArray("l_offer_type", lines, size, line => line.OfferType));
            parameters.Add(TextArray("l_offer_instance_id", "OFFER_INSTANCE_ID", lines, size, MaxVarchar2Length, line => line.OfferInstanceId));
            parameters.Add(TextArray("l_offer_line_role", "OFFER_LINE_ROLE", lines, size, MaxVarchar2Length, line => line.OfferLineRole));
            parameters.Add(NumberArray("l_offer_parent_line_id", lines, size, line => line.OfferParentLineId));
            parameters.Add(NumberArray("l_offer_price_applied", lines, size, line => line.OfferPriceApplied));
            parameters.Add(NumberArray("l_offer_dis_applied", lines, size, line => line.OfferDisApplied));
            parameters.Add(TextArray("l_offer_name_snapshot", "OFFER_NAME_SNAPSHOT", lines, size, MaxVarchar2Length, line => line.OfferNameSnapshot));
            parameters.Add(NumberArray("l_offer_object_version_number", lines, size, line => line.OfferObjectVersionNumber));
            parameters.Add(NumberArray("l_offer_dtl_object_version_number", lines, size, line => line.OfferDtlObjectVersionNumber));
            parameters.Add(new OracleParameter(LineCountParameterName, OracleDbType.Decimal)
            {
                Direction = ParameterDirection.Input,
                Value = count,
            });
        }
        catch
        {
            // A rejected value disposes the arrays built before it.
            foreach (var parameter in parameters)
            {
                parameter.Dispose();
            }

            throw;
        }

        return parameters.AsReadOnly();
    }

    /// <summary>True when the line's price override is bound: it has one and is neither a package component nor an offer line.</summary>
    private static bool AllowsPriceOverride(InvoiceLineDraft line)
    {
        if (line.PriceOverride is null)
        {
            return false;
        }

        var isComponent = string.Equals(line.PackageLineRole?.Trim(), ComponentLineRole, StringComparison.OrdinalIgnoreCase);
        var isOffer = line.OfferId is not null || !string.IsNullOrWhiteSpace(line.OfferLineRole);
        return !isComponent && !isOffer;
    }

    /// <summary>Builds a NUMBER associative array; null values are bound as null elements.</summary>
    private static OracleParameter NumberArray(
        string name,
        IReadOnlyList<InvoiceLineDraft> lines,
        int size,
        Func<InvoiceLineDraft, decimal?> select)
    {
        var values = new OracleDecimal[size];
        var status = NullStatuses(size);
        Array.Fill(values, OracleDecimal.Null);

        for (var i = 0; i < lines.Count; i++)
        {
            if (select(lines[i]) is { } value)
            {
                values[i] = new OracleDecimal(value);
                status[i] = OracleParameterStatus.Success;
            }
        }

        return ArrayParameter(name, OracleDbType.Decimal, size, values, status);
    }

    /// <summary>Builds a VARCHAR2 associative array with per-element bind sizes, rejecting a value over the field's destination width <paramref name="maxLength"/> on <paramref name="item"/>; null values are bound as null elements.</summary>
    private static OracleParameter TextArray(
        string name,
        string item,
        IReadOnlyList<InvoiceLineDraft> lines,
        int size,
        int maxLength,
        Func<InvoiceLineDraft, string?> select)
    {
        var values = new OracleString[size];
        var status = NullStatuses(size);
        var bindSizes = new int[size];
        Array.Fill(values, OracleString.Null);
        Array.Fill(bindSizes, 1);

        for (var i = 0; i < lines.Count; i++)
        {
            var value = select(lines[i]);
            if (value is null)
            {
                continue;
            }

            // A value over its field's destination width in characters or UTF-8 bytes is rejected on its item and 1-based line, never truncated to fit (D-109).
            if (BoundedVarchar2.Rejection($"{item} on line {i + 1}", item, value, maxLength, nameof(lines)) is { } rejection)
            {
                throw rejection;
            }

            values[i] = new OracleString(value);
            status[i] = OracleParameterStatus.Success;
            bindSizes[i] = Math.Max(value.Length, 1);
        }

        var parameter = ArrayParameter(name, OracleDbType.Varchar2, size, values, status);
        parameter.ArrayBindSize = bindSizes;
        return parameter;
    }

    /// <summary>Builds a DATE associative array; null values are bound as null elements.</summary>
    private static OracleParameter DateArray(
        string name,
        IReadOnlyList<InvoiceLineDraft> lines,
        int size,
        Func<InvoiceLineDraft, DateTime?> select)
    {
        var values = new OracleDate[size];
        var status = NullStatuses(size);
        Array.Fill(values, OracleDate.Null);

        for (var i = 0; i < lines.Count; i++)
        {
            if (select(lines[i]) is { } value)
            {
                values[i] = new OracleDate(value);
                status[i] = OracleParameterStatus.Success;
            }
        }

        return ArrayParameter(name, OracleDbType.Date, size, values, status);
    }

    /// <summary>Creates a status array with every element marked as a null insert.</summary>
    private static OracleParameterStatus[] NullStatuses(int size)
    {
        var status = new OracleParameterStatus[size];
        Array.Fill(status, OracleParameterStatus.NullInsert);
        return status;
    }

    /// <summary>Creates an IN PL/SQL associative-array parameter holding the given element values and statuses.</summary>
    private static OracleParameter ArrayParameter(
        string name,
        OracleDbType dbType,
        int size,
        Array values,
        OracleParameterStatus[] status) =>
        new(name, dbType)
        {
            Direction = ParameterDirection.Input,
            CollectionType = OracleCollectionType.PLSQLAssociativeArray,
            Size = size,
            Value = values,
            ArrayBindStatus = status,
        };
}
