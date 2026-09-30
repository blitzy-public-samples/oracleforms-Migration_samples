using System.Data;
using System.Text;
using System.Text.Json;
using Billing.Invoicing.Api.Errors;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Domain.Model;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

namespace Billing.Invoicing.Tests.Data;

/// <summary>Associative-array binding tests for <see cref="LineInputBinder"/> and <see cref="ClientIdBinder"/>, and how their rejections are translated and written.</summary>
[Trait("Category", "DataUnit")]
public sealed class LineInputBinderTests
{
    private const string LineCountName = "line_count";
    private const string ClientIdName = "l_client_id";
    private const string UsePriceOverrideName = "l_use_price_override";

    /// <summary>U+00E9, two bytes in UTF-8.</summary>
    private const char TwoByteCharacter = '\u00E9';

    private static readonly (string Name, OracleDbType Type)[] ExpectedArrays =
    [
        ("l_serviceid", OracleDbType.Varchar2),
        ("l_qty", OracleDbType.Decimal),
        ("l_price_override", OracleDbType.Decimal),
        ("l_use_price_override", OracleDbType.Varchar2),
        ("l_discount_type", OracleDbType.Varchar2),
        ("l_disc", OracleDbType.Decimal),
        ("l_my_disc", OracleDbType.Decimal),
        ("l_teeth_no", OracleDbType.Varchar2),
        ("l_tooth_surface", OracleDbType.Varchar2),
        ("l_teeth_no2", OracleDbType.Varchar2),
        ("l_pat_serv_req_row_id", OracleDbType.Decimal),
        ("l_approv_date", OracleDbType.Date),
        ("l_approv_validity", OracleDbType.Decimal),
        ("l_approv_ref_no", OracleDbType.Varchar2),
        ("l_claim_no", OracleDbType.Varchar2),
        ("l_req_need_a", OracleDbType.Decimal),
        ("l_req_a_status", OracleDbType.Decimal),
        ("l_package_service_id", OracleDbType.Varchar2),
        ("l_package_instance_id", OracleDbType.Varchar2),
        ("l_package_line_role", OracleDbType.Varchar2),
        ("l_package_component_order", OracleDbType.Decimal),
        ("l_package_parent_line_id", OracleDbType.Decimal),
        ("l_package_pricing_method", OracleDbType.Varchar2),
        ("l_package_definition_token", OracleDbType.Varchar2),
        ("l_offer_id", OracleDbType.Decimal),
        ("l_offer_dtl_id", OracleDbType.Decimal),
        ("l_offer_type", OracleDbType.Decimal),
        ("l_offer_instance_id", OracleDbType.Varchar2),
        ("l_offer_line_role", OracleDbType.Varchar2),
        ("l_offer_parent_line_id", OracleDbType.Decimal),
        ("l_offer_price_applied", OracleDbType.Decimal),
        ("l_offer_dis_applied", OracleDbType.Decimal),
        ("l_offer_name_snapshot", OracleDbType.Varchar2),
        ("l_offer_object_version_number", OracleDbType.Decimal),
        ("l_offer_dtl_object_version_number", OracleDbType.Decimal),
    ];

