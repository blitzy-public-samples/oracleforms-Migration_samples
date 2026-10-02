using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Api.Errors;
using Billing.Invoicing.Domain.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;

namespace Billing.Invoicing.Tests.Api;

/// <summary>Legacy item names, upper-snake fallbacks and form-level nulls that <see cref="ModelStateFieldMap.FieldOf"/> returns for JSON, query, route and member model-state keys, and the 422 messages <see cref="ModelStateFieldMap.MessagesOf"/> builds from model state.</summary>
[Trait("Category", "Orchestration")]
public sealed class ModelStateFieldMapTests
{
    private const string SolutionFileName = "SmallCashInvoice.sln";
    private const string FormsNamespace = "http://xmlns.oracle.com/Forms";
    private const string HeaderPathPrefix = "$.draft.header.";
    private const string LinePathPrefix = "$.draft.lines[0].";
    private const string InvalidInputText = "The input was not valid.";
    private const string RequestRequiredText = "The request field is required.";
    private const string DraftRequiredText = "The draft field is required.";
    private const string EmptyBodyText = "A non-empty request body is required.";

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

    /// <summary>A body value the formatter cannot read yields its own field message only, without the body parameter's required error.</summary>
    [Fact]
    public void MessagesOf_drops_the_body_parameter_required_error_beside_a_path_error()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("request", RequestRequiredText);
        modelState.TryAddModelException("$.draft.lines[0].approvDate", new JsonException());

        var messages = ModelStateFieldMap.MessagesOf(Context(modelState, BodyParameter("request")));

