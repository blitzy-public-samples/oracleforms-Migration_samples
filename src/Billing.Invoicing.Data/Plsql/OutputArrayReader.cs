using System.Data;
using System.Globalization;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

namespace Billing.Invoicing.Data.Plsql;

/// <summary>Converts the OUT scalars and OUT scalar associative arrays of the <see cref="PlsqlBlocks"/> blocks into the package DTOs; UNVERIFIED against Oracle.</summary>
public static class OutputArrayReader
{
    private const int MaxVarcharSize = 4000;
    private const int DecimalPrecision = 28;
    private const string PreviewLineCountName = "pl_count";
    private const string EngineLineCountName = "el_count";

    private static readonly OutputField[] PreviewLineFields =
    [
        TextField("pl_client_id", 4000),
        NumberField("pl_line_no"),
        TextField("pl_serviceid"),
        TextField("pl_servicedesc"),
        NumberField("pl_catid"),
        NumberField("pl_list_id"),
        TextField("pl_curr_code"),
        NumberField("pl_qty"),
        NumberField("pl_price"),
        NumberField("pl_plan_discount_pct"),
        NumberField("pl_plan_discount_amount"),
        TextField("pl_manual_discount_type", 1),
        NumberField("pl_manual_discount_pct"),
        NumberField("pl_manual_discount_amount"),
        TextField("pl_discount_source", 20),
        NumberField("pl_disc"),
        NumberField("pl_my_disc"),
        NumberField("pl_my_price"),
        NumberField("pl_my_net"),
        NumberField("pl_the_pay"),
        NumberField("pl_the_comp"),
        NumberField("pl_vat_rate"),
        NumberField("pl_vat_val_pat"),
        NumberField("pl_vat_val_co"),
        NumberField("pl_vat_val_pat_ex"),
        NumberField("pl_req_need_a"),
        NumberField("pl_req_a_status"),
        TextField("pl_allow_manual_discount", 1),
        TextField("pl_allow_price_override", 1),
        TextField("pl_package_service_id"),
        TextField("pl_package_instance_id"),
        TextField("pl_package_line_role"),
        NumberField("pl_package_component_order"),
        NumberField("pl_package_parent_line_id"),
        TextField("pl_package_pricing_method"),
        TextField("pl_package_definition_token", 64),
        NumberField("pl_offer_id"),
        NumberField("pl_offer_dtl_id"),
        NumberField("pl_offer_type"),
        TextField("pl_offer_instance_id"),
        TextField("pl_offer_line_role"),
        NumberField("pl_offer_parent_line_id"),
        NumberField("pl_offer_price_applied"),
        NumberField("pl_offer_dis_applied"),
        TextField("pl_offer_name_snapshot"),
        NumberField("pl_offer_object_version_number"),
        NumberField("pl_offer_dtl_object_version_number"),
    ];

    private static readonly OutputField[] PreviewTotalsFields =
    [
        NumberField("pt_line_count"),
        NumberField("pt_total_gross"),
        NumberField("pt_total_discount"),
        NumberField("pt_total_net"),
        NumberField("pt_pat_pay"),
        NumberField("pt_comp_pay"),
        NumberField("pt_vat_total_pat"),
        NumberField("pt_vat_total_co"),
        NumberField("pt_cash_collected"),
        NumberField("pt_amount_1"),
        NumberField("pt_amount_2"),
        NumberField("pt_remaining_amount"),
        TextField("pt_payment_status", 30),
    ];

    private static readonly OutputField[] EngineLineFields =
    [
        TextField("el_serviceid"),
        NumberField("el_qty"),
        NumberField("el_price_override"),
        TextField("el_use_price_override", 1),
        TextField("el_discount_type", 1),
        NumberField("el_disc"),
        NumberField("el_my_disc"),
        TextField("el_teeth_no"),
        TextField("el_tooth_surface"),
        TextField("el_teeth_no2"),
        NumberField("el_pat_serv_req_row_id"),
        DateField("el_approv_date"),
        NumberField("el_approv_validity"),
        TextField("el_approv_ref_no"),
        TextField("el_claim_no"),
        NumberField("el_req_need_a"),
        NumberField("el_req_a_status"),
        TextField("el_package_service_id"),
        TextField("el_package_instance_id"),
        TextField("el_package_line_role"),
        NumberField("el_package_component_order"),
        NumberField("el_package_parent_line_id"),
        TextField("el_package_pricing_method"),
        TextField("el_package_definition_token", 64),
        NumberField("el_offer_id"),
        NumberField("el_offer_dtl_id"),
        NumberField("el_offer_type"),
        TextField("el_offer_instance_id"),
        TextField("el_offer_line_role"),
        NumberField("el_offer_parent_line_id"),
        NumberField("el_offer_price_applied"),
        NumberField("el_offer_dis_applied"),
        TextField("el_offer_name_snapshot"),
        NumberField("el_offer_object_version_number"),
        NumberField("el_offer_dtl_object_version_number"),
    ];

