using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Domain.Workflow;

namespace Billing.Invoicing.Tests.Domain;

/// <summary>Unit tests of <see cref="OpenItemGate.Evaluate"/>.</summary>
[Trait("Category", "DomainUnit")]
public sealed class OpenItemGateTests
{
    private static InvoiceHeaderDraft Header(int? payType = 1, string? subCompCode = null, string? preAuthorization = null) =>
        new() { PayType = payType, SubCompCode = subCompCode, PreAuthorization = preAuthorization };

    private static ServiceProfile Service(
        string serviceId = "100",
        int? addToQue = 0,
        int? consRev = 0,
        int? isPackage = 0,
        int? pkgType = null,
        int? servLocId = null,
        params ServiceProfile[] components) =>
        new()
        {
            ServiceId = serviceId,
            AddToQue = addToQue,
            ConsRev = consRev,
            IsPackage = isPackage,
            PkgType = pkgType,
            ServLocId = servLocId,
            Components = components,
        };

    private static InvoiceEntryParameters Params(long? pkgInv = null) => new() { PkgInv = pkgInv };

    private static IReadOnlyList<string> Eval(
        InvoiceHeaderDraft header,
        IEnumerable<ServiceProfile> services,
        int? cardId = null,
        decimal? maxDeductable = null,
        int? useAdvanced = null,
        InvoiceEntryParameters? parameters = null) =>
        OpenItemGate.Evaluate(header, services, cardId, maxDeductable, useAdvanced, parameters ?? Params());

    [Fact]
    public void Clean_draft_returns_no_open_items()
    {
        Assert.Empty(Eval(Header(), [Service()]));
    }

    [Fact]
    public void Draft_without_lines_returns_no_open_items()
    {
        Assert.Empty(Eval(Header(payType: 2, subCompCode: "10"), []));
    }

    [Fact]
    [Trait("OpenItem", "OI-32")]
    public void Queued_service_with_sub_company_returns_OI32()
    {
        Assert.Equal(new[] { OpenItemIds.OI32 }, Eval(Header(subCompCode: "10"), [Service(addToQue: 1)]));
    }

    [Fact]
    [Trait("OpenItem", "OI-32")]
    public void Queued_service_without_sub_company_returns_no_OI32()
    {
        Assert.DoesNotContain(OpenItemIds.OI32, Eval(Header(), [Service(addToQue: 1)]));
    }

    [Theory]
    [Trait("OpenItem", "OI-32")]
    [InlineData(1)]
    [InlineData(2)]
    public void Claim_or_revisit_limit_returns_OI32(int consRev)
    {
        Assert.Equal(new[] { OpenItemIds.OI32 }, Eval(Header(), [Service(consRev: consRev)]));
    }

    [Fact]
    [Trait("OpenItem", "OI-32")]
    public void Other_cons_rev_value_returns_no_open_items()
    {
        Assert.Empty(Eval(Header(), [Service(consRev: 3)]));
    }

    [Fact]
    [Trait("OpenItem", "OI-32")]
    public void Advance_instalment_package_returns_OI32()
    {
        Assert.Equal(new[] { OpenItemIds.OI32 }, Eval(Header(), [Service(isPackage: 1, pkgType: 3)]));
    }

    [Fact]
    [Trait("OpenItem", "OI-32")]
    public void Package_of_other_type_returns_no_open_items()
    {
        Assert.Empty(Eval(Header(), [Service(isPackage: 1, pkgType: 1)]));
    }

    [Fact]
    [Trait("OpenItem", "OI-32")]
    public void Queued_package_component_with_sub_company_returns_OI32()
    {
        var package = Service("900", isPackage: 1, pkgType: 1, servLocId: 14, components: [Service("901"), Service("902", addToQue: 1)]);

        Assert.Equal(new[] { OpenItemIds.OI32 }, Eval(Header(subCompCode: "10"), [package]));
    }

    [Fact]
    [Trait("OpenItem", "OI-32")]
    public void Component_with_revisit_limit_returns_OI32()
    {
        var package = Service("900", isPackage: 1, servLocId: 14, components: [Service("901", consRev: 2)]);

        Assert.Equal(new[] { OpenItemIds.OI32 }, Eval(Header(), [package]));
    }