        Assert.Equal(new MessageDto { Field = "APPROV_DATE", Text = InvalidInputText, Severity = ValidationMessage.Blocking }, Assert.Single(messages));
    }

    /// <summary>An empty or null body yields the formatter's form-level message only, without the body parameter's required error.</summary>
    [Fact]
    public void MessagesOf_drops_the_body_parameter_required_error_beside_a_form_level_error()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError(string.Empty, EmptyBodyText);
        modelState.AddModelError("draft", DraftRequiredText);

        var messages = ModelStateFieldMap.MessagesOf(Context(modelState, BodyParameter("draft")));

        Assert.Equal(new MessageDto { Field = null, Text = EmptyBodyText, Severity = ValidationMessage.Blocking }, Assert.Single(messages));
    }

    /// <summary>The body parameter's required error is kept when model state holds no other error.</summary>
    [Fact]
    public void MessagesOf_keeps_the_body_parameter_required_error_when_it_is_the_only_error()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("request", RequestRequiredText);

        var messages = ModelStateFieldMap.MessagesOf(Context(modelState, BodyParameter("request")));

        Assert.Equal(new MessageDto { Field = null, Text = RequestRequiredText, Severity = ValidationMessage.Blocking }, Assert.Single(messages));
    }

    /// <summary>A required member inside the body, such as a null <c>draft</c> on create, is kept: its key is not the body parameter's.</summary>
    [Fact]
    public void MessagesOf_keeps_a_nested_required_member_error()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Draft", "The Draft field is required.");

        var messages = ModelStateFieldMap.MessagesOf(Context(modelState, BodyParameter("request")));

        Assert.Equal(new MessageDto { Field = null, Text = "The Draft field is required.", Severity = ValidationMessage.Blocking }, Assert.Single(messages));
    }

    /// <summary>The body parameter's key is matched without regard to case, as model-state keys are.</summary>
    [Theory]
    [InlineData("REQUEST")]
    [InlineData("Request")]
    public void MessagesOf_matches_the_body_parameter_key_case_insensitively(string key)
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError(key, RequestRequiredText);
        modelState.TryAddModelException("$.draft.draftDate", new JsonException());

        var messages = ModelStateFieldMap.MessagesOf(Context(modelState, BodyParameter("request")));

        Assert.Equal(new MessageDto { Field = "INVDATE", Text = InvalidInputText, Severity = ValidationMessage.Blocking }, Assert.Single(messages));
    }

    /// <summary>A body parameter bound under a model name is keyed by that name, not by the parameter name.</summary>
    [Fact]
    public void MessagesOf_keys_a_body_parameter_by_its_binder_model_name()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("payload", "The payload field is required.");
        modelState.AddModelError("request", "Request text.");
        modelState.TryAddModelException("$.target", new JsonException());

        var messages = ModelStateFieldMap.MessagesOf(Context(modelState, BodyParameter("request", "payload")));

        Assert.Equal(2, messages.Length);
        Assert.Contains(new MessageDto { Field = null, Text = "Request text.", Severity = ValidationMessage.Blocking }, messages);
        Assert.Contains(new MessageDto { Field = "TARGET", Text = InvalidInputText, Severity = ValidationMessage.Blocking }, messages);
    }

    /// <summary>Errors of a parameter bound from another source are kept, in model-state order, with every error of a key.</summary>
    [Fact]
    public void MessagesOf_keeps_every_error_of_a_non_body_parameter_in_model_state_order()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("parameters", "First.");
        modelState.AddModelError("parameters", "Second.");
        modelState.AddModelError("invNo", "Third.");
        var query = new ParameterDescriptor
        {
            Name = "parameters",
            ParameterType = typeof(InvoiceEntryParameters),
            BindingInfo = new BindingInfo { BindingSource = BindingSource.Query },
        };

        var expected = modelState
            .SelectMany(entry => entry.Value!.Errors.Select(error => (Field: ModelStateFieldMap.FieldOf(entry.Key), Text: error.ErrorMessage)))
            .ToArray();

        var messages = ModelStateFieldMap.MessagesOf(Context(modelState, query, BodyParameter("request")));

        Assert.Equal(3, expected.Length);
        Assert.Equal(expected, messages.Select(message => (message.Field, message.Text)));
        Assert.Equal(new[] { "First.", "Second." }, messages.Where(message => message.Field is null).Select(message => message.Text));
        Assert.All(messages, message => Assert.Equal(ValidationMessage.Blocking, message.Severity));
    }

    /// <summary>An error without text is reported as 'The input was not valid.'.</summary>
    [Fact]
    public void MessagesOf_reports_an_error_without_text_as_invalid_input()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("$.draft.lines[0].qty", string.Empty);

        var messages = ModelStateFieldMap.MessagesOf(Context(modelState, BodyParameter("request")));

        Assert.Equal(new MessageDto { Field = "QTY", Text = InvalidInputText, Severity = ValidationMessage.Blocking }, Assert.Single(messages));
    }

    /// <summary>Model state without errors yields no message, and a null context is refused.</summary>
    [Fact]
    public void MessagesOf_returns_no_message_for_valid_model_state_and_refuses_a_null_context()
    {
        var modelState = new ModelStateDictionary();
        modelState.SetModelValue("request", "value", "value");

        Assert.Empty(ModelStateFieldMap.MessagesOf(Context(modelState, BodyParameter("request"))));
        Assert.Throws<ArgumentNullException>(() => ModelStateFieldMap.MessagesOf(null!));
    }

    /// <summary>Builds the context of an action with the given parameters and model state.</summary>
    /// <param name="modelState">Model state of the request.</param>
    /// <param name="parameters">Action parameters.</param>
    /// <returns>The action context.</returns>
    private static ActionContext Context(ModelStateDictionary modelState, params ParameterDescriptor[] parameters) =>
        new(new DefaultHttpContext(), new RouteData(), new ActionDescriptor { Parameters = parameters }, modelState);

    /// <summary>Builds a parameter bound from the request body.</summary>
    /// <param name="name">Parameter name.</param>
    /// <param name="binderModelName">Model name the parameter binds under; none when null.</param>
    /// <returns>The parameter descriptor.</returns>
    private static ParameterDescriptor BodyParameter(string name, string? binderModelName = null) => new()
    {
        Name = name,
        ParameterType = typeof(object),
        BindingInfo = new BindingInfo { BindingSource = BindingSource.Body, BinderModelName = binderModelName },
    };

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