    private static readonly OutputField[] ImportResultFields =
    [
        TextField("ir_source_type", 30),
        NumberField("ir_source_count"),
        NumberField("ir_imported_count"),
        NumberField("ir_skipped_rejected_count"),
        NumberField("ir_skipped_need_approval_count"),
        NumberField("ir_skipped_invalid_count"),
        TextField("ir_has_price_overrides"),
        TextField("ir_message"),
    ];

    private static readonly OutputField[] FullInvoiceResultFields =
    [
        NumberField("fr_inv_no"),
        DateField("fr_invdate"),
        TextField("fr_patientno"),
        TextField("fr_curr_code"),
        NumberField("fr_line_count"),
        NumberField("fr_total_gross"),
        NumberField("fr_total_discount"),
        NumberField("fr_total_net"),
        NumberField("fr_pat_pay"),
        NumberField("fr_comp_pay"),
        NumberField("fr_vat_total_pat"),
        NumberField("fr_vat_total_co"),
        NumberField("fr_vat_total"),
        NumberField("fr_finaldisc"),
        NumberField("fr_cash_collected"),
        NumberField("fr_shift_system_unique"),
        TextField("fr_payment_posted"),
        TextField("fr_queue_posted"),
        TextField("fr_stock_posted"),
        TextField("fr_print_url_built"),
        TextField("fr_sms_sent"),
        TextField("fr_message"),
        TextField("fr_message_send_status"),
        TextField("fr_message_text"),
    ];

    /// <summary>Reads the <c>pl_*</c> OUT arrays into editable preview lines.</summary>
    /// <param name="parameters">The executed command's parameters.</param>
    /// <returns>The first <c>min(pl_count, array length)</c> lines.</returns>
    /// <exception cref="InvalidOperationException">An expected OUT parameter is absent.</exception>
    public static IReadOnlyList<EditablePreviewLine> ReadPreviewLines(OracleParameterCollection parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var outputs = new ArrayOutputs(parameters, PreviewLineCountName, PreviewLineFields);
        var lines = new List<EditablePreviewLine>(outputs.RowCount);
        for (int row = 0; row < outputs.RowCount; row++)
        {
            lines.Add(new EditablePreviewLine
            {
                ClientId = outputs.Text("pl_client_id", row),
                LineNo = outputs.Int32("pl_line_no", row),
                ServiceId = outputs.Text("pl_serviceid", row),
                ServiceDesc = outputs.Text("pl_servicedesc", row),
                CatId = outputs.Int32("pl_catid", row),
                ListId = outputs.Decimal("pl_list_id", row),
                CurrCode = outputs.Text("pl_curr_code", row),
                Qty = outputs.Decimal("pl_qty", row),
                Price = outputs.Decimal("pl_price", row),
                PlanDiscountPct = outputs.Decimal("pl_plan_discount_pct", row),
                PlanDiscountAmount = outputs.Decimal("pl_plan_discount_amount", row),
                ManualDiscountType = outputs.Text("pl_manual_discount_type", row),
                ManualDiscountPct = outputs.Decimal("pl_manual_discount_pct", row),
                ManualDiscountAmount = outputs.Decimal("pl_manual_discount_amount", row),
                DiscountSource = outputs.Text("pl_discount_source", row),
                Disc = outputs.Decimal("pl_disc", row),
                MyDisc = outputs.Decimal("pl_my_disc", row),
                MyPrice = outputs.Decimal("pl_my_price", row),
                MyNet = outputs.Decimal("pl_my_net", row),
                ThePay = outputs.Decimal("pl_the_pay", row),
                TheComp = outputs.Decimal("pl_the_comp", row),
                VatRate = outputs.Decimal("pl_vat_rate", row),
                VatValPat = outputs.Decimal("pl_vat_val_pat", row),
                VatValCo = outputs.Decimal("pl_vat_val_co", row),
                VatValPatEx = outputs.Decimal("pl_vat_val_pat_ex", row),
                ReqNeedA = outputs.Int32("pl_req_need_a", row),
                ReqAStatus = outputs.Int32("pl_req_a_status", row),
                AllowManualDiscount = outputs.Text("pl_allow_manual_discount", row),
                AllowPriceOverride = outputs.Text("pl_allow_price_override", row),
                PackageServiceId = outputs.Text("pl_package_service_id", row),
                PackageInstanceId = outputs.Text("pl_package_instance_id", row),
                PackageLineRole = outputs.Text("pl_package_line_role", row),
                PackageComponentOrder = outputs.Int32("pl_package_component_order", row),
                PackageParentLineId = outputs.Int64("pl_package_parent_line_id", row),
                PackagePricingMethod = outputs.Text("pl_package_pricing_method", row),
                PackageDefinitionToken = outputs.Text("pl_package_definition_token", row),
                OfferId = outputs.Int32("pl_offer_id", row),
                OfferDtlId = outputs.Int64("pl_offer_dtl_id", row),
                OfferType = outputs.Int32("pl_offer_type", row),
                OfferInstanceId = outputs.Text("pl_offer_instance_id", row),
                OfferLineRole = outputs.Text("pl_offer_line_role", row),
                OfferParentLineId = outputs.Int64("pl_offer_parent_line_id", row),
                OfferPriceApplied = outputs.Decimal("pl_offer_price_applied", row),
                OfferDisApplied = outputs.Decimal("pl_offer_dis_applied", row),
                OfferNameSnapshot = outputs.Text("pl_offer_name_snapshot", row),
                OfferObjectVersionNumber = outputs.Int64("pl_offer_object_version_number", row),
                OfferDtlObjectVersionNumber = outputs.Int64("pl_offer_dtl_object_version_number", row),
            });
        }

        return lines;
    }