    private static readonly DateTime ApprovalDate = new(2026, 9, 14, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>Every VARCHAR2 array in t_line_input order with its destination width in bytes; 4000 where the width is unstated.</summary>
    private static readonly (string Name, int Width)[] LineTextWidths =
    [
        ("l_serviceid", 20),
        (UsePriceOverrideName, 1),
        ("l_discount_type", 1),
        ("l_teeth_no", 2),
        ("l_tooth_surface", 7),
        ("l_teeth_no2", 2),
        ("l_approv_ref_no", 20),
        ("l_claim_no", 4000),
        ("l_package_service_id", 4000),
        ("l_package_instance_id", 4000),
        ("l_package_line_role", 4000),
        ("l_package_pricing_method", 4000),
        ("l_package_definition_token", 64),
        ("l_offer_instance_id", 4000),
        ("l_offer_line_role", 4000),
        ("l_offer_name_snapshot", 4000),
    ];

    /// <summary>All 16 VARCHAR2 arrays with their destination widths.</summary>
    public static TheoryData<string, int> TextArrayWidths => ToTheoryData(LineTextWidths);

    /// <summary>The VARCHAR2 arrays bound from a line field, with their destination widths; <c>l_use_price_override</c> is computed by the binder.</summary>
    public static TheoryData<string, int> SettableTextArrayWidths =>
        ToTheoryData(LineTextWidths.Where(array => array.Name != UsePriceOverrideName));

    [Fact]
    public void Bind_TwoLines_Returns35ArraysInLineInputOrderThenLineCount()
    {
        IReadOnlyList<OracleParameter> parameters = LineInputBinder.Bind(TwoLines());

        Assert.Equal(36, parameters.Count);
        Assert.Equal(
            ExpectedArrays.Select(array => array.Name).Append(LineCountName),
            parameters.Select(Name));
    }

    [Fact]
    public void Bind_TwoLines_UsesTheOracleTypeOfEachArray()
    {
        IReadOnlyList<OracleParameter> parameters = LineInputBinder.Bind(TwoLines());

        Assert.Equal(
            ExpectedArrays,
            Arrays(parameters).Select(parameter => (Name(parameter), parameter.OracleDbType)));
    }

    [Fact]
    public void Bind_TwoLines_BindsEveryArrayAsInputAssociativeArrayOfTwoElements()
    {
        IReadOnlyList<OracleParameter> parameters = LineInputBinder.Bind(TwoLines());

        Assert.All(Arrays(parameters), parameter =>
        {
            Assert.Equal(OracleCollectionType.PLSQLAssociativeArray, parameter.CollectionType);
            Assert.Equal(ParameterDirection.Input, parameter.Direction);
            Assert.Equal(2, parameter.Size);
            Assert.Equal(2, Elements(parameter).Length);
        });
    }

    [Fact]
    public void Bind_TwoLines_SetsPositiveArrayBindSizeOnEveryVarchar2Array()
    {
        IReadOnlyList<OracleParameter> parameters = LineInputBinder.Bind(TwoLines());
        OracleParameter[] textArrays = Arrays(parameters)
            .Where(parameter => parameter.OracleDbType == OracleDbType.Varchar2)
            .ToArray();

        Assert.NotEmpty(textArrays);
        Assert.All(textArrays, parameter =>
        {
            Assert.NotNull(parameter.ArrayBindSize);
            Assert.Equal(2, parameter.ArrayBindSize.Length);
            Assert.All(parameter.ArrayBindSize, size => Assert.True(size > 0));

            object?[] values = Plain(parameter);
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] is string text)
                {
                    Assert.True(parameter.ArrayBindSize[i] >= text.Length, Name(parameter));
                }
            }
        });
    }

    [Fact]
    public void Bind_TwoLines_BindsLineCountAsInputScalar()
    {
        OracleParameter lineCount = Find(LineInputBinder.Bind(TwoLines()), LineCountName);

        Assert.Equal(OracleCollectionType.None, lineCount.CollectionType);
        Assert.Equal(ParameterDirection.Input, lineCount.Direction);
        Assert.Equal(2, Convert.ToInt32(lineCount.Value));
    }

    [Fact]
    public void Bind_TwoLines_PlacesEachLineAtItsOwnIndex()
    {
        IReadOnlyList<OracleParameter> parameters = LineInputBinder.Bind(TwoLines());

        Assert.Equal(new object?[] { "S1", "S2" }, Plain(Find(parameters, "l_serviceid")));
        Assert.Equal(new object?[] { 2m, 3m }, Plain(Find(parameters, "l_qty")));
        Assert.Equal(new object?[] { "c-1", "c-2" }, Plain(ClientIdBinder.Bind(TwoLines())));
    }

    [Fact]
    public void Bind_FullyPopulatedLine_MapsEveryFieldToItsArray()
    {
        InvoiceLineDraft line = FullyPopulatedLine();

        IReadOnlyList<OracleParameter> parameters = LineInputBinder.Bind([line]);

        var expected = new Dictionary<string, object?>
        {
            ["l_serviceid"] = "SV-10",
            ["l_qty"] = 4m,
            ["l_price_override"] = null,
            ["l_use_price_override"] = "N",
            ["l_discount_type"] = "V",
            ["l_disc"] = 12.5m,
            ["l_my_disc"] = 7.25m,
            ["l_teeth_no"] = "11",
            ["l_tooth_surface"] = "M",
            ["l_teeth_no2"] = "12",
            ["l_pat_serv_req_row_id"] = 9001m,
            ["l_approv_date"] = ApprovalDate,
            ["l_approv_validity"] = 30m,
            ["l_approv_ref_no"] = "APR-77",
            ["l_claim_no"] = "O-5-14-140926",
            ["l_req_need_a"] = 1m,
            ["l_req_a_status"] = 2m,
            ["l_package_service_id"] = "PKG-1",
            ["l_package_instance_id"] = "PI-1",
            ["l_package_line_role"] = "COMPONENT",
            ["l_package_component_order"] = 3m,
            ["l_package_parent_line_id"] = 501m,
            ["l_package_pricing_method"] = "FIXED",
            ["l_package_definition_token"] = "TOKEN-ABC",
            ["l_offer_id"] = 61m,
            ["l_offer_dtl_id"] = 6101m,
            ["l_offer_type"] = 2m,
            ["l_offer_instance_id"] = "OFR-INST-1",
            ["l_offer_line_role"] = "COMPONENT",
            ["l_offer_parent_line_id"] = 502m,
            ["l_offer_price_applied"] = 80m,
            ["l_offer_dis_applied"] = 20m,
            ["l_offer_name_snapshot"] = "Summer offer",
            ["l_offer_object_version_number"] = 5m,
            ["l_offer_dtl_object_version_number"] = 6m,
        };

        Assert.Equal(ExpectedArrays.Select(array => array.Name).Order(), expected.Keys.Order());
        Assert.All(Arrays(parameters), parameter =>
            Assert.Equal(expected[Name(parameter)], Assert.Single(Plain(parameter))));
    }

    [Fact]
    public void Bind_NullFieldValues_AreBoundAsNullElementsWithNullInsertStatus()
    {
        InvoiceLineDraft line = new() { ServiceId = "S1", DiscountType = null };

        IReadOnlyList<OracleParameter> parameters = LineInputBinder.Bind([line]);

        OracleParameter[] nullArrays = Arrays(parameters)
            .Where(parameter => Name(parameter) is not "l_serviceid" and not "l_use_price_override")
            .ToArray();
        Assert.Equal(33, nullArrays.Length);
        Assert.All(nullArrays, parameter =>
        {
            Assert.True(IsNull(Assert.Single(Elements(parameter))), Name(parameter));
            Assert.Equal(OracleParameterStatus.NullInsert, Assert.Single(parameter.ArrayBindStatus));
        });
        Assert.Equal(
            OracleParameterStatus.Success,
            Assert.Single(Find(parameters, "l_serviceid").ArrayBindStatus));
    }

    [Fact]
    public void Bind_NoLines_SendsOneNullPlaceholderPerArrayAndZeroLineCount()
    {
        IReadOnlyList<OracleParameter> parameters = LineInputBinder.Bind([]);

        Assert.Equal(36, parameters.Count);
        Assert.All(Arrays(parameters), parameter =>
        {
            Assert.Equal(OracleCollectionType.PLSQLAssociativeArray, parameter.CollectionType);
            Assert.Equal(1, parameter.Size);
            Assert.True(IsNull(Assert.Single(Elements(parameter))), Name(parameter));
        });
        Assert.Equal(0, Convert.ToInt32(Find(parameters, LineCountName).Value));
    }

    [Fact]
    public void ClientIdBinder_NoLines_SendsOneNullPlaceholder()
    {
        OracleParameter clientIds = ClientIdBinder.Bind([]);

        Assert.Equal(ClientIdName, Name(clientIds));
        Assert.Equal(OracleCollectionType.PLSQLAssociativeArray, clientIds.CollectionType);
        Assert.Equal(1, clientIds.Size);
        Assert.True(IsNull(Assert.Single(Elements(clientIds))));
    }

    [Fact]
    public void ClientIdBinder_ThreeLines_AlignsClientIdsWithServiceIds()
    {
        IReadOnlyList<InvoiceLineDraft> lines = [Line("S1", "c-1"), Line("S2", "c-2"), Line("S3", "c-3")];

        OracleParameter clientIds = ClientIdBinder.Bind(lines);
        OracleParameter serviceIds = Find(LineInputBinder.Bind(lines), "l_serviceid");

        Assert.Equal(ClientIdName, Name(clientIds));
        Assert.Equal(OracleDbType.Varchar2, clientIds.OracleDbType);
        Assert.Equal(OracleCollectionType.PLSQLAssociativeArray, clientIds.CollectionType);
        Assert.Equal(ParameterDirection.Input, clientIds.Direction);
        Assert.Equal(3, clientIds.Size);
        Assert.Equal(3, serviceIds.Size);
        Assert.NotNull(clientIds.ArrayBindSize);
        Assert.All(clientIds.ArrayBindSize, size => Assert.True(size > 0));

        object?[] boundClientIds = Plain(clientIds);
        object?[] boundServiceIds = Plain(serviceIds);
        Assert.Equal(new object?[] { "c-1", "c-2", "c-3" }, boundClientIds);
        for (var i = 0; i < lines.Count; i++)
        {
            Assert.Equal(lines[i].ServiceId, boundServiceIds[i]);
            Assert.Equal(lines[i].ClientId, boundClientIds[i]);
        }
    }

    [Fact]
    public void ClientIdBinder_LineWithoutClientId_BindsNullElementAtItsIndex()
    {
        IReadOnlyList<InvoiceLineDraft> lines = [Line("S1", "c-1"), new InvoiceLineDraft { ServiceId = "S2" }];

        OracleParameter clientIds = ClientIdBinder.Bind(lines);

        object?[] elements = Elements(clientIds);
        Assert.Equal("c-1", Plain(elements[0]));
        Assert.True(IsNull(elements[1]));
        Assert.Equal(OracleParameterStatus.NullInsert, clientIds.ArrayBindStatus[1]);
    }

    [Fact]
    public void ClientIdBinder_ClientIdOf4000Characters_IsBoundWholeWithItsLengthAsBindSize()
    {
        string clientId = new('x', 4000);

        OracleParameter clientIds = ClientIdBinder.Bind([Line("S1", clientId)]);

        Assert.Equal(clientId, Assert.Single(Plain(clientIds)));
        Assert.Equal(4000, Assert.Single(clientIds.ArrayBindSize));
        Assert.Equal(OracleParameterStatus.Success, Assert.Single(clientIds.ArrayBindStatus));
    }

    [Fact]
    public void ClientIdBinder_ClientIdLongerThan4000Characters_ThrowsNamingTheLine()
    {
        IReadOnlyList<InvoiceLineDraft> lines = [Line("S1", "c-1"), Line("S2", "c-2"), Line("S3", new string('x', 4001))];

        ArgumentException error = Assert.Throws<ArgumentException>(() => ClientIdBinder.Bind(lines));

        Assert.Contains("index 2", error.Message);
        Assert.Contains(ClientIdName, error.Message);
        Assert.Contains("4001", error.Message);
        Assert.Contains("4000", error.Message);
        Assert.Equal("lines", error.ParamName);
    }

    [Fact]
    public void ClientIdBinder_MixedLengthClientIds_KeepEachFullValueAndBindSizeAtItsIndex()
    {
        string longest = new('y', 4000);
        IReadOnlyList<InvoiceLineDraft> lines = [Line("S1", "c-1"), Line("S2", longest), new InvoiceLineDraft { ServiceId = "S3" }];

        OracleParameter clientIds = ClientIdBinder.Bind(lines);

        Assert.Equal(3, clientIds.Size);
        Assert.Equal(new object?[] { "c-1", longest, null }, Plain(clientIds));
        Assert.Equal(new[] { 3, 4000, 1 }, clientIds.ArrayBindSize);
        Assert.Equal(
            new[] { OracleParameterStatus.Success, OracleParameterStatus.Success, OracleParameterStatus.NullInsert },
            clientIds.ArrayBindStatus);
    }

    [Fact]
    public void ClientIdBinder_ClientIdOf4000Utf8Bytes_IsBoundWholeWithItsCharacterLengthAsBindSize()
    {
        string clientId = new(TwoByteCharacter, 2000);

        OracleParameter clientIds = ClientIdBinder.Bind([Line("S1", clientId)]);

        Assert.Equal(clientId, Assert.Single(Plain(clientIds)));
        Assert.Equal(2000, Assert.Single(clientIds.ArrayBindSize));
        Assert.Equal(OracleParameterStatus.Success, Assert.Single(clientIds.ArrayBindStatus));
    }

    [Fact]
    public void ClientIdBinder_ClientIdOver4000Utf8Bytes_ThrowsNamingTheLine()
    {
        IReadOnlyList<InvoiceLineDraft> lines = [Line("S1", "c-1"), Line("S2", new string(TwoByteCharacter, 2001))];

        ArgumentException error = Assert.Throws<ArgumentException>(() => ClientIdBinder.Bind(lines));

        Assert.Contains("index 1", error.Message);
        Assert.Contains(ClientIdName, error.Message);
        Assert.Contains("4002", error.Message);
        Assert.Contains("UTF-8", error.Message);
        Assert.Equal("lines", error.ParamName);
    }

    [Theory]
    [InlineData("direct-without-override", null, "N")]
    [InlineData("direct-flagged-without-override", null, "N")]
    [InlineData("direct-with-override", 42.5, "Y")]
    [InlineData("package-parent-with-override", 42.5, "Y")]
    [InlineData("package-component-with-override", null, "N")]
    [InlineData("package-component-padded-lowercase-with-override", null, "N")]
    [InlineData("offer-line-with-override", null, "N")]
    [InlineData("offer-id-only-with-override", null, "N")]
    [InlineData("offer-role-only-with-override", null, "N")]
    public void Bind_PriceOverride_IsBoundOnlyOnDirectOrParentLinesThatCarryOne(
        string lineKind,
        double? expectedOverride,
        string expectedFlag)
    {
        IReadOnlyList<OracleParameter> parameters = LineInputBinder.Bind([PriceOverrideCase(lineKind)]);

        object? expectedValue = expectedOverride is { } amount ? (decimal)amount : null;
        Assert.Equal(expectedValue, Assert.Single(Plain(Find(parameters, "l_price_override"))));
        Assert.Equal(expectedFlag, Assert.Single(Plain(Find(parameters, "l_use_price_override"))));
    }

    [Fact]
    public void Bind_MixedLines_DecidesPriceOverridePerLine()
    {
        IReadOnlyList<InvoiceLineDraft> lines =
        [
            PriceOverrideCase("direct-with-override"),
            PriceOverrideCase("package-component-with-override"),
            PriceOverrideCase("direct-without-override"),
            PriceOverrideCase("offer-line-with-override"),
        ];

        IReadOnlyList<OracleParameter> parameters = LineInputBinder.Bind(lines);

        Assert.Equal(new object?[] { 42.5m, null, null, null }, Plain(Find(parameters, "l_price_override")));
        Assert.Equal(new object?[] { "Y", "N", "N", "N" }, Plain(Find(parameters, "l_use_price_override")));
    }

    [Fact]
    public void Bind_ParameterNames_ExcludeDisplayOnlyLineFields()
    {
        string[] names = LineInputBinder.Bind(TwoLines()).Select(Name).ToArray();

        // Every bound name containing "price" is a t_line_input field (BIL_INVOICE_ENGINE.sql:27-63).
        Assert.Equal(
            new[] { "l_price_override", "l_use_price_override", "l_offer_price_applied" },
            names.Where(name => name.Contains("price", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain("l_price", names);
        Assert.DoesNotContain("l_catid", names);
        Assert.DoesNotContain("l_fixpay", names);
        Assert.DoesNotContain("l_payrate", names);
        Assert.DoesNotContain("l_ins_emp", names);
        Assert.DoesNotContain(ClientIdName, names);
        Assert.DoesNotContain(names, name => name.StartsWith("l_lens", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.Contains("lenses", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("l_f_l_indicator", names);
        Assert.DoesNotContain("l_number_of_pairs", names);
    }

    [Fact]
    public void Bind_DisplayOnlyFieldChanges_LeaveEveryBoundValueUnchanged()
    {
        IReadOnlyList<InvoiceLineDraft> original =
        [
            FullyPopulatedLine(),
            PriceOverrideCase("direct-with-override") with { CatId = 3, FixPay = 5m, PayRate = 90m },
        ];
        IReadOnlyList<InvoiceLineDraft> changed = original
            .Select(line => line with
            {
                Price = 123.45m,
                CatId = 9,
                FixPay = 15m,
                PayRate = 70m,
                RegularLensesType = "Toric",
                LensSpecifications = "-2.00",
                ContactLensesType = "Soft",
                FLIndicator = "L",
                NumberOfPairs = "2",
                InsEmp = 4411,
            })
            .ToArray();

        IReadOnlyList<OracleParameter> before = LineInputBinder.Bind(original);
        IReadOnlyList<OracleParameter> after = LineInputBinder.Bind(changed);

        Assert.Equal(before.Select(Name), after.Select(Name));
        for (var i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].OracleDbType, after[i].OracleDbType);
            if (before[i].CollectionType == OracleCollectionType.PLSQLAssociativeArray)
            {
                Assert.Equal(Plain(before[i]), Plain(after[i]));
                Assert.Equal(before[i].ArrayBindSize, after[i].ArrayBindSize);
            }
            else
            {
                Assert.Equal(before[i].Value, after[i].Value);
            }
        }
    }

    [Fact]
    public void Bind_TextOf4000Characters_IsBoundWithItsLengthAsBindSize()
    {
        string snapshot = new('x', 4000);

        OracleParameter parameter = Find(
            LineInputBinder.Bind([Line("S1", "c-1") with { OfferNameSnapshot = snapshot }]),
            "l_offer_name_snapshot");

        Assert.Equal(snapshot, Assert.Single(Plain(parameter)));
        Assert.Equal(4000, Assert.Single(parameter.ArrayBindSize));
    }

    [Fact]
    public void Bind_TextLongerThan4000Characters_Throws()
    {
        InvoiceLineDraft line = Line("S1", "c-1") with { OfferNameSnapshot = new string('x', 4001) };

        ArgumentException error = Assert.Throws<ArgumentException>(() => LineInputBinder.Bind([line]));

        Assert.Contains("l_offer_name_snapshot", error.Message);
    }

    [Fact]
    public void Bind_TextOf4000Utf8Bytes_IsBoundWithItsCharacterLengthAsBindSize()
    {
        string snapshot = new(TwoByteCharacter, 2000);

        OracleParameter parameter = Find(
            LineInputBinder.Bind([Line("S1", "c-1") with { OfferNameSnapshot = snapshot }]),
            "l_offer_name_snapshot");

        Assert.Equal(snapshot, Assert.Single(Plain(parameter)));
        Assert.Equal(2000, Assert.Single(parameter.ArrayBindSize));
        Assert.Equal(OracleParameterStatus.Success, Assert.Single(parameter.ArrayBindStatus));
    }

    [Fact]
    public void Bind_TextOver4000Utf8Bytes_ThrowsNamingTheLineAndArray()
    {
        IReadOnlyList<InvoiceLineDraft> lines =
        [
            Line("S1", "c-1"),
            Line("S2", "c-2") with { OfferNameSnapshot = new string(TwoByteCharacter, 2001) },
        ];

        ArgumentException error = Assert.Throws<ArgumentException>(() => LineInputBinder.Bind(lines));

        Assert.Contains("index 1", error.Message);
        Assert.Contains("l_offer_name_snapshot", error.Message);
        Assert.Contains("4002", error.Message);
        Assert.Contains("UTF-8", error.Message);
        Assert.Equal("lines", error.ParamName);
    }

    [Fact]
    public void TextArrayWidths_CoverEveryVarchar2ArrayInLineInputOrder()
    {
        Assert.Equal(
            ExpectedArrays.Where(array => array.Type == OracleDbType.Varchar2).Select(array => array.Name),
            LineTextWidths.Select(array => array.Name));
    }

    [Theory]
    [MemberData(nameof(TextArrayWidths))]
    public void Bind_TextOfItsFieldWidthInCharacters_IsBoundWholeWithItsLengthAsBindSize(string arrayName, int width)
    {
        string value = arrayName == UsePriceOverrideName ? "Y" : new string('x', width);
        InvoiceLineDraft line = arrayName == UsePriceOverrideName
            ? PriceOverrideCase("direct-with-override")
            : WithText(Line("S1", "c-1"), arrayName, value);

        OracleParameter parameter = Find(LineInputBinder.Bind([line]), arrayName);

        Assert.Equal(width, value.Length);
        Assert.Equal(value, Assert.Single(Plain(parameter)));
        Assert.Equal(width, Assert.Single(parameter.ArrayBindSize));
        Assert.Equal(OracleParameterStatus.Success, Assert.Single(parameter.ArrayBindStatus));
    }

    [Fact]
    public void Bind_UsePriceOverrideFlags_AreBoundAtTheirOneByteWidth()
    {
        OracleParameter flags = Find(
            LineInputBinder.Bind([PriceOverrideCase("direct-with-override"), PriceOverrideCase("direct-without-override")]),
            UsePriceOverrideName);

        Assert.Equal(new object?[] { "Y", "N" }, Plain(flags));
        Assert.Equal(new[] { 1, 1 }, flags.ArrayBindSize);
    }

    [Theory]
    [MemberData(nameof(SettableTextArrayWidths))]
    public void Bind_TextOfItsFieldWidthInUtf8Bytes_IsBoundWholeWithItsCharacterLengthAsBindSize(string arrayName, int width)
    {
        string value = new string(TwoByteCharacter, width / 2) + (width % 2 == 1 ? "x" : string.Empty);

        OracleParameter parameter = Find(LineInputBinder.Bind([WithText(Line("S1", "c-1"), arrayName, value)]), arrayName);

        Assert.Equal(width, Encoding.UTF8.GetByteCount(value));
        Assert.Equal(value, Assert.Single(Plain(parameter)));
        Assert.Equal(value.Length, Assert.Single(parameter.ArrayBindSize));
        Assert.Equal(OracleParameterStatus.Success, Assert.Single(parameter.ArrayBindStatus));
    }

    [Theory]
    [MemberData(nameof(SettableTextArrayWidths))]
    public void Bind_TextOverItsFieldWidthInCharacters_ThrowsBindingRejectionNamingTheLineAndArray(string arrayName, int width)
    {
        IReadOnlyList<InvoiceLineDraft> lines =
        [
            Line("S1", "c-1"),
            WithText(Line("S2", "c-2"), arrayName, new string('x', width + 1)),
        ];
        string expected = $"Invoice line at index 1: {arrayName} has {width + 1} characters; at most {width} can be bound.";

        ArgumentException error = Assert.Throws<ArgumentException>(() => LineInputBinder.Bind(lines));

        AssertBindingRejection(expected, error);
    }

    [Theory]
    [MemberData(nameof(SettableTextArrayWidths))]
    public void Bind_TextWithinItsFieldWidthInCharactersButOverItInUtf8Bytes_ThrowsBindingRejection(string arrayName, int width)
    {
        string value = new(TwoByteCharacter, width / 2 + 1);
        int bytes = Encoding.UTF8.GetByteCount(value);
        IReadOnlyList<InvoiceLineDraft> lines = [Line("S1", "c-1"), WithText(Line("S2", "c-2"), arrayName, value)];
        string expected = $"Invoice line at index 1: {arrayName} has {bytes} bytes in UTF-8; at most {width} can be bound.";

        ArgumentException error = Assert.Throws<ArgumentException>(() => LineInputBinder.Bind(lines));

        Assert.InRange(value.Length, 1, width);
        Assert.True(bytes > width, $"{bytes} bytes");
        AssertBindingRejection(expected, error);
    }

    [Fact]
    public void Bind_NullLineList_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LineInputBinder.Bind(null!));
    }

    [Fact]
    public void Bind_NullLine_Throws()
    {
        Assert.Throws<ArgumentException>(() => LineInputBinder.Bind([Line("S1", "c-1"), null!]));
    }

    [Fact]
    public void ClientIdBinder_NullLineList_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ClientIdBinder.Bind(null!));
    }

    [Fact]
    public void ClientIdBinder_NullLine_Throws()
    {
        Assert.Throws<ArgumentException>(() => ClientIdBinder.Bind([Line("S1", "c-1"), null!]));
    }

    [Theory]
    [InlineData("client-id-characters", "Invoice line at index 1: l_client_id has 4001 characters; at most 4000 can be bound.")]
    [InlineData("client-id-bytes", "Invoice line at index 1: l_client_id has 4002 bytes in UTF-8; at most 4000 can be bound.")]
    [InlineData("text-characters", "Invoice line at index 1: l_offer_name_snapshot has 4001 characters; at most 4000 can be bound.")]
    [InlineData("text-bytes", "Invoice line at index 1: l_offer_name_snapshot has 4002 bytes in UTF-8; at most 4000 can be bound.")]
    [InlineData("service-id-characters", "Invoice line at index 1: l_serviceid has 21 characters; at most 20 can be bound.")]
    [InlineData("teeth-no-characters", "Invoice line at index 1: l_teeth_no has 3 characters; at most 2 can be bound.")]
    [InlineData("teeth-no-bytes", "Invoice line at index 1: l_teeth_no has 4 bytes in UTF-8; at most 2 can be bound.")]
    [InlineData("discount-type-bytes", "Invoice line at index 1: l_discount_type has 2 bytes in UTF-8; at most 1 can be bound.")]
    [InlineData("package-definition-token-bytes", "Invoice line at index 1: l_package_definition_token has 66 bytes in UTF-8; at most 64 can be bound.")]
    public void BindingRejection_CarriesItsTextAndTranslatesToFormLevelFieldValidation(string rejectionCase, string expectedText)
    {
        ArgumentException error = BindingRejection(rejectionCase);

        Assert.Equal("lines", error.ParamName);
        Assert.Equal(expectedText, error.Data[OracleFailureTranslator.BindingRejectionKey]);

        DataFailure? failure = new OracleFailureTranslator().Translate(error);

        Assert.NotNull(failure);
        Assert.Equal(422, failure.Status);
        Assert.Equal("field-validation", failure.Type);
        Assert.Equal(expectedText, failure.Message);
        Assert.Null(failure.Field);
        Assert.Null(failure.Number);
        Assert.Null(failure.Package);
        Assert.Null(failure.Kind);
    }

    [Fact]
    public void BindingRejection_KeyAbsentOrOnAnotherExceptionType_IsNotTranslated()
    {
        var translator = new OracleFailureTranslator();
        ArgumentException clientIdNullLine = Assert.Throws<ArgumentException>(() => ClientIdBinder.Bind([Line("S1", "c-1"), null!]));
        ArgumentException lineNullLine = Assert.Throws<ArgumentException>(() => LineInputBinder.Bind([Line("S1", "c-1"), null!]));
        var otherType = new InvalidOperationException("Not a binding rejection.");
        otherType.Data[OracleFailureTranslator.BindingRejectionKey] = "Not a binding rejection.";

        Assert.False(clientIdNullLine.Data.Contains(OracleFailureTranslator.BindingRejectionKey));
        Assert.False(lineNullLine.Data.Contains(OracleFailureTranslator.BindingRejectionKey));
        Assert.Null(translator.Translate(clientIdNullLine));
        Assert.Null(translator.Translate(lineNullLine));
        Assert.Null(translator.Translate(otherType));
    }

    [Fact]
    public async Task BindingRejection_IsWrittenAsFieldValidationProblem()
    {
        const string expectedText = "Invoice line at index 1: l_client_id has 4001 characters; at most 4000 can be bound.";
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature
        {
            Error = BindingRejection("client-id-characters"),
            Path = "/api/invoices",
        });

        await new ProblemDetailsWriter(new OracleFailureTranslator()).WriteAsync(context);

        Assert.Equal(422, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        using JsonDocument document = await JsonDocument.ParseAsync(context.Response.Body);
        JsonElement body = document.RootElement;
        Assert.Equal("field-validation", body.GetProperty("type").GetString());
        Assert.Equal(422, body.GetProperty("status").GetInt32());
        JsonElement message = Assert.Single(body.GetProperty("messages").EnumerateArray());
        Assert.Equal(expectedText, message.GetProperty("text").GetString());
        Assert.Equal("Blocking", message.GetProperty("severity").GetString());
        Assert.Equal(JsonValueKind.Null, message.GetProperty("field").ValueKind);
        Assert.Equal(JsonValueKind.Array, body.GetProperty("openItems").ValueKind);
        Assert.Empty(body.GetProperty("openItems").EnumerateArray());
    }

    private static InvoiceLineDraft Line(string serviceId, string clientId) =>
        new() { ServiceId = serviceId, ClientId = clientId, Qty = 1m };

    private static IReadOnlyList<InvoiceLineDraft> TwoLines() =>
        [Line("S1", "c-1") with { Qty = 2m }, Line("S2", "c-2") with { Qty = 3m }];

    /// <summary>Returns the line with the field bound to the named VARCHAR2 array set to the value.</summary>
    private static InvoiceLineDraft WithText(InvoiceLineDraft line, string arrayName, string value) => arrayName switch
    {
        "l_serviceid" => line with { ServiceId = value },
        "l_discount_type" => line with { DiscountType = value },
        "l_teeth_no" => line with { TeethNo = value },
        "l_tooth_surface" => line with { ToothSurface = value },
        "l_teeth_no2" => line with { TeethNo2 = value },
        "l_approv_ref_no" => line with { ApprovRefNo = value },
        "l_claim_no" => line with { ClaimNo = value },
        "l_package_service_id" => line with { PackageServiceId = value },
        "l_package_instance_id" => line with { PackageInstanceId = value },
        "l_package_line_role" => line with { PackageLineRole = value },
        "l_package_pricing_method" => line with { PackagePricingMethod = value },
        "l_package_definition_token" => line with { PackageDefinitionToken = value },
        "l_offer_instance_id" => line with { OfferInstanceId = value },
        "l_offer_line_role" => line with { OfferLineRole = value },
        "l_offer_name_snapshot" => line with { OfferNameSnapshot = value },
        _ => throw new ArgumentOutOfRangeException(nameof(arrayName), arrayName, "Not a VARCHAR2 array bound from a line field."),
    };

    /// <summary>Converts (array name, width) pairs to theory rows.</summary>
    private static TheoryData<string, int> ToTheoryData(IEnumerable<(string Name, int Width)> widths)
    {
        var data = new TheoryData<string, int>();
        foreach ((string name, int width) in widths)
        {
            data.Add(name, width);
        }

        return data;
    }

    /// <summary>Asserts the exception is the binder's rejection of <c>lines</c> carrying exactly the expected text.</summary>
    private static void AssertBindingRejection(string expected, ArgumentException error)
    {
        Assert.Equal(new ArgumentException(expected, "lines").Message, error.Message);
        Assert.Equal("lines", error.ParamName);
        Assert.Equal(expected, error.Data[OracleFailureTranslator.BindingRejectionKey]);
    }

    private static InvoiceLineDraft PriceOverrideCase(string lineKind)
    {
        InvoiceLineDraft direct = Line("S1", "c-1") with { Price = 99m };
        InvoiceLineDraft withOverride = direct with { PriceOverride = 42.5m };

        return lineKind switch
        {
            "direct-without-override" => direct,
            "direct-flagged-without-override" => direct with { UsePriceOverride = "Y" },
            "direct-with-override" => withOverride,
            "package-parent-with-override" => withOverride with
            {
                PackageServiceId = "PKG-1",
                PackageInstanceId = "PI-1",
                PackageLineRole = "PARENT",
            },
            "package-component-with-override" => withOverride with
            {
                PackageServiceId = "PKG-1",
                PackageInstanceId = "PI-1",
                PackageLineRole = "COMPONENT",
                PackageComponentOrder = 1,
                PackageParentLineId = 1,
            },
            "package-component-padded-lowercase-with-override" => withOverride with
            {
                PackageServiceId = "PKG-1",
                PackageInstanceId = "PI-1",
                PackageLineRole = " component ",
            },
            "offer-line-with-override" => withOverride with
            {
                OfferId = 61,
                OfferDtlId = 6101,
                OfferInstanceId = "OFR-1",
                OfferLineRole = "COMPONENT",
            },
            "offer-id-only-with-override" => withOverride with { OfferId = 61 },
            "offer-role-only-with-override" => withOverride with { OfferLineRole = "PARENT" },
            _ => throw new ArgumentOutOfRangeException(nameof(lineKind), lineKind, "Unknown price-override case."),
        };
    }

    private static ArgumentException BindingRejection(string rejectionCase)
    {
        InvoiceLineDraft first = Line("S1", "c-1");
        InvoiceLineDraft second = Line("S2", "c-2");

        return rejectionCase switch
        {
            "client-id-characters" => Assert.Throws<ArgumentException>(
                () => ClientIdBinder.Bind([first, second with { ClientId = new string('x', 4001) }])),
            "client-id-bytes" => Assert.Throws<ArgumentException>(
                () => ClientIdBinder.Bind([first, second with { ClientId = new string(TwoByteCharacter, 2001) }])),
            "text-characters" => Assert.Throws<ArgumentException>(
                () => LineInputBinder.Bind([first, second with { OfferNameSnapshot = new string('x', 4001) }])),
            "text-bytes" => Assert.Throws<ArgumentException>(
                () => LineInputBinder.Bind([first, second with { OfferNameSnapshot = new string(TwoByteCharacter, 2001) }])),
            "service-id-characters" => Assert.Throws<ArgumentException>(
                () => LineInputBinder.Bind([first, second with { ServiceId = new string('S', 21) }])),
            "teeth-no-characters" => Assert.Throws<ArgumentException>(
                () => LineInputBinder.Bind([first, second with { TeethNo = "123" }])),
            "teeth-no-bytes" => Assert.Throws<ArgumentException>(
                () => LineInputBinder.Bind([first, second with { TeethNo = new string(TwoByteCharacter, 2) }])),
            "discount-type-bytes" => Assert.Throws<ArgumentException>(
                () => LineInputBinder.Bind([first, second with { DiscountType = new string(TwoByteCharacter, 1) }])),
            "package-definition-token-bytes" => Assert.Throws<ArgumentException>(
                () => LineInputBinder.Bind([first, second with { PackageDefinitionToken = new string(TwoByteCharacter, 33) }])),
            _ => throw new ArgumentOutOfRangeException(nameof(rejectionCase), rejectionCase, "Unknown binding-rejection case."),
        };
    }

    private static InvoiceLineDraft FullyPopulatedLine() => new()
    {
        ServiceId = "SV-10",
        Qty = 4m,
        PriceOverride = 42.5m,
        UsePriceOverride = "Y",
        DiscountType = "V",
        Disc = 12.5m,
        MyDisc = 7.25m,
        TeethNo = "11",
        ToothSurface = "M",
        TeethNo2 = "12",
        PatServReqRowId = 9001,
        ApprovDate = ApprovalDate,
        ApprovValidity = 30m,
        ApprovRefNo = "APR-77",
        ClaimNo = "O-5-14-140926",
        ReqNeedA = 1,
        ReqAStatus = 2,
        PackageServiceId = "PKG-1",
        PackageInstanceId = "PI-1",
        PackageLineRole = "COMPONENT",
        PackageComponentOrder = 3,
        PackageParentLineId = 501,
        PackagePricingMethod = "FIXED",
        PackageDefinitionToken = "TOKEN-ABC",
        OfferId = 61,
        OfferDtlId = 6101,
        OfferType = 2,
        OfferInstanceId = "OFR-INST-1",
        OfferLineRole = "COMPONENT",
        OfferParentLineId = 502,
        OfferPriceApplied = 80m,
        OfferDisApplied = 20m,
        OfferNameSnapshot = "Summer offer",
        OfferObjectVersionNumber = 5,
        OfferDtlObjectVersionNumber = 6,
        ClientId = "c-full",
        Price = 50m,
        CatId = 1,
        FixPay = 5m,
        PayRate = 80m,
    };

    private static string Name(OracleParameter parameter) => parameter.ParameterName.TrimStart(':');

    private static OracleParameter Find(IReadOnlyList<OracleParameter> parameters, string name) =>
        Assert.Single(parameters, parameter => Name(parameter) == name);

    private static OracleParameter[] Arrays(IReadOnlyList<OracleParameter> parameters) =>
        parameters.Where(parameter => Name(parameter).StartsWith("l_", StringComparison.Ordinal)).ToArray();

    private static bool IsNull(object? value) =>
        value is null or DBNull || value is INullable { IsNull: true };

    private static object?[] Elements(OracleParameter parameter) =>
        Assert.IsAssignableFrom<Array>(parameter.Value).Cast<object?>().ToArray();

    private static object?[] Plain(OracleParameter parameter) =>
        Elements(parameter).Select(element => Plain(element)).ToArray();

    private static object? Plain(object? element)
    {
        if (IsNull(element))
        {
            return null;
        }

        return element switch
        {
            OracleDecimal number => number.Value,
            OracleString text => text.Value,
            OracleDate date => date.Value,
            _ => element,
        };
    }
}
