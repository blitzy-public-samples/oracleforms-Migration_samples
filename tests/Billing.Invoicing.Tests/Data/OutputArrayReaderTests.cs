using System.Data;
using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Plsql;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

namespace Billing.Invoicing.Tests.Data;

/// <summary>OUT scalar and OUT associative-array conversion tests for <see cref="OutputArrayReader"/>.</summary>
[Trait("Category", "DataUnit")]
public sealed class OutputArrayReaderTests
{
    private const string PreviewLineCount = "pl_count";
    private const string EngineLineCount = "el_count";

    private static readonly int Capacity = new InvoicingDataOptions().MaxOutputLines;

    private static readonly DateTime FirstApprovalDate = new(2026, 9, 29, 10, 30, 0);

    private static readonly string[] PreviewLineFields =
    [
        "pl_client_id", "pl_line_no", "pl_serviceid", "pl_servicedesc", "pl_catid", "pl_list_id",
        "pl_curr_code", "pl_qty", "pl_price", "pl_plan_discount_pct", "pl_plan_discount_amount",
        "pl_manual_discount_type", "pl_manual_discount_pct", "pl_manual_discount_amount", "pl_discount_source",
        "pl_disc", "pl_my_disc", "pl_my_price", "pl_my_net", "pl_the_pay", "pl_the_comp", "pl_vat_rate",
        "pl_vat_val_pat", "pl_vat_val_co", "pl_vat_val_pat_ex", "pl_req_need_a", "pl_req_a_status",
        "pl_allow_manual_discount", "pl_allow_price_override", "pl_package_service_id", "pl_package_instance_id",
        "pl_package_line_role", "pl_package_component_order", "pl_package_parent_line_id",
        "pl_package_pricing_method", "pl_package_definition_token", "pl_offer_id", "pl_offer_dtl_id",
        "pl_offer_type", "pl_offer_instance_id", "pl_offer_line_role", "pl_offer_parent_line_id",
        "pl_offer_price_applied", "pl_offer_dis_applied", "pl_offer_name_snapshot",
        "pl_offer_object_version_number", "pl_offer_dtl_object_version_number",
    ];

    private static readonly string[] PreviewTotalsFields =
    [
        "pt_line_count", "pt_total_gross", "pt_total_discount", "pt_total_net", "pt_pat_pay", "pt_comp_pay",
        "pt_vat_total_pat", "pt_vat_total_co", "pt_cash_collected", "pt_amount_1", "pt_amount_2",
        "pt_remaining_amount", "pt_payment_status",
    ];

    private static readonly string[] EngineLineFields =
    [
        "el_serviceid", "el_qty", "el_price_override", "el_use_price_override", "el_discount_type", "el_disc",
        "el_my_disc", "el_teeth_no", "el_tooth_surface", "el_teeth_no2", "el_pat_serv_req_row_id",
        "el_approv_date", "el_approv_validity", "el_approv_ref_no", "el_claim_no", "el_req_need_a",
        "el_req_a_status", "el_package_service_id", "el_package_instance_id", "el_package_line_role",
        "el_package_component_order", "el_package_parent_line_id", "el_package_pricing_method",
        "el_package_definition_token", "el_offer_id", "el_offer_dtl_id", "el_offer_type", "el_offer_instance_id",
        "el_offer_line_role", "el_offer_parent_line_id", "el_offer_price_applied", "el_offer_dis_applied",
        "el_offer_name_snapshot", "el_offer_object_version_number", "el_offer_dtl_object_version_number",
    ];

    private static readonly string[] ImportResultFields =
    [
        "ir_source_type", "ir_source_count", "ir_imported_count", "ir_skipped_rejected_count",
        "ir_skipped_need_approval_count", "ir_skipped_invalid_count", "ir_has_price_overrides", "ir_message",
    ];