    /// <summary>Reads the <c>pt_*</c> OUT scalars into the preview totals.</summary>
    /// <param name="parameters">The executed command's parameters.</param>
    /// <returns>The preview totals as returned by the package.</returns>
    /// <exception cref="InvalidOperationException">An expected OUT parameter is absent.</exception>
    public static PreviewTotalsRow ReadPreviewTotals(OracleParameterCollection parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return new PreviewTotalsRow
        {
            LineCount = ScalarInt32(parameters, "pt_line_count"),
            TotalGross = ScalarDecimal(parameters, "pt_total_gross"),
            TotalDiscount = ScalarDecimal(parameters, "pt_total_discount"),
            TotalNet = ScalarDecimal(parameters, "pt_total_net"),
            PatPay = ScalarDecimal(parameters, "pt_pat_pay"),
            CompPay = ScalarDecimal(parameters, "pt_comp_pay"),
            VatTotalPat = ScalarDecimal(parameters, "pt_vat_total_pat"),
            VatTotalCo = ScalarDecimal(parameters, "pt_vat_total_co"),
            CashCollected = ScalarDecimal(parameters, "pt_cash_collected"),
            Amount1 = ScalarDecimal(parameters, "pt_amount_1"),
            Amount2 = ScalarDecimal(parameters, "pt_amount_2"),
            RemainingAmount = ScalarDecimal(parameters, "pt_remaining_amount"),
            PaymentStatus = ScalarText(parameters, "pt_payment_status"),
        };
    }

