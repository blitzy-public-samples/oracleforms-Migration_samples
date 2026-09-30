using System.Data;
using System.Text;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Domain.Model;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

namespace Billing.Invoicing.Data.Plsql;

/// <summary>Binds draft lines as the 35 scalar associative arrays of BIL_INVOICE_ENGINE.t_line_input plus line_count. UNVERIFIED against Oracle.</summary>
public static class LineInputBinder
{
    private const string LineCountParameterName = "line_count";
    private const int MaxVarchar2Length = 4000;
    private const string ComponentLineRole = "COMPONENT";

    /// <summary>Builds the IN associative-array parameters for <c>l_serviceid</c> … <c>l_offer_dtl_object_version_number</c> and the <c>line_count</c> scalar.</summary>
    /// <param name="lines">Draft lines in bind order; element <c>i</c> of every array belongs to <c>lines[i]</c>.</param>
    /// <returns>36 parameters: the 35 <c>l_*</c> arrays in t_line_input order, then <c>line_count</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lines"/> is null.</exception>
    /// <exception cref="ArgumentException">A line is null, or a text value is longer than 4000 characters or 4000 UTF-8 bytes.</exception>
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

        var parameters = new List<OracleParameter>(36)
        {
            TextArray("l_serviceid", lines, size, line => line.ServiceId),
            NumberArray("l_qty", lines, size, line => line.Qty),
            NumberArray("l_price_override", lines, size, line => AllowsPriceOverride(line) ? line.PriceOverride : null),
            TextArray("l_use_price_override", lines, size, line => AllowsPriceOverride(line) ? "Y" : "N"),
            TextArray("l_discount_type", lines, size, line => line.DiscountType),
            NumberArray("l_disc", lines, size, line => line.Disc),
            NumberArray("l_my_disc", lines, size, line => line.MyDisc),
            TextArray("l_teeth_no", lines, size, line => line.TeethNo),
            TextArray("l_tooth_surface", lines, size, line => line.ToothSurface),
            TextArray("l_teeth_no2", lines, size, line => line.TeethNo2),
            NumberArray("l_pat_serv_req_row_id", lines, size, line => line.PatServReqRowId),
            DateArray("l_approv_date", lines, size, line => line.ApprovDate),
            NumberArray("l_approv_validity", lines, size, line => line.ApprovValidity),
            TextArray("l_approv_ref_no", lines, size, line => line.ApprovRefNo),
            TextArray("l_claim_no", lines, size, line => line.ClaimNo),
            NumberArray("l_req_need_a", lines, size, line => line.ReqNeedA),
            NumberArray("l_req_a_status", lines, size, line => line.ReqAStatus),
            TextArray("l_package_service_id", lines, size, line => line.PackageServiceId),
            TextArray("l_package_instance_id", lines, size, line => line.PackageInstanceId),
            TextArray("l_package_line_role", lines, size, line => line.PackageLineRole),
            NumberArray("l_package_component_order", lines, size, line => line.PackageComponentOrder),
            NumberArray("l_package_parent_line_id", lines, size, line => line.PackageParentLineId),
            TextArray("l_package_pricing_method", lines, size, line => line.PackagePricingMethod),
            TextArray("l_package_definition_token", lines, size, line => line.PackageDefinitionToken),
            NumberArray("l_offer_id", lines, size, line => line.OfferId),
            NumberArray("l_offer_dtl_id", lines, size, line => line.OfferDtlId),
            NumberArray("l_offer_type", lines, size, line => line.OfferType),
            TextArray("l_offer_instance_id", lines, size, line => line.OfferInstanceId),
            TextArray("l_offer_line_role", lines, size, line => line.OfferLineRole),
            NumberArray("l_offer_parent_line_id", lines, size, line => line.OfferParentLineId),
            NumberArray("l_offer_price_applied", lines, size, line => line.OfferPriceApplied),
            NumberArray("l_offer_dis_applied", lines, size, line => line.OfferDisApplied),
            TextArray("l_offer_name_snapshot", lines, size, line => line.OfferNameSnapshot),
            NumberArray("l_offer_object_version_number", lines, size, line => line.OfferObjectVersionNumber),
            NumberArray("l_offer_dtl_object_version_number", lines, size, line => line.OfferDtlObjectVersionNumber),
            new OracleParameter(LineCountParameterName, OracleDbType.Decimal)
            {
                Direction = ParameterDirection.Input,
                Value = count,
            },
        };

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

    /// <summary>Builds a VARCHAR2 associative array with per-element bind sizes; null values are bound as null elements.</summary>
    private static OracleParameter TextArray(
        string name,
        IReadOnlyList<InvoiceLineDraft> lines,
        int size,
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

            // A value over the t_vc element size in characters or UTF-8 bytes is rejected, never truncated to fit.
            string? rejection = null;
            if (value.Length > MaxVarchar2Length)
            {
                rejection = $"Invoice line at index {i}: {name} has {value.Length} characters; at most {MaxVarchar2Length} can be bound.";
            }
            else if (Encoding.UTF8.GetByteCount(value) is var bytes and > MaxVarchar2Length)
            {
                rejection = $"Invoice line at index {i}: {name} has {bytes} bytes in UTF-8; at most {MaxVarchar2Length} can be bound.";
            }

            if (rejection is not null)
            {
                var error = new ArgumentException(rejection, nameof(lines));
                error.Data[OracleFailureTranslator.BindingRejectionKey] = rejection;
                throw error;
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
