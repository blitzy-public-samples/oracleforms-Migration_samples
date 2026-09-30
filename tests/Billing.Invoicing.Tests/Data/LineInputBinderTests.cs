using System.Data;
using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Domain.Model;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

namespace Billing.Invoicing.Tests.Data;

/// <summary>Associative-array binding tests for <see cref="LineInputBinder"/> and <see cref="ClientIdBinder"/>.</summary>
[Trait("Category", "DataUnit")]
public sealed class LineInputBinderTests
{
    private const string LineCountName = "line_count";
    private const string ClientIdName = "l_client_id";

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

    private static InvoiceLineDraft Line(string serviceId, string clientId) =>
        new() { ServiceId = serviceId, ClientId = clientId, Qty = 1m };

    private static IReadOnlyList<InvoiceLineDraft> TwoLines() =>
        [Line("S1", "c-1") with { Qty = 2m }, Line("S2", "c-2") with { Qty = 3m }];

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