    /// <summary>Reads the <c>el_*</c> OUT arrays into engine lines.</summary>
    /// <param name="parameters">The executed command's parameters.</param>
    /// <returns>The first <c>min(el_count, array length)</c> lines.</returns>
    /// <exception cref="InvalidOperationException">An expected OUT parameter is absent.</exception>
    public static IReadOnlyList<EngineLineInput> ReadEngineLines(OracleParameterCollection parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var outputs = new ArrayOutputs(parameters, EngineLineCountName, EngineLineFields);
        var lines = new List<EngineLineInput>(outputs.RowCount);
        for (int row = 0; row < outputs.RowCount; row++)
        {
            lines.Add(new EngineLineInput
            {
                ServiceId = outputs.Text("el_serviceid", row),
                Qty = outputs.Decimal("el_qty", row),
                PriceOverride = outputs.Decimal("el_price_override", row),
                UsePriceOverride = outputs.Text("el_use_price_override", row),
                DiscountType = outputs.Text("el_discount_type", row),
                Disc = outputs.Decimal("el_disc", row),
                MyDisc = outputs.Decimal("el_my_disc", row),
                TeethNo = outputs.Text("el_teeth_no", row),
                ToothSurface = outputs.Text("el_tooth_surface", row),
                TeethNo2 = outputs.Text("el_teeth_no2", row),
                PatServReqRowId = outputs.Int64("el_pat_serv_req_row_id", row),
                ApprovDate = outputs.Date("el_approv_date", row),
                ApprovValidity = outputs.Decimal("el_approv_validity", row),
                ApprovRefNo = outputs.Text("el_approv_ref_no", row),
                ClaimNo = outputs.Text("el_claim_no", row),
                ReqNeedA = outputs.Int32("el_req_need_a", row),
                ReqAStatus = outputs.Int32("el_req_a_status", row),
                PackageServiceId = outputs.Text("el_package_service_id", row),
                PackageInstanceId = outputs.Text("el_package_instance_id", row),
                PackageLineRole = outputs.Text("el_package_line_role", row),
                PackageComponentOrder = outputs.Int32("el_package_component_order", row),
                PackageParentLineId = outputs.Int64("el_package_parent_line_id", row),
                PackagePricingMethod = outputs.Text("el_package_pricing_method", row),
                PackageDefinitionToken = outputs.Text("el_package_definition_token", row),
                OfferId = outputs.Int32("el_offer_id", row),
                OfferDtlId = outputs.Int64("el_offer_dtl_id", row),
                OfferType = outputs.Int32("el_offer_type", row),
                OfferInstanceId = outputs.Text("el_offer_instance_id", row),
                OfferLineRole = outputs.Text("el_offer_line_role", row),
                OfferParentLineId = outputs.Int64("el_offer_parent_line_id", row),
                OfferPriceApplied = outputs.Decimal("el_offer_price_applied", row),
                OfferDisApplied = outputs.Decimal("el_offer_dis_applied", row),
                OfferNameSnapshot = outputs.Text("el_offer_name_snapshot", row),
                OfferObjectVersionNumber = outputs.Int64("el_offer_object_version_number", row),
                OfferDtlObjectVersionNumber = outputs.Int64("el_offer_dtl_object_version_number", row),
            });
        }

        return lines;
    }

    /// <summary>Reads the <c>ir_*</c> OUT scalars into the import result.</summary>
    /// <param name="parameters">The executed command's parameters.</param>
    /// <returns>The import counts and message as returned by the package.</returns>
    /// <exception cref="InvalidOperationException">An expected OUT parameter is absent.</exception>
    public static ImportResultRow ReadImportResult(OracleParameterCollection parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return new ImportResultRow
        {
            SourceType = ScalarText(parameters, "ir_source_type"),
            SourceCount = ScalarInt32(parameters, "ir_source_count"),
            ImportedCount = ScalarInt32(parameters, "ir_imported_count"),
            SkippedRejectedCount = ScalarInt32(parameters, "ir_skipped_rejected_count"),
            SkippedNeedApprovalCount = ScalarInt32(parameters, "ir_skipped_need_approval_count"),
            SkippedInvalidCount = ScalarInt32(parameters, "ir_skipped_invalid_count"),
            HasPriceOverrides = ScalarText(parameters, "ir_has_price_overrides"),
            Message = ScalarText(parameters, "ir_message"),
        };
    }