    private static readonly HashSet<string> TextFields = new(StringComparer.Ordinal)
    {
        "pl_client_id", "pl_serviceid", "pl_servicedesc", "pl_curr_code", "pl_manual_discount_type",
        "pl_discount_source", "pl_allow_manual_discount", "pl_allow_price_override", "pl_package_service_id",
        "pl_package_instance_id", "pl_package_line_role", "pl_package_pricing_method",
        "pl_package_definition_token", "pl_offer_instance_id", "pl_offer_line_role", "pl_offer_name_snapshot",
        "pt_payment_status",
        "el_serviceid", "el_use_price_override", "el_discount_type", "el_teeth_no", "el_tooth_surface",
        "el_teeth_no2", "el_approv_ref_no", "el_claim_no", "el_package_service_id", "el_package_instance_id",
        "el_package_line_role", "el_package_pricing_method", "el_package_definition_token",
        "el_offer_instance_id", "el_offer_line_role", "el_offer_name_snapshot",
        "ir_source_type", "ir_has_price_overrides", "ir_message",
    };

    private static readonly HashSet<string> DateFields = new(StringComparer.Ordinal) { "el_approv_date" };

    [Fact]
    public void ReadPreviewLines_CountBelowPopulatedSlots_ReadsOnlyCountedSlotsFieldByField()
    {
        using OracleCommand command = new();
        Dictionary<string, Array> arrays = PopulatedArrays(PreviewLineFields, populatedSlots: 3);
        Set(arrays, "pl_client_id", 0, "c-1");
        Set(arrays, "pl_serviceid", 0, "S1");
        Set(arrays, "pl_qty", 0, 1m);
        Set(arrays, "pl_price", 0, 12.35m);
        Set(arrays, "pl_discount_source", 0, "PLAN_AND_MANUAL");
        AddTable(command.Parameters, PreviewLineCount, new OracleDecimal(2), arrays);

        IReadOnlyList<EditablePreviewLine> lines = OutputArrayReader.ReadPreviewLines(command.Parameters);

        Assert.Equal(2, lines.Count);
        Assert.Equal("c-1", lines[0].ClientId);
        Assert.Equal("S1", lines[0].ServiceId);
        Assert.Equal(1m, lines[0].Qty);
        Assert.Equal(12.35m, lines[0].Price);
        Assert.Equal("PLAN_AND_MANUAL", lines[0].DiscountSource);
        EditablePreviewLine expectedFirst = ExpectedPreviewLine(0) with
        {
            ClientId = "c-1",
            ServiceId = "S1",
            Qty = 1m,
            Price = 12.35m,
            DiscountSource = "PLAN_AND_MANUAL",
        };
        Assert.Equal(expectedFirst, lines[0]);
        Assert.Equal(ExpectedPreviewLine(1), lines[1]);
    }

    [Fact]
    public void ReadPreviewLines_SlotBeyondCountHoldsInvalidValue_IsNotRead()
    {
        using OracleCommand command = new();
        Dictionary<string, Array> arrays = PopulatedArrays(PreviewLineFields, populatedSlots: 3);
        Set(arrays, "pl_line_no", 2, 2.5m);
        AddTable(command.Parameters, PreviewLineCount, new OracleDecimal(2), arrays);

        IReadOnlyList<EditablePreviewLine> lines = OutputArrayReader.ReadPreviewLines(command.Parameters);

        Assert.Equal(2, lines.Count);
        Assert.Equal(ExpectedPreviewLine(1), lines[1]);
    }