    [Fact]
    [Trait("OpenItem", "OI-32")]
    public void Nested_advance_instalment_component_returns_OI32()
    {
        var inner = Service("910", isPackage: 1, pkgType: 3);
        var package = Service("900", isPackage: 1, servLocId: 14, components: [Service("901", components: [inner])]);

        Assert.Equal(new[] { OpenItemIds.OI32 }, Eval(Header(), [package]));
    }

    [Fact]
    [Trait("OpenItem", "OI-32")]
    public void Package_with_unqueued_components_and_sub_company_returns_no_open_items()
    {
        var package = Service("900", isPackage: 1, servLocId: 14, components: [Service("901"), Service("902")]);

        Assert.Empty(Eval(Header(subCompCode: "10"), [package]));
    }

    [Fact]
    [Trait("OpenItem", "OI-32")]
    public void Patient_card_returns_OI32()
    {
        Assert.Equal(new[] { OpenItemIds.OI32 }, Eval(Header(), [Service()], cardId: 5));
    }

    [Theory]
    [Trait("OpenItem", "OI-32")]
    [InlineData(null)]
    [InlineData("PA-123")]
    public void D52_preauthorization_is_ignored(string? preAuthorization)
    {
        var header = Header(subCompCode: "10", preAuthorization: preAuthorization);

        Assert.Equal(new[] { OpenItemIds.OI32 }, Eval(header, [Service(addToQue: 1)]));
    }

    [Fact]
    [Trait("OpenItem", "OI-32")]
    public void D52_server_read_card_returns_OI32_when_header_carries_none()
    {
        Assert.Equal(new[] { OpenItemIds.OI32 }, Eval(Header(payType: 2), [Service()], cardId: 77));
    }

    [Fact]
    [Trait("OpenItem", "OI-31")]
    public void Package_consumption_mode_returns_OI31()
    {
        Assert.Equal(new[] { OpenItemIds.OI31 }, Eval(Header(), [Service()], parameters: Params(pkgInv: 42)));
    }

    [Fact]
    [Trait("OpenItem", "OI-23")]
    public void D51_credit_with_deductible_returns_OI23()
    {
        Assert.Contains(OpenItemIds.OI23, Eval(Header(payType: 2), [Service()], maxDeductable: 50m));
    }

    [Fact]
    [Trait("OpenItem", "OI-23")]
    public void D51_credit_with_advanced_class_returns_OI23()
    {
        Assert.Contains(OpenItemIds.OI23, Eval(Header(payType: 2), [Service()], maxDeductable: 0m, useAdvanced: 2));
    }

    [Fact]
    [Trait("OpenItem", "OI-23")]
    public void D51_credit_without_deductible_or_advanced_class_returns_no_OI23()
    {
        Assert.DoesNotContain(OpenItemIds.OI23, Eval(Header(payType: 2), [Service()], maxDeductable: 0m, useAdvanced: 1));
    }

    [Fact]
    [Trait("OpenItem", "OI-23")]
    public void D51_cash_with_deductible_returns_no_OI23()
    {
        Assert.DoesNotContain(OpenItemIds.OI23, Eval(Header(payType: 1), [Service()], maxDeductable: 50m, useAdvanced: 2));
    }

    [Fact]
    public void Combined_conditions_return_each_id_once_in_ordinal_order()
    {
        var header = Header(payType: 2, subCompCode: "10");
        var services = new[] { Service("100", addToQue: 1), Service("200", consRev: 1) };

        var result = Eval(header, services, cardId: 5, maxDeductable: 10m, parameters: Params(pkgInv: 7));

        var expected = new[] { OpenItemIds.OI32, OpenItemIds.OI31, OpenItemIds.OI23 };
        Assert.Equal(expected.OrderBy(id => id, StringComparer.Ordinal), result);
        Assert.DoesNotContain(OpenItemIds.OI33, result);
    }

    [Fact]
    public void Null_header_or_parameters_throw()
    {
        Assert.Throws<ArgumentNullException>(() => OpenItemGate.Evaluate(null!, [], null, null, null, Params()));
        Assert.Throws<ArgumentNullException>(() => OpenItemGate.Evaluate(Header(), [], null, null, null, null!));
    }
}