    /// <summary>Reads the <c>fr_*</c> OUT scalars into the full-invoice result.</summary>
    /// <param name="parameters">The executed command's parameters.</param>
    /// <returns>The invoice result, posting flags and message as returned by the package.</returns>
    /// <exception cref="InvalidOperationException">An expected OUT parameter is absent.</exception>
    public static FullInvoiceResultRow ReadFullInvoiceResult(OracleParameterCollection parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return new FullInvoiceResultRow
        {
            InvNo = ScalarInt64(parameters, "fr_inv_no"),
            InvDate = ScalarDate(parameters, "fr_invdate"),
            PatientNo = ScalarText(parameters, "fr_patientno"),
            CurrCode = ScalarText(parameters, "fr_curr_code"),
            LineCount = ScalarInt32(parameters, "fr_line_count"),
            TotalGross = ScalarDecimal(parameters, "fr_total_gross"),
            TotalDiscount = ScalarDecimal(parameters, "fr_total_discount"),
            TotalNet = ScalarDecimal(parameters, "fr_total_net"),
            PatPay = ScalarDecimal(parameters, "fr_pat_pay"),
            CompPay = ScalarDecimal(parameters, "fr_comp_pay"),
            VatTotalPat = ScalarDecimal(parameters, "fr_vat_total_pat"),
            VatTotalCo = ScalarDecimal(parameters, "fr_vat_total_co"),
            VatTotal = ScalarDecimal(parameters, "fr_vat_total"),
            FinalDisc = ScalarDecimal(parameters, "fr_finaldisc"),
            CashCollected = ScalarDecimal(parameters, "fr_cash_collected"),
            ShiftSystemUnique = ScalarDecimal(parameters, "fr_shift_system_unique"),
            PaymentPosted = ScalarText(parameters, "fr_payment_posted"),
            QueuePosted = ScalarText(parameters, "fr_queue_posted"),
            StockPosted = ScalarText(parameters, "fr_stock_posted"),
            PrintUrlBuilt = ScalarText(parameters, "fr_print_url_built"),
            SmsSent = ScalarText(parameters, "fr_sms_sent"),
            Message = ScalarText(parameters, "fr_message"),
            MessageSendStatus = ScalarText(parameters, "fr_message_send_status"),
            MessageText = ScalarText(parameters, "fr_message_text"),
        };
    }

    /// <summary>Adds <c>pl_count</c> and the 47 <c>pl_*</c> OUT associative arrays.</summary>
    /// <param name="parameters">The command's parameter collection.</param>
    /// <param name="capacity">Element capacity of each array, from <c>InvoicingDataOptions.MaxOutputLines</c>.</param>
    internal static void AddPreviewLineOutputs(OracleParameterCollection parameters, int capacity) =>
        AddArrayOutputs(parameters, PreviewLineCountName, PreviewLineFields, capacity);

    /// <summary>Adds the 13 <c>pt_*</c> OUT scalars.</summary>
    /// <param name="parameters">The command's parameter collection.</param>
    internal static void AddPreviewTotalsOutputs(OracleParameterCollection parameters) =>
        AddScalarOutputs(parameters, PreviewTotalsFields);

    /// <summary>Adds <c>el_count</c> and the 35 <c>el_*</c> OUT associative arrays.</summary>
    /// <param name="parameters">The command's parameter collection.</param>
    /// <param name="capacity">Element capacity of each array, from <c>InvoicingDataOptions.MaxOutputLines</c>.</param>
    internal static void AddEngineLineOutputs(OracleParameterCollection parameters, int capacity) =>
        AddArrayOutputs(parameters, EngineLineCountName, EngineLineFields, capacity);

    /// <summary>Adds the 8 <c>ir_*</c> OUT scalars.</summary>
    /// <param name="parameters">The command's parameter collection.</param>
    internal static void AddImportResultOutputs(OracleParameterCollection parameters) =>
        AddScalarOutputs(parameters, ImportResultFields);

    /// <summary>Adds the 24 <c>fr_*</c> OUT scalars.</summary>
    /// <param name="parameters">The command's parameter collection.</param>
    internal static void AddFullInvoiceResultOutputs(OracleParameterCollection parameters) =>
        AddScalarOutputs(parameters, FullInvoiceResultFields);

    private static void AddArrayOutputs(OracleParameterCollection parameters, string countName, OutputField[] fields, int capacity)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        EnsureAbsent(parameters, countName);
        foreach (OutputField field in fields)
        {
            EnsureAbsent(parameters, field.Name);
        }