    [Fact]
    public void ReadPreviewLines_FractionalValueInWholeNumberField_ThrowsInvalidCast()
    {
        using OracleCommand command = new();
        Dictionary<string, Array> arrays = PopulatedArrays(PreviewLineFields, populatedSlots: 3);
        Set(arrays, "pl_line_no", 2, 2.5m);
        AddTable(command.Parameters, PreviewLineCount, new OracleDecimal(3), arrays);

        InvalidCastException error = Assert.Throws<InvalidCastException>(
            () => OutputArrayReader.ReadPreviewLines(command.Parameters));

        Assert.Contains("pl_line_no", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadPreviewLines_NullSlot_YieldsNullOnEveryProperty()
    {
        using OracleCommand command = new();
        AddTable(command.Parameters, PreviewLineCount, new OracleDecimal(1), PopulatedArrays(PreviewLineFields, populatedSlots: 0));

        IReadOnlyList<EditablePreviewLine> lines = OutputArrayReader.ReadPreviewLines(command.Parameters);

        EditablePreviewLine line = Assert.Single(lines);
        Assert.Null(line.Price);
        Assert.Null(line.ClientId);
        Assert.Null(line.LineNo);
        Assert.Null(line.OfferDtlObjectVersionNumber);
        Assert.Equal(new EditablePreviewLine(), line);
    }

    [Fact]
    public void ReadPreviewTotals_Scalars_MapFieldByField()
    {
        using OracleCommand command = new();
        Dictionary<string, object?> values = GeneratedScalars(PreviewTotalsFields);
        values["pt_payment_status"] = "Partial";
        values["pt_cash_collected"] = 100.25m;
        values["pt_amount_1"] = 50m;
        values["pt_vat_total_pat"] = null;
        AddScalars(command.Parameters, values);

        PreviewTotalsRow totals = OutputArrayReader.ReadPreviewTotals(command.Parameters);

        Assert.Equal("Partial", totals.PaymentStatus);
        Assert.Equal(100.25m, totals.CashCollected);
        Assert.Equal(50m, totals.Amount1);
        Assert.Null(totals.VatTotalPat);
        PreviewTotalsRow expected = ExpectedPreviewTotals() with
        {
            PaymentStatus = "Partial",
            CashCollected = 100.25m,
            Amount1 = 50m,
            VatTotalPat = null,
        };
        Assert.Equal(expected, totals);
    }

    [Fact]
    public void ReadPreviewTotals_ColonPrefixedNames_AreFound()
    {
        using OracleCommand command = new();
        foreach (KeyValuePair<string, object?> value in GeneratedScalars(PreviewTotalsFields))
        {
            AddScalar(command.Parameters, ":" + value.Key, TypeOf(value.Key), OracleValue(TypeOf(value.Key), value.Value));
        }

        PreviewTotalsRow totals = OutputArrayReader.ReadPreviewTotals(command.Parameters);

        Assert.Equal(ExpectedPreviewTotals(), totals);
    }

    [Fact]
    public void ReadPreviewTotals_NumberBeyondDecimalPrecision_RoundsTo28SignificantDigits()
    {
        using OracleCommand command = new();
        AddScalars(command.Parameters, GeneratedScalars(PreviewTotalsFields));
        Replace(command.Parameters, "pt_total_gross", OracleDecimal.Parse("1.23456789012345678901234567891"));

        PreviewTotalsRow totals = OutputArrayReader.ReadPreviewTotals(command.Parameters);

        Assert.Equal(1.234567890123456789012345679m, totals.TotalGross);
    }

    [Fact]
    public void ReadPreviewTotals_NumberBeyondDecimalRange_ThrowsOverflow()
    {
        using OracleCommand command = new();
        AddScalars(command.Parameters, GeneratedScalars(PreviewTotalsFields));
        Replace(command.Parameters, "pt_total_net", OracleDecimal.Parse("1000000000000000000000000000000"));

        OverflowException error = Assert.Throws<OverflowException>(
            () => OutputArrayReader.ReadPreviewTotals(command.Parameters));

        Assert.Contains("pt_total_net", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadEngineLines_CountOfOne_ReadsSlotZeroFieldByField()
    {
        using OracleCommand command = new();
        Dictionary<string, Array> arrays = PopulatedArrays(EngineLineFields, populatedSlots: 3);
        Set(arrays, "el_serviceid", 0, "1001");
        Set(arrays, "el_qty", 0, 2m);
        Set(arrays, "el_price_override", 0, 80.5m);
        Set(arrays, "el_use_price_override", 0, "Y");
        Set(arrays, "el_package_line_role", 0, "COMPONENT");
        Set(arrays, "el_offer_id", 0, 7m);
        AddTable(command.Parameters, EngineLineCount, new OracleDecimal(1), arrays);

        IReadOnlyList<EngineLineInput> lines = OutputArrayReader.ReadEngineLines(command.Parameters);

        EngineLineInput line = Assert.Single(lines);
        Assert.Equal("1001", line.ServiceId);
        Assert.Equal(2m, line.Qty);
        Assert.Equal(80.5m, line.PriceOverride);
        Assert.Equal("Y", line.UsePriceOverride);
        Assert.Equal("COMPONENT", line.PackageLineRole);
        Assert.Equal(7, line.OfferId);
        Assert.Equal(FirstApprovalDate, line.ApprovDate);
        EngineLineInput expected = ExpectedEngineLine(0) with
        {
            ServiceId = "1001",
            Qty = 2m,
            PriceOverride = 80.5m,
            UsePriceOverride = "Y",
            PackageLineRole = "COMPONENT",
            OfferId = 7,
        };
        Assert.Equal(expected, line);
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("negative")]
    [InlineData("oracle-null")]
    [InlineData("db-null")]
    [InlineData("clr-null")]
    public void ReadEngineLines_NoRowsCounted_ReturnsEmpty(string count)
    {
        object? countValue = count switch
        {
            "zero" => new OracleDecimal(0),
            "negative" => new OracleDecimal(-1),
            "oracle-null" => OracleDecimal.Null,
            "db-null" => DBNull.Value,
            _ => null,
        };
        using OracleCommand command = new();
        AddTable(command.Parameters, EngineLineCount, countValue, PopulatedArrays(EngineLineFields, populatedSlots: 3));

        IReadOnlyList<EngineLineInput> lines = OutputArrayReader.ReadEngineLines(command.Parameters);

        Assert.Empty(lines);
    }

    [Fact]
    public void ReadEngineLines_ArrayShorterThanCount_IsBoundedByTheShortestArray()
    {
        using OracleCommand command = new();
        Dictionary<string, Array> arrays = PopulatedArrays(EngineLineFields, populatedSlots: 3);
        AddTable(command.Parameters, EngineLineCount, new OracleDecimal(3), arrays);
        Replace(command.Parameters, "el_offer_type", new[] { new OracleDecimal(1), new OracleDecimal(2) });

        IReadOnlyList<EngineLineInput> lines = OutputArrayReader.ReadEngineLines(command.Parameters);

        Assert.Equal(2, lines.Count);
        Assert.Equal(ExpectedEngineLine(0) with { OfferType = 1 }, lines[0]);
        Assert.Equal(ExpectedEngineLine(1) with { OfferType = 2 }, lines[1]);
    }

    [Fact]
    public void ReadEngineLines_NullArrayValue_ReturnsEmpty()
    {
        using OracleCommand command = new();
        AddTable(command.Parameters, EngineLineCount, new OracleDecimal(3), PopulatedArrays(EngineLineFields, populatedSlots: 3));
        Replace(command.Parameters, "el_teeth_no", DBNull.Value);

        IReadOnlyList<EngineLineInput> lines = OutputArrayReader.ReadEngineLines(command.Parameters);

        Assert.Empty(lines);
    }

    [Theory]
    [InlineData("oracle-null")]
    [InlineData("db-null")]
    [InlineData("clr-null")]
    public void ReadEngineLines_NullElementRepresentations_YieldNull(string representation)
    {
        using OracleCommand command = new();
        AddTable(command.Parameters, EngineLineCount, new OracleDecimal(1), PopulatedArrays(EngineLineFields, populatedSlots: 1));
        Replace(command.Parameters, "el_price_override", new[] { NullElement(representation, OracleDecimal.Null) });
        Replace(command.Parameters, "el_use_price_override", new[] { NullElement(representation, OracleString.Null) });
        Replace(command.Parameters, "el_approv_date", new[] { NullElement(representation, OracleDate.Null) });
        Replace(command.Parameters, "el_offer_id", new[] { NullElement(representation, OracleDecimal.Null) });

        EngineLineInput line = Assert.Single(OutputArrayReader.ReadEngineLines(command.Parameters));

        Assert.Null(line.PriceOverride);
        Assert.Null(line.UsePriceOverride);
        Assert.Null(line.ApprovDate);
        Assert.Null(line.OfferId);
        Assert.Equal(ExpectedEngineLine(0) with
        {
            PriceOverride = null,
            UsePriceOverride = null,
            ApprovDate = null,
            OfferId = null,
        }, line);
    }

    [Fact]
    public void ReadEngineLines_ClrElementValues_AreAccepted()
    {
        using OracleCommand command = new();
        AddTable(command.Parameters, EngineLineCount, new OracleDecimal(1), PopulatedArrays(EngineLineFields, populatedSlots: 1));
        Replace(command.Parameters, "el_serviceid", new object[] { "2000" });
        Replace(command.Parameters, "el_qty", new object[] { 3 });
        Replace(command.Parameters, "el_price_override", new object[] { 45.75m });
        Replace(command.Parameters, "el_pat_serv_req_row_id", new object[] { 900000000001L });
        Replace(command.Parameters, "el_req_need_a", new object[] { (short)1 });
        Replace(command.Parameters, "el_offer_type", new object[] { (byte)2 });
        Replace(command.Parameters, "el_approv_date", new object[] { FirstApprovalDate.AddDays(5) });

        EngineLineInput line = Assert.Single(OutputArrayReader.ReadEngineLines(command.Parameters));

        Assert.Equal("2000", line.ServiceId);
        Assert.Equal(3m, line.Qty);
        Assert.Equal(45.75m, line.PriceOverride);
        Assert.Equal(900000000001L, line.PatServReqRowId);
        Assert.Equal(1, line.ReqNeedA);
        Assert.Equal(2, line.OfferType);
        Assert.Equal(FirstApprovalDate.AddDays(5), line.ApprovDate);
    }

    [Fact]
    public void ReadEngineLines_NonArrayValue_ThrowsInvalidCast()
    {
        using OracleCommand command = new();
        AddTable(command.Parameters, EngineLineCount, new OracleDecimal(1), PopulatedArrays(EngineLineFields, populatedSlots: 1));
        Replace(command.Parameters, "el_qty", new OracleDecimal(1));

        InvalidCastException error = Assert.Throws<InvalidCastException>(
            () => OutputArrayReader.ReadEngineLines(command.Parameters));

        Assert.Contains("el_qty", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadImportResult_Scalars_MapFieldByField()
    {
        using OracleCommand command = new();
        AddScalars(command.Parameters, new Dictionary<string, object?>
        {
            ["ir_source_type"] = "REQUEST",
            ["ir_source_count"] = 3m,
            ["ir_imported_count"] = 1m,
            ["ir_skipped_rejected_count"] = 1m,
            ["ir_skipped_need_approval_count"] = 1m,
            ["ir_skipped_invalid_count"] = 0m,
            ["ir_has_price_overrides"] = "N",
            ["ir_message"] = "1 line(s) imported.",
        });

        ImportResultRow result = OutputArrayReader.ReadImportResult(command.Parameters);

        Assert.Equal(
            new ImportResultRow
            {
                SourceType = "REQUEST",
                SourceCount = 3,
                ImportedCount = 1,
                SkippedRejectedCount = 1,
                SkippedNeedApprovalCount = 1,
                SkippedInvalidCount = 0,
                HasPriceOverrides = "N",
                Message = "1 line(s) imported.",
            },
            result);
    }

    [Fact]
    public void ReadImportResult_NullMessage_YieldsNull()
    {
        using OracleCommand command = new();
        Dictionary<string, object?> values = GeneratedScalars(ImportResultFields);
        values["ir_message"] = null;
        AddScalars(command.Parameters, values);

        ImportResultRow result = OutputArrayReader.ReadImportResult(command.Parameters);

        Assert.Null(result.Message);
        Assert.Equal(Text("ir_source_type", 0), result.SourceType);
        Assert.Equal((int)Number(ImportResultFields, "ir_skipped_invalid_count", 0), result.SkippedInvalidCount);
    }

    [Fact]
    public void ReadImportResult_FractionalCount_ThrowsInvalidCast()
    {
        using OracleCommand command = new();
        AddScalars(command.Parameters, GeneratedScalars(ImportResultFields));
        Replace(command.Parameters, "ir_source_count", new OracleDecimal(1.5m));

        InvalidCastException error = Assert.Throws<InvalidCastException>(
            () => OutputArrayReader.ReadImportResult(command.Parameters));

        Assert.Contains("ir_source_count", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadImportResult_MissingParameter_ThrowsInvalidOperation()
    {
        using OracleCommand command = new();
        AddScalars(command.Parameters, GeneratedScalars(ImportResultFields));
        command.Parameters.RemoveAt(command.Parameters.IndexOf("ir_message"));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => OutputArrayReader.ReadImportResult(command.Parameters));

        Assert.Contains("ir_message", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Readers_NullCollection_ThrowArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => OutputArrayReader.ReadPreviewLines(null!));
        Assert.Throws<ArgumentNullException>(() => OutputArrayReader.ReadPreviewTotals(null!));
        Assert.Throws<ArgumentNullException>(() => OutputArrayReader.ReadEngineLines(null!));
        Assert.Throws<ArgumentNullException>(() => OutputArrayReader.ReadImportResult(null!));
    }

    private static void AddArray(OracleParameterCollection parameters, string name, OracleDbType type, Array values) =>
        parameters.Add(new OracleParameter(name, type)
        {
            Direction = ParameterDirection.Output,
            CollectionType = OracleCollectionType.PLSQLAssociativeArray,
            Size = Capacity,
            Value = values,
        });

    private static void AddScalar(OracleParameterCollection parameters, string name, OracleDbType type, object? value) =>
        parameters.Add(new OracleParameter(name, type) { Direction = ParameterDirection.Output, Value = value });

    private static void AddTable(OracleParameterCollection parameters, string countName, object? count, Dictionary<string, Array> arrays)
    {
        AddScalar(parameters, countName, OracleDbType.Decimal, count);
        foreach (KeyValuePair<string, Array> array in arrays)
        {
            AddArray(parameters, array.Key, TypeOf(array.Key), array.Value);
        }
    }

    private static void AddScalars(OracleParameterCollection parameters, Dictionary<string, object?> values)
    {
        foreach (KeyValuePair<string, object?> value in values)
        {
            AddScalar(parameters, value.Key, TypeOf(value.Key), OracleValue(TypeOf(value.Key), value.Value));
        }
    }

    private static void Replace(OracleParameterCollection parameters, string name, object? value) =>
        parameters[parameters.IndexOf(name)].Value = value;

    private static Dictionary<string, Array> PopulatedArrays(string[] fields, int populatedSlots)
    {
        var arrays = new Dictionary<string, Array>(StringComparer.Ordinal);
        foreach (string field in fields)
        {
            OracleDbType type = TypeOf(field);
            Array values = NullArray(type);
            for (int slot = 0; slot < populatedSlots; slot++)
            {
                values.SetValue(OracleValue(type, Generated(fields, field, slot)), slot);
            }

            arrays[field] = values;
        }

        return arrays;
    }

    private static void Set(Dictionary<string, Array> arrays, string field, int slot, object? value) =>
        arrays[field].SetValue(OracleValue(TypeOf(field), value), slot);

    private static Dictionary<string, object?> GeneratedScalars(string[] fields) =>
        fields.ToDictionary(field => field, field => (object?)Generated(fields, field, 0), StringComparer.Ordinal);

    private static OracleDbType TypeOf(string field) =>
        TextFields.Contains(field) ? OracleDbType.Varchar2
        : DateFields.Contains(field) ? OracleDbType.Date
        : OracleDbType.Decimal;

    private static Array NullArray(OracleDbType type) => type switch
    {
        OracleDbType.Varchar2 => Enumerable.Repeat(OracleString.Null, Capacity).ToArray(),
        OracleDbType.Date => Enumerable.Repeat(OracleDate.Null, Capacity).ToArray(),
        _ => Enumerable.Repeat(OracleDecimal.Null, Capacity).ToArray(),
    };

    private static object OracleValue(OracleDbType type, object? value) => (type, value) switch
    {
        (OracleDbType.Varchar2, null) => OracleString.Null,
        (OracleDbType.Varchar2, string text) => new OracleString(text),
        (OracleDbType.Date, null) => OracleDate.Null,
        (OracleDbType.Date, DateTime date) => new OracleDate(date),
        (OracleDbType.Decimal, null) => OracleDecimal.Null,
        (OracleDbType.Decimal, decimal number) => new OracleDecimal(number),
        _ => throw new ArgumentException($"No {type} test value for {value!.GetType().Name}.", nameof(value)),
    };

    private static object? NullElement(string representation, object oracleNull) => representation switch
    {
        "oracle-null" => oracleNull,
        "db-null" => DBNull.Value,
        _ => null,
    };

    private static object Generated(string[] fields, string field, int slot) => TypeOf(field) switch
    {
        OracleDbType.Varchar2 => Text(field, slot),
        OracleDbType.Date => FirstApprovalDate.AddDays(slot),
        _ => Number(fields, field, slot),
    };

    private static string Text(string field, int slot) => $"{field}#{slot}";

    private static decimal Number(string[] fields, string field, int slot)
    {
        int index = Array.IndexOf(fields, field);
        ArgumentOutOfRangeException.ThrowIfNegative(index, field);
        return index + 1 + (100 * slot);
    }


    private static EditablePreviewLine ExpectedPreviewLine(int slot)
    {
        string T(string field) => Text(field, slot);
        decimal N(string field) => Number(PreviewLineFields, field, slot);
        return new EditablePreviewLine
        {
            ClientId = T("pl_client_id"),
            LineNo = (int)N("pl_line_no"),
            ServiceId = T("pl_serviceid"),
            ServiceDesc = T("pl_servicedesc"),
            CatId = (int)N("pl_catid"),
            ListId = N("pl_list_id"),
            CurrCode = T("pl_curr_code"),
            Qty = N("pl_qty"),
            Price = N("pl_price"),
            PlanDiscountPct = N("pl_plan_discount_pct"),
            PlanDiscountAmount = N("pl_plan_discount_amount"),
            ManualDiscountType = T("pl_manual_discount_type"),
            ManualDiscountPct = N("pl_manual_discount_pct"),
            ManualDiscountAmount = N("pl_manual_discount_amount"),
            DiscountSource = T("pl_discount_source"),
            Disc = N("pl_disc"),
            MyDisc = N("pl_my_disc"),
            MyPrice = N("pl_my_price"),
            MyNet = N("pl_my_net"),
            ThePay = N("pl_the_pay"),
            TheComp = N("pl_the_comp"),
            VatRate = N("pl_vat_rate"),
            VatValPat = N("pl_vat_val_pat"),
            VatValCo = N("pl_vat_val_co"),
            VatValPatEx = N("pl_vat_val_pat_ex"),
            ReqNeedA = (int)N("pl_req_need_a"),
            ReqAStatus = (int)N("pl_req_a_status"),
            AllowManualDiscount = T("pl_allow_manual_discount"),
            AllowPriceOverride = T("pl_allow_price_override"),
            PackageServiceId = T("pl_package_service_id"),
            PackageInstanceId = T("pl_package_instance_id"),
            PackageLineRole = T("pl_package_line_role"),
            PackageComponentOrder = (int)N("pl_package_component_order"),
            PackageParentLineId = (long)N("pl_package_parent_line_id"),
            PackagePricingMethod = T("pl_package_pricing_method"),
            PackageDefinitionToken = T("pl_package_definition_token"),
            OfferId = (int)N("pl_offer_id"),
            OfferDtlId = (long)N("pl_offer_dtl_id"),
            OfferType = (int)N("pl_offer_type"),
            OfferInstanceId = T("pl_offer_instance_id"),
            OfferLineRole = T("pl_offer_line_role"),
            OfferParentLineId = (long)N("pl_offer_parent_line_id"),
            OfferPriceApplied = N("pl_offer_price_applied"),
            OfferDisApplied = N("pl_offer_dis_applied"),
            OfferNameSnapshot = T("pl_offer_name_snapshot"),
            OfferObjectVersionNumber = (long)N("pl_offer_object_version_number"),
            OfferDtlObjectVersionNumber = (long)N("pl_offer_dtl_object_version_number"),
        };
    }

    private static PreviewTotalsRow ExpectedPreviewTotals()
    {
        decimal N(string field) => Number(PreviewTotalsFields, field, 0);
        return new PreviewTotalsRow
        {
            LineCount = (int)N("pt_line_count"),
            TotalGross = N("pt_total_gross"),
            TotalDiscount = N("pt_total_discount"),
            TotalNet = N("pt_total_net"),
            PatPay = N("pt_pat_pay"),
            CompPay = N("pt_comp_pay"),
            VatTotalPat = N("pt_vat_total_pat"),
            VatTotalCo = N("pt_vat_total_co"),
            CashCollected = N("pt_cash_collected"),
            Amount1 = N("pt_amount_1"),
            Amount2 = N("pt_amount_2"),
            RemainingAmount = N("pt_remaining_amount"),
            PaymentStatus = Text("pt_payment_status", 0),
        };
    }

    private static EngineLineInput ExpectedEngineLine(int slot)
    {
        string T(string field) => Text(field, slot);
        decimal N(string field) => Number(EngineLineFields, field, slot);
        return new EngineLineInput
        {
            ServiceId = T("el_serviceid"),
            Qty = N("el_qty"),
            PriceOverride = N("el_price_override"),
            UsePriceOverride = T("el_use_price_override"),
            DiscountType = T("el_discount_type"),
            Disc = N("el_disc"),
            MyDisc = N("el_my_disc"),
            TeethNo = T("el_teeth_no"),
            ToothSurface = T("el_tooth_surface"),
            TeethNo2 = T("el_teeth_no2"),
            PatServReqRowId = (long)N("el_pat_serv_req_row_id"),
            ApprovDate = FirstApprovalDate.AddDays(slot),
            ApprovValidity = N("el_approv_validity"),
            ApprovRefNo = T("el_approv_ref_no"),
            ClaimNo = T("el_claim_no"),
            ReqNeedA = (int)N("el_req_need_a"),
            ReqAStatus = (int)N("el_req_a_status"),
            PackageServiceId = T("el_package_service_id"),
            PackageInstanceId = T("el_package_instance_id"),
            PackageLineRole = T("el_package_line_role"),
            PackageComponentOrder = (int)N("el_package_component_order"),
            PackageParentLineId = (long)N("el_package_parent_line_id"),
            PackagePricingMethod = T("el_package_pricing_method"),
            PackageDefinitionToken = T("el_package_definition_token"),
            OfferId = (int)N("el_offer_id"),
            OfferDtlId = (long)N("el_offer_dtl_id"),
            OfferType = (int)N("el_offer_type"),
            OfferInstanceId = T("el_offer_instance_id"),
            OfferLineRole = T("el_offer_line_role"),
            OfferParentLineId = (long)N("el_offer_parent_line_id"),
            OfferPriceApplied = N("el_offer_price_applied"),
            OfferDisApplied = N("el_offer_dis_applied"),
            OfferNameSnapshot = T("el_offer_name_snapshot"),
            OfferObjectVersionNumber = (long)N("el_offer_object_version_number"),
            OfferDtlObjectVersionNumber = (long)N("el_offer_dtl_object_version_number"),
        };
    }
}
