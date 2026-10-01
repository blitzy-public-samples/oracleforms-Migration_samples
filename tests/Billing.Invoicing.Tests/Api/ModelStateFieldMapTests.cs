using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Billing.Invoicing.Api.Errors;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Tests.Api;

/// <summary>Legacy item names, upper-snake fallbacks and form-level nulls that <see cref="ModelStateFieldMap.FieldOf"/> returns for JSON, query, route and member model-state keys.</summary>
[Trait("Category", "Orchestration")]
public sealed class ModelStateFieldMapTests
{
    private const string SolutionFileName = "SmallCashInvoice.sln";
    private const string FormsNamespace = "http://xmlns.oracle.com/Forms";
    private const string HeaderPathPrefix = "$.draft.header.";
    private const string LinePathPrefix = "$.draft.lines[0].";

    private static readonly Regex DraftItemRow = new(
        @"^\| Item\\\|(?<block>T_INV|D_INV|TOOL)\\\|(?<item>[A-Z0-9_]+)\\\|",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

    private static readonly Regex DraftPropertyReference = new(
        @"\b(?<type>InvoiceHeaderDraft|InvoiceLineDraft)\.(?<property>[A-Za-z0-9]+)\b",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

    /// <summary>Maps each key shape the input formatters, model binders and validators produce to its legacy item, or to null when form-level.</summary>
    /// <param name="key">Model-state key.</param>
    /// <param name="expected">Expected legacy item name.</param>
    [Theory]
    [InlineData("$.draft.header.patientNo", "PATIENTNO")]
    [InlineData("$.patientNo", "PATIENTNO")]
    [InlineData("$['patientNo']", "PATIENTNO")]
    [InlineData("$.draft['header'][\"patientNo\"]", "PATIENTNO")]
    [InlineData("$.draft.draftDate", "INVDATE")]
    [InlineData("$.draft.header.invDate", "INVDATE")]
    [InlineData("$.draft.lines[0].qty", "QTY")]
    [InlineData("$[0].qty", "QTY")]
    [InlineData("Draft.Lines[2].Qty", "QTY")]
    [InlineData("[2].Qty", "QTY")]
    [InlineData("$.draft.lines[3].serviceId", "SERVICEID")]
    [InlineData("$[3].serviceId", "SERVICEID")]
    [InlineData("$.draft.lines[0].priceOverride", "PRICE")]
    [InlineData("$.draft.lines[0].discountType", "LDISCT")]
    [InlineData("$.draft.lines[0].clientId", "CLIENT_ID")]
    [InlineData("$.draft.lines[0].packageServiceId", "IMP_FROM_PKG")]
    [InlineData("$.draft.lines[0].offerId", "OFFER_ID")]
    [InlineData("$.draft.lines[1]", "LINE")]
    [InlineData("$[1]", "LINE")]
    [InlineData("$.draft.lines", "LINE")]
    [InlineData("lineIndex", "LINE")]
    [InlineData("$.lineIndex", "LINE")]
    [InlineData("payType", "PAYTYPE")]
    [InlineData("docIdx", "DOCIDX")]
    [InlineData("draftDate", "INVDATE")]
    [InlineData("patientNo", "PATIENTNO")]
    [InlineData("compCode", "COMP_CODE")]
    [InlineData("subCompCode", "SUB_COMP_CODE")]
    [InlineData("invNo", "INV_NO")]
    [InlineData("kind", "KIND")]
    [InlineData("$.target", "TARGET")]
    [InlineData("$.draft.requestId", "REQUEST_ID")]
    [InlineData("Draft.RequestId", "REQUEST_ID")]
    [InlineData("$.draft.header.amount1", "AMOUNT_1")]
    [InlineData("$.draft.header.subPayType2", "SUB_PAYTYPE2")]
    [InlineData("$.packageServiceId", "SERVICEID")]
    [InlineData("$.offerId", "OFERID")]
    [InlineData("Draft.DISC_T", "DISC_T")]
    [InlineData("FINALDISC_PERC", "FINALDISC_PERC")]
    [InlineData("Draft.AMOUNT_1", "AMOUNT_1")]
    [InlineData("CashOrCredit", "CASH_OR_CREDIT")]
    [InlineData("parameters.CashOrCredit", "CASH_OR_CREDIT")]
    [InlineData("X422ApprovCheck", "X422_APPROV_CHECK")]
    [InlineData("$.draft.parameters.x422ApprovCheck", "X422_APPROV_CHECK")]
    [InlineData("$", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    [InlineData("request", null)]
    [InlineData("draft", null)]
    [InlineData("$.draft", null)]
    [InlineData("$.draft.header", null)]
    [InlineData("$.draft.parameters", null)]
    [InlineData("parameters", null)]
    [InlineData("$['a b']", null)]
    [InlineData("$['']", null)]
    [InlineData("$.draft.header.", null)]
    public void FieldOf_maps_model_state_keys_to_legacy_items(string? key, string? expected) =>
        Assert.Equal(expected, ModelStateFieldMap.FieldOf(key));

    /// <summary>Every public property of <see cref="InvoiceEntryParameters"/>, however it is bound, maps to a ModuleParameter name of the Form.</summary>
    [Fact]
    public void FieldOf_maps_every_entry_parameter_to_its_module_parameter()
    {
        var moduleParameters = LoadModuleParameterNames();
        var properties = typeof(InvoiceEntryParameters).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.Equal(moduleParameters.Count, properties.Length);

        var mapped = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            var item = ModelStateFieldMap.FieldOf(property.Name);
            Assert.NotNull(item);
            Assert.Contains(item, moduleParameters);
            Assert.Equal(item, ModelStateFieldMap.FieldOf("parameters." + property.Name));
            Assert.Equal(item, ModelStateFieldMap.FieldOf("$.draft.parameters." + JsonNamingPolicy.CamelCase.ConvertName(property.Name)));
            mapped.Add(item);
        }

        Assert.Equal(moduleParameters.OrderBy(name => name, StringComparer.Ordinal), mapped.OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>Each header or line property a §9 forward Item row names maps, under its JSON path, to that row's legacy item.</summary>
    [Fact]
    public void FieldOf_maps_every_matrix_draft_property_to_its_item()
    {
        var path = Path.Combine(FindRepositoryRoot(), "docs", "legacy-form-spec.md");
        var checkedBlocks = new HashSet<string>(StringComparer.Ordinal);
        var mismatches = new List<string>();

        foreach (var line in File.ReadLines(path))
        {
            var row = DraftItemRow.Match(line);
            if (!row.Success)
            {
                continue;
            }

            var item = row.Groups["item"].Value;
            foreach (Match reference in DraftPropertyReference.Matches(line))
            {
                var prefix = reference.Groups["type"].Value == nameof(InvoiceHeaderDraft) ? HeaderPathPrefix : LinePathPrefix;
                var key = prefix + JsonNamingPolicy.CamelCase.ConvertName(reference.Groups["property"].Value);
                var actual = ModelStateFieldMap.FieldOf(key);
                if (actual != item)
                {
                    mismatches.Add($"{key} -> {actual ?? "null"}, expected {item}");
                }

                checkedBlocks.Add(row.Groups["block"].Value);
            }
        }

        Assert.Equal(new[] { "D_INV", "TOOL", "T_INV" }, checkedBlocks.Order(StringComparer.Ordinal));
        Assert.Empty(mismatches);
    }

    /// <summary>Reads the ModuleParameter names of the Form XML export.</summary>
    /// <returns>The distinct parameter names.</returns>
    private static HashSet<string> LoadModuleParameterNames()
    {
        var path = Path.Combine(FindRepositoryRoot(), "05_Complex", "Inv_Small_Cash.xml");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Form XML export not found.", path);
        }

        XNamespace ns = FormsNamespace;
        var names = XDocument.Load(path)
            .Descendants(ns + "ModuleParameter")
            .Select(element => (string?)element.Attribute("Name")
                ?? throw new InvalidDataException($"{path}: ModuleParameter without a Name attribute."))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(names);
        return names;
    }

    /// <summary>Returns the nearest directory at or above the test output directory that holds the solution file.</summary>
    /// <returns>The repository root path.</returns>
    private static string FindRepositoryRoot()
    {
        var start = AppContext.BaseDirectory;
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No directory containing {SolutionFileName} was found at or above {start}.");
    }
}