        parameters.Add(new OracleParameter(countName, OracleDbType.Decimal) { Direction = ParameterDirection.Output });
        foreach (OutputField field in fields)
        {
            var parameter = new OracleParameter(field.Name, field.Type)
            {
                Direction = ParameterDirection.Output,
                CollectionType = OracleCollectionType.PLSQLAssociativeArray,
                Size = capacity,
            };
            if (field.Type == OracleDbType.Varchar2)
            {
                parameter.ArrayBindSize = Enumerable.Repeat(field.Size, capacity).ToArray();
            }

            parameters.Add(parameter);
        }
    }

    private static void AddScalarOutputs(OracleParameterCollection parameters, OutputField[] fields)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        foreach (OutputField field in fields)
        {
            EnsureAbsent(parameters, field.Name);
        }

        foreach (OutputField field in fields)
        {
            var parameter = new OracleParameter(field.Name, field.Type) { Direction = ParameterDirection.Output };
            if (field.Type == OracleDbType.Varchar2)
            {
                parameter.Size = field.Size;
            }

            parameters.Add(parameter);
        }
    }

    private static void EnsureAbsent(OracleParameterCollection parameters, string name)
    {
        if (IndexOf(parameters, name) >= 0)
        {
            throw new InvalidOperationException($"OUT parameter '{name}' is already in the parameter collection.");
        }
    }

    /// <summary>Finds a parameter by name, with or without a leading <c>:</c>.</summary>
    private static int IndexOf(OracleParameterCollection parameters, string name)
    {
        int index = parameters.IndexOf(name);
        return index >= 0 ? index : parameters.IndexOf(":" + name);
    }

    private static object? ParameterValue(OracleParameterCollection parameters, string name)
    {
        int index = IndexOf(parameters, name);
        if (index < 0)
        {
            throw new InvalidOperationException($"OUT parameter '{name}' is not in the parameter collection.");
        }

        return parameters[index].Value;
    }

    private static Array? ArrayValue(OracleParameterCollection parameters, string name)
    {
        object? value = ParameterValue(parameters, name);
        return value switch
        {
            null or DBNull => null,
            Array array => array,
            _ => throw new InvalidCastException($"OUT parameter '{name}' holds a {value.GetType().Name}, not an array."),
        };
    }

    private static decimal? ScalarDecimal(OracleParameterCollection parameters, string name) =>
        ToDecimal(ParameterValue(parameters, name), name);

    private static int? ScalarInt32(OracleParameterCollection parameters, string name) =>
        ToInt32(ParameterValue(parameters, name), name);

    private static long? ScalarInt64(OracleParameterCollection parameters, string name) =>
        ToInt64(ParameterValue(parameters, name), name);

    private static string? ScalarText(OracleParameterCollection parameters, string name) =>
        ToText(ParameterValue(parameters, name), name);

    private static DateTime? ScalarDate(OracleParameterCollection parameters, string name) =>
        ToDate(ParameterValue(parameters, name), name);

    private static decimal? ToDecimal(object? value, string name)
    {
        switch (value)
        {
            case null:
            case DBNull:
                return null;
            case OracleDecimal oracleDecimal:
                return oracleDecimal.IsNull ? null : FromOracleDecimal(oracleDecimal, name);
            case INullable nullable when nullable.IsNull:
                return null;
            case decimal number:
                return number;
            case int number:
                return number;
            case long number:
                return number;
            case short number:
                return number;
            case byte number:
                return number;
            default:
                throw new InvalidCastException($"OUT parameter '{name}' holds a {value.GetType().Name}, not a number.");
        }
    }

    /// <summary>Rounds to 28 significant digits; a value still outside the decimal range throws <see cref="OverflowException"/>.</summary>
    private static decimal FromOracleDecimal(OracleDecimal value, string name)
    {
        try
        {
            return OracleDecimal.SetPrecision(value, DecimalPrecision).Value;
        }
        catch (Exception ex) when (ex is InvalidCastException or OverflowException)
        {
            throw new OverflowException($"OUT parameter '{name}' holds {value}, which exceeds the decimal range.", ex);
        }
    }

    private static int? ToInt32(object? value, string name)
    {
        decimal? number = ToWholeNumber(value, name);
        if (number is not { } whole)
        {
            return null;
        }

        if (whole < int.MinValue || whole > int.MaxValue)
        {
            throw new OverflowException($"OUT parameter '{name}' holds {Format(whole)}, which exceeds the Int32 range.");
        }

        return (int)whole;
    }

    private static long? ToInt64(object? value, string name)
    {
        decimal? number = ToWholeNumber(value, name);
        if (number is not { } whole)
        {
            return null;
        }

        if (whole < long.MinValue || whole > long.MaxValue)
        {
            throw new OverflowException($"OUT parameter '{name}' holds {Format(whole)}, which exceeds the Int64 range.");
        }

        return (long)whole;
    }

    /// <summary>Converts to a whole number; a fractional value throws <see cref="InvalidCastException"/> instead of being truncated.</summary>
    private static decimal? ToWholeNumber(object? value, string name)
    {
        decimal? number = ToDecimal(value, name);
        if (number is { } whole && decimal.Truncate(whole) != whole)
        {
            throw new InvalidCastException($"OUT parameter '{name}' holds {Format(whole)}, which is not a whole number.");
        }

        return number;
    }

    private static string? ToText(object? value, string name)
    {
        switch (value)
        {
            case null:
            case DBNull:
                return null;
            case OracleString oracleString:
                return oracleString.IsNull ? null : oracleString.Value;
            case string text:
                return text;
            case INullable nullable when nullable.IsNull:
                return null;
            default:
                throw new InvalidCastException($"OUT parameter '{name}' holds a {value.GetType().Name}, not text.");
        }
    }

    private static DateTime? ToDate(object? value, string name)
    {
        switch (value)
        {
            case null:
            case DBNull:
                return null;
            case OracleDate oracleDate:
                return oracleDate.IsNull ? null : oracleDate.Value;
            case OracleTimeStamp timeStamp:
                return timeStamp.IsNull ? null : timeStamp.Value;
            case OracleTimeStampLTZ localTimeStamp:
                return localTimeStamp.IsNull ? null : localTimeStamp.Value;
            case OracleTimeStampTZ zonedTimeStamp:
                return zonedTimeStamp.IsNull ? null : zonedTimeStamp.Value;
            case DateTime dateTime:
                return dateTime;
            case INullable nullable when nullable.IsNull:
                return null;
            default:
                throw new InvalidCastException($"OUT parameter '{name}' holds a {value.GetType().Name}, not a date.");
        }
    }

    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static OutputField NumberField(string name) => new(name, OracleDbType.Decimal, 0);

    private static OutputField TextField(string name, int size = MaxVarcharSize) => new(name, OracleDbType.Varchar2, size);

    private static OutputField DateField(string name) => new(name, OracleDbType.Date, 0);

    /// <summary>One OUT bind: name, Oracle type and, for text, the element or buffer size.</summary>
    private readonly record struct OutputField(string Name, OracleDbType Type, int Size);

    /// <summary>The OUT arrays of one output table, bounded by its OUT count.</summary>
    private sealed class ArrayOutputs
    {
        private readonly Dictionary<string, Array?> arrays = new(StringComparer.Ordinal);

        /// <summary>Resolves the count and every field array; the row count is the smaller of the count and the shortest array, and a null array counts as empty.</summary>
        /// <param name="parameters">The executed command's parameters.</param>
        /// <param name="countName">Name of the OUT count scalar.</param>
        /// <param name="fields">The table's OUT array fields.</param>
        public ArrayOutputs(OracleParameterCollection parameters, string countName, OutputField[] fields)
        {
            int rows = Math.Max(0, ScalarInt32(parameters, countName) ?? 0);
            foreach (OutputField field in fields)
            {
                Array? values = ArrayValue(parameters, field.Name);
                arrays[field.Name] = values;
                rows = Math.Min(rows, values?.Length ?? 0);
            }

            RowCount = rows;
        }

        /// <summary>Number of rows to read.</summary>
        public int RowCount { get; }

        public decimal? Decimal(string name, int row) => ToDecimal(Element(name, row), name);

        public int? Int32(string name, int row) => ToInt32(Element(name, row), name);

        public long? Int64(string name, int row) => ToInt64(Element(name, row), name);

        public string? Text(string name, int row) => ToText(Element(name, row), name);

        public DateTime? Date(string name, int row) => ToDate(Element(name, row), name);

        private object? Element(string name, int row)
        {
            if (!arrays.TryGetValue(name, out Array? values))
            {
                throw new InvalidOperationException($"OUT array '{name}' is not a field of this output table.");
            }

            ArgumentOutOfRangeException.ThrowIfNegative(row);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, RowCount);
            return values!.GetValue(values.GetLowerBound(0) + row);
        }
    }
}
