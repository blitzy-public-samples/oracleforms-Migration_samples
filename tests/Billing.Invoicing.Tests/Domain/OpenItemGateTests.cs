using System.Collections;
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
        Assert.Empty(Eval(Header(), [Service(addToQue: 1)]));
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

    /// <summary>A two-profile cycle and a self-loop without any condition return no open items, each component list read once.</summary>
    [Fact]
    [Trait("OpenItem", "OI-32")]
    public void Cyclic_component_graph_without_condition_is_traversed_once_and_returns_no_open_items()
    {
        var aComponents = new CountingComponents();
        var bComponents = new CountingComponents();
        var cComponents = new CountingComponents();
        var a = Service("900", isPackage: 1, servLocId: 14) with { Components = aComponents };
        var b = Service("901", isPackage: 1, servLocId: 14) with { Components = bComponents };
        var c = Service("902", isPackage: 1, servLocId: 14) with { Components = cComponents };
        aComponents.Add(b);
        bComponents.Add(a);
        cComponents.Add(c);

        Assert.Empty(Eval(Header(subCompCode: "10"), [a, c]));
        Assert.All(new[] { aComponents, bComponents, cComponents }, components => Assert.Equal(components.Count, components.Reads));
    }

    /// <summary>A revisit-limit component reached only after a component cycle returns OI-32.</summary>
    [Fact]
    [Trait("OpenItem", "OI-32")]
    public void Revisit_limit_component_after_component_cycle_returns_OI32()
    {
        var aComponents = new CountingComponents();
        var bComponents = new CountingComponents();
        var a = Service("900", isPackage: 1, servLocId: 14) with { Components = aComponents };
        var b = Service("901", isPackage: 1, servLocId: 14) with { Components = bComponents };
        var d = Service("903", consRev: 2);
        aComponents.Add(b);
        aComponents.Add(d);
        bComponents.Add(a);

        Assert.Equal(new[] { OpenItemIds.OI32 }, Eval(Header(), [a]));
    }

    /// <summary>A profile referenced by two packages, twice by one and again as a draft line, is traversed once.</summary>
    [Fact]
    [Trait("OpenItem", "OI-32")]
    public void Duplicate_component_references_are_traversed_once_and_return_no_open_items()
    {
        var sharedComponents = new CountingComponents();
        var shared = Service("910", isPackage: 1, servLocId: 14) with { Components = sharedComponents };
        sharedComponents.Add(Service("911"));
        var first = Service("900", isPackage: 1, servLocId: 14, components: [shared, shared]);
        var second = Service("920", isPackage: 1, servLocId: 14, components: [shared]);

        Assert.Empty(Eval(Header(subCompCode: "10"), [first, shared, second]));
        Assert.Equal(sharedComponents.Count, sharedComponents.Reads);
    }

    /// <summary>A claim-limit component referenced twice and again as a draft line returns OI-32 once.</summary>
    [Fact]
    [Trait("OpenItem", "OI-32")]
    public void Duplicate_claim_limit_component_references_return_OI32_once()
    {
        var limited = Service("930", consRev: 1);
        var package = Service("900", isPackage: 1, servLocId: 14, components: [limited, limited]);

        Assert.Equal(new[] { OpenItemIds.OI32 }, Eval(Header(), [package, limited]));
    }

    /// <summary>A null service profile and a null package component are skipped and return no open items.</summary>
    [Fact]
    [Trait("OpenItem", "OI-32")]
    public void Null_service_and_null_component_return_no_open_items()
    {
        var package = Service("900", isPackage: 1, servLocId: 14, components: [null!, Service("901")]);

        Assert.Empty(Eval(Header(subCompCode: "10"), [null!, package]));
    }

    /// <summary>A queued component after a null sibling component, under a sub-company, returns OI-32.</summary>
    [Fact]
    [Trait("OpenItem", "OI-32")]
    public void Queued_component_after_null_component_with_sub_company_returns_OI32()
    {
        var package = Service("900", isPackage: 1, servLocId: 14, components: [null!, Service("901", addToQue: 1)]);

        Assert.Equal(new[] { OpenItemIds.OI32 }, Eval(Header(subCompCode: "10"), [null!, package]));
    }

    /// <summary>A profile whose component list is null returns no open items.</summary>
    [Fact]
    [Trait("OpenItem", "OI-32")]
    public void Profile_with_null_components_returns_no_open_items()
    {
        var package = Service("900", isPackage: 1, servLocId: 14) with { Components = null! };

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
        Assert.Empty(Eval(Header(payType: 2), [Service()], maxDeductable: 0m, useAdvanced: 1));
    }

    [Fact]
    [Trait("OpenItem", "OI-23")]
    public void D51_cash_with_deductible_returns_no_OI23()
    {
        Assert.Empty(Eval(Header(payType: 1), [Service()], maxDeductable: 50m, useAdvanced: 2));
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

    /// <summary>Component list that counts element reads and throws once they exceed a fixed limit.</summary>
    private sealed class CountingComponents : IReadOnlyList<ServiceProfile>
    {
        private const int ReadLimit = 64;
        private readonly List<ServiceProfile> _components = [];

        /// <summary>Elements read so far, through the indexer or enumeration.</summary>
        public int Reads { get; private set; }

        /// <inheritdoc />
        public int Count => _components.Count;

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">More than the read limit of elements has been read.</exception>
        public ServiceProfile this[int index]
        {
            get
            {
                Reads++;
                if (Reads > ReadLimit)
                {
                    throw new InvalidOperationException(
                        $"The component graph was traversed without end: more than {ReadLimit} component reads.");
                }

                return _components[index];
            }
        }

        /// <summary>Appends a component.</summary>
        /// <param name="component">Component profile.</param>
        public void Add(ServiceProfile component) => _components.Add(component);

        /// <inheritdoc />
        public IEnumerator<ServiceProfile> GetEnumerator()
        {
            for (var index = 0; index < _components.Count; index++)
            {
                yield return this[index];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
