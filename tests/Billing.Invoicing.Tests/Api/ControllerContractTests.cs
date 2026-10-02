using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Billing.Invoicing.Api.Context;
using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Api.Controllers;
using Billing.Invoicing.Api.Errors;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Domain.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;

namespace Billing.Invoicing.Tests.Api;

/// <summary>Result types, response metadata and error bodies of the five Api controllers over hand-written port fakes.</summary>
[Trait("Category", "Orchestration")]
public sealed class ControllerContractTests
{
    private const string ProblemJson = "application/problem+json";
    private const string InvalidInvoiceNumberText = "Invoice number must be a positive whole number.";
    private const string DocumentKindText = "Document kind must be invoice, patient-card, barcode-sms or iqama-check.";
    private const string RequestIdText = "Request id must be 32 upper-case hexadecimal characters.";
    private const string OfferIdText = "Offer id must be a positive whole number.";
    private const string LocalDocTypeText = "LOCAL_DOC_TYPE must be 505, 532 or 783.";
    private const string RouteNotFoundText = "No resource matches the request path.";
    private const string MethodNotAllowedText = "The request method is not allowed for this resource.";
    private const string UnsupportedMediaTypeText = "The request body must be sent as application/json.";
    private const string JsonContentType = "application/json";
    private const string FormContentType = "application/x-www-form-urlencoded";
    private const string PatientNo = "P100";

    /// <summary>2^53 + 1, the smallest positive integer a JSON number read as a double cannot hold.</summary>
    private const decimal LargeOfferId = 9007199254740993m;

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static readonly Type[] Controllers =
    [
        typeof(DraftsController),
        typeof(PatientsController),
        typeof(LookupsController),
        typeof(ImportsController),
        typeof(InvoicesController),
    ];

    private static readonly OperatorContext Operator = new()
    {
        UserNo = 1,
        UserName = "dev",
        InfoCenterId = "1",
        MachineName = "clone23",
        SessionId = "0123456789ABCDEF0123456789ABCDEF",
    };

    private static readonly DateTime DraftDate = new(2026, 9, 29, 10, 0, 0);

    /// <summary>Every controller action with its success type (null for the blocked actions) and the statuses it declares.</summary>
    public static TheoryData<string, Type?, int[]> ActionResponses { get; } = new()
    {
        { "DraftsController.New", typeof(NewDraftResponse), [200, 422, 501, 503, 500] },
        { "DraftsController.Validate", typeof(ValidateDraftResponse), [200, 422, 501, 503, 500] },
        { "PatientsController.Coverage", typeof(CoverageResponse), [200, 422, 503, 500] },
        { "LookupsController.Lov", typeof(LovResponse), [200, 404, 422, 501, 503, 500] },
        { "LookupsController.InvoiceTypes", typeof(IReadOnlyList<LookupItem>), [200, 422, 503, 500] },
        { "LookupsController.Currencies", typeof(IReadOnlyList<LookupItem>), [200, 422, 503, 500] },
        { "ImportsController.Requests", typeof(ImportResponse), [200, 422, 503, 500] },
        { "ImportsController.VisitLine", typeof(ImportResponse), [200, 422, 503, 500] },
        { "ImportsController.Package", typeof(ImportResponse), [200, 422, 501, 503, 500] },
        { "ImportsController.BundledOffer", typeof(ImportResponse), [200, 422, 503, 500] },
        { "InvoicesController.Preview", typeof(PreviewResponse), [200, 422, 501, 503, 500] },
        { "InvoicesController.Create", typeof(CreateInvoiceResponse), [201, 422, 501, 503, 500] },
        { "InvoicesController.Get", typeof(InvoiceViewResponse), [200, 404, 422, 503, 500] },
        { "InvoicesController.Last", typeof(LastInvoiceNoResponse), [200, 404, 422, 503, 500] },
        { "InvoicesController.More", typeof(MoreDetailsResponse), [200, 404, 422, 503, 500] },
        { "InvoicesController.Sms", null, [422, 501] },
        { "InvoicesController.Documents", null, [422, 501] },
        { "InvoicesController.Update", null, [422, 501] },
        { "InvoicesController.StockTransfer", null, [422, 501] },
    };

    private static IEnumerable<MethodInfo> Actions() =>
        Controllers.SelectMany(controller =>
            controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

    private static string ActionName(MethodInfo action) => $"{action.DeclaringType!.Name}.{action.Name}";

    private static string[] ContentTypes(ProducesResponseTypeAttribute response)
    {
        var contentTypes = new MediaTypeCollection();
        ((IApiResponseMetadataProvider)response).SetContentTypes(contentTypes);
        return contentTypes.ToArray();
    }

    private static DefaultHttpContext NewContext(bool withOperator = true)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        if (withOperator)
        {
            context.Items[OperatorContextMiddleware.ItemKey] = Operator;
        }

        return context;
    }

    private static T Controller<T>(FakeDataPorts fakes, HttpContext context)
        where T : ControllerBase
    {
        var controller = (T)Activator.CreateInstance(
            typeof(T), fakes.CreateService(), new ProblemDetailsWriter(new OracleFailureTranslator()))!;
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private static async Task<JsonElement> Body(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return document.RootElement.Clone();
    }

    private static string[] Members(JsonElement body) =>
        body.EnumerateObject().Select(member => member.Name).ToArray();

    private static async Task AssertInvalidInvoiceNumber(HttpContext context, FakeDataPorts fakes)
    {
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, context.Response.StatusCode);
        Assert.Equal(ProblemJson, context.Response.ContentType);
        var body = await Body(context);
        Assert.Equal("field-validation", body.GetProperty("type").GetString());
        var message = Assert.Single(body.GetProperty("messages").EnumerateArray());
        Assert.Equal("INV_NO", message.GetProperty("field").GetString());
        Assert.Equal(InvalidInvoiceNumberText, message.GetProperty("text").GetString());
        Assert.Equal(ValidationMessage.Blocking, message.GetProperty("severity").GetString());
        Assert.Empty(body.GetProperty("openItems").EnumerateArray());
        Assert.Empty(fakes.Journal);
    }

    private static async Task AssertNotFound(HttpContext context, string message)
    {
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal(ProblemJson, context.Response.ContentType);
        var body = await Body(context);
        Assert.Equal(new[] { "type", "title", "status", "message" }, Members(body));
        Assert.Equal("not-found", body.GetProperty("type").GetString());
        Assert.Equal("Not found", body.GetProperty("title").GetString());
        Assert.Equal(404, body.GetProperty("status").GetInt32());
        Assert.Equal(message, body.GetProperty("message").GetString());
    }

    private static DraftDto CashDraft() => new()
    {
        RequestId = Guid.NewGuid().ToString("N").ToUpperInvariant(),
        DraftDate = DraftDate,
        Header = new InvoiceHeaderDraft
        {
            PatientNo = PatientNo,
            InvDate = DraftDate,
            DraftDate = DraftDate,
            PayType = 1,
            SubPayType = 1,
            ClinicId = 5,
            DocId = 12,
            CompCode = "0",
            DeptWise = 0,
            Call = 0,
            DiscT = 0,
            InvNo = null,
        },
        Lines = new[] { new InvoiceLineDraft { ServiceId = "S1", Qty = 1m, DiscountType = "R", ClientId = "c1" } },
        Parameters = new InvoiceEntryParameters(),
        DiscountLimitChoice = null,
    };

    private static string DraftJson(string draftDate)
    {
        var draft = JsonSerializer.SerializeToNode(CashDraft(), WebJson)!.AsObject();
        draft["draftDate"] = draftDate;
        return draft.ToJsonString();
    }

    [Fact]
    public void ActionResponses_ListEveryControllerAction()
    {
        Assert.Equal(
            Actions().Select(ActionName).Order(StringComparer.Ordinal),
            ActionResponses.Select(row => (string)row[0]).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ActionResponses))]
    public void Action_ReturnsTypedResultAndDeclaresItsResponses(string name, Type? success, int[] statuses)
    {
        var action = Assert.Single(Actions(), method => ActionName(method) == name);
        var responses = action.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();

        Assert.Equal(typeof(Task<>), action.ReturnType.GetGenericTypeDefinition());
        var result = action.ReturnType.GetGenericArguments()[0];
        Assert.Equal(success is null ? typeof(ActionResult) : typeof(ActionResult<>).MakeGenericType(success), result);
        Assert.Equal(statuses.Order(), responses.Select(response => response.StatusCode).Order());

        foreach (var response in responses)
        {
            if (response.StatusCode < 300)
            {
                Assert.Equal(success, response.Type);
                Assert.Empty(ContentTypes(response));
            }
            else
            {
                Assert.Equal(typeof(ProblemDetails), response.Type);
                Assert.Equal(new[] { ProblemJson }, ContentTypes(response));
            }
        }
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+5")]
    [InlineData(" 5")]
    [InlineData("1.5")]
    [InlineData("99999999999999999999")]
    public async Task Get_InvalidInvoiceNumber_Writes422InvNoWithoutReads(string invNo)
    {
        var fakes = new FakeDataPorts();
        var context = NewContext();

        var result = await Controller<InvoicesController>(fakes, context).Get(invNo, new InvoiceEntryParameters());

        Assert.IsType<EmptyResult>(result.Result);
        await AssertInvalidInvoiceNumber(context, fakes);
    }

    [Theory]
    [InlineData("more")]
    [InlineData("sms")]
    [InlineData("documents")]
    [InlineData("update")]
    [InlineData("stock-transfer")]
    public async Task SavedInvoiceAction_InvalidInvoiceNumber_Writes422InvNoWithoutReads(string action)
    {
        var fakes = new FakeDataPorts();
        var context = NewContext();
        var controller = Controller<InvoicesController>(fakes, context);

        IActionResult? result = action switch
        {
            "more" => (await controller.More("abc", new InvoiceEntryParameters())).Result,
            "sms" => await controller.Sms("abc"),
            "documents" => await controller.Documents("abc", "bogus"),
            "update" => await controller.Update("abc"),
            _ => await controller.StockTransfer("abc"),
        };

        Assert.IsType<EmptyResult>(result);
        await AssertInvalidInvoiceNumber(context, fakes);
    }

    [Fact]
    public async Task Get_WithoutOperatorContext_Writes422OperatorContextMissingBeforeTheNumberCheck()
    {
        var fakes = new FakeDataPorts();
        var context = NewContext(withOperator: false);

        var result = await Controller<InvoicesController>(fakes, context).Get("abc", new InvoiceEntryParameters());

        Assert.IsType<EmptyResult>(result.Result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, context.Response.StatusCode);
        Assert.Equal("operator-context-missing", (await Body(context)).GetProperty("type").GetString());
        Assert.Empty(fakes.Journal);
    }

    [Fact]
    public async Task Get_AbsentInvoice_Writes404NotFoundNamingTheNumber()
    {
        var fakes = new FakeDataPorts();
        var context = NewContext();

        var result = await Controller<InvoicesController>(fakes, context).Get("007", new InvoiceEntryParameters());

        Assert.IsType<EmptyResult>(result.Result);
        Assert.Equal(7L, Assert.Single(fakes.Invoices.Calls).Arg<long>());
        await AssertNotFound(context, "Invoice 7 was not found.");
    }

    [Fact]
    public async Task More_AbsentInvoice_Writes404NotFoundNamingTheNumber()
    {
        var fakes = new FakeDataPorts();
        var context = NewContext();

        var result = await Controller<InvoicesController>(fakes, context).More("12", new InvoiceEntryParameters());

        Assert.IsType<EmptyResult>(result.Result);
        Assert.Equal(12L, Assert.Single(fakes.Invoices.Calls).Arg<long>());
        await AssertNotFound(context, "Invoice 12 was not found.");
    }

    [Fact]
    public async Task Last_NoInvoice_Writes404NotFound()
    {
        var fakes = new FakeDataPorts();
        var context = NewContext();

        var result = await Controller<InvoicesController>(fakes, context).Last();

        Assert.IsType<EmptyResult>(result.Result);
        await AssertNotFound(context, "No invoice exists for the operator's information centre.");
    }

    [Fact]
    public async Task Last_ReturnsTheNumberAsLastInvoiceNoResponse()
    {
        var fakes = new FakeDataPorts();
        fakes.Invoices.LastInvoiceNo = 42;

        var result = await Controller<InvoicesController>(fakes, NewContext()).Last();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var value = Assert.IsType<LastInvoiceNoResponse>(ok.Value);
        Assert.Equal(42L, value.InvNo);
        Assert.Equal("{\"invNo\":42}", JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public async Task Lov_UnknownName_Writes404NotFoundWithoutEchoingTheName()
    {
        var fakes = new FakeDataPorts();
        var context = NewContext();

        var result = await Controller<LookupsController>(fakes, context).Lov("NOPE<b>", null, null, null, null, null, null);

        Assert.IsType<EmptyResult>(result.Result);
        await AssertNotFound(context, "List of values not found.");
        Assert.Empty(fakes.Journal);
    }

    [Fact]
    public async Task Lov_Offers_ReturnsEachOferIdAsItsExactDecimalText()
    {
        var fakes = new FakeDataPorts();
        fakes.Lovs.Rows =
        [
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["OFERID"] = LargeOfferId, ["OFFER_NAME"] = "Bundle" },
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["OFERID"] = null, ["OFFER_NAME"] = "Unnumbered" },
        ];

        var result = await Controller<LookupsController>(fakes, NewContext()).Lov("OFFERS", null, null, null, null, "1", DraftDate);

        var value = Assert.IsType<LovResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("OFFERS", value.Name);
        Assert.Equal(2, value.Rows.Count);
        Assert.Equal("9007199254740993", Assert.IsType<string>(value.Rows[0]["oferid"]));
        Assert.Equal("Bundle", value.Rows[0]["OFFER_NAME"]);
        Assert.Null(value.Rows[1]["OFERID"]);
        Assert.Equal("Unnumbered", value.Rows[1]["OFFER_NAME"]);
        Assert.Equal(LargeOfferId, fakes.Lovs.Rows[0]["OFERID"]);
        var json = JsonSerializer.Serialize(value, WebJson);
        Assert.Contains("\"OFERID\":\"9007199254740993\"", json, StringComparison.Ordinal);
        Assert.Contains("\"OFERID\":null", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lov_OtherList_KeepsItsRowsAndNumbersUnchanged()
    {
        var fakes = new FakeDataPorts();
        fakes.Lovs.Rows =
        [
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["OFERID"] = LargeOfferId, ["CATID"] = 3m },
        ];

        var result = await Controller<LookupsController>(fakes, NewContext()).Lov("CAT", null, null, null, null, null, null);

        var value = Assert.IsType<LovResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Same(fakes.Lovs.Rows, value.Rows);
        Assert.Equal(LargeOfferId, Assert.IsType<decimal>(value.Rows[0]["OFERID"]));
        Assert.Contains("\"OFERID\":9007199254740993", JsonSerializer.Serialize(value, WebJson), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"offerId":"9007199254740993"}""", true)]
    [InlineData("""{"offerId":9007199254740993}""", true)]
    [InlineData("""{"OfferId":"9007199254740993"}""", false)]
    [InlineData("""{"OfferId":9007199254740993}""", false)]
    public void BundledOfferRequest_ReadsTheOfferIdExactlyFromDecimalTextOrANumber(string json, bool webDefaults)
    {
        var request = JsonSerializer.Deserialize<BundledOfferRequest>(json, webDefaults ? WebJson : new JsonSerializerOptions());

        Assert.NotNull(request);
        Assert.Equal(LargeOfferId, request.OfferId);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("9007199254740993.5")]
    public void BundledOfferRequest_RejectsAnOfferIdThatIsNotAPositiveWholeNumberOnOferId(string offerId)
    {
        var request = new BundledOfferRequest { Draft = CashDraft(), OfferId = decimal.Parse(offerId, CultureInfo.InvariantCulture) };
        var results = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true));

        var result = Assert.Single(results);
        Assert.Equal(OfferIdText, result.ErrorMessage);
        var member = Assert.Single(result.MemberNames);
        Assert.Equal("OFERID", member);
        Assert.Equal("OFERID", ModelStateFieldMap.FieldOf(member));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("5.0")]
    [InlineData("9007199254740993")]
    public void BundledOfferRequest_AcceptsAPositiveWholeOfferId(string offerId)
    {
        var request = new BundledOfferRequest { Draft = CashDraft(), OfferId = decimal.Parse(offerId, CultureInfo.InvariantCulture) };
        var results = new List<ValidationResult>();

        Assert.True(Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true));
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("receipt")]
    public async Task Documents_UnknownKind_ThrowsTheKindValidationWrittenAs422(string kind)
    {
        var fakes = new FakeDataPorts();

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => Controller<InvoicesController>(fakes, NewContext()).Documents("5", kind));
        var written = await ProblemDetailsWriterTests.WriteHandled(error);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, written.Status);
        Assert.Equal("field-validation", written.Body.GetProperty("type").GetString());
        var message = Assert.Single(written.Body.GetProperty("messages").EnumerateArray());
        Assert.Equal("KIND", message.GetProperty("field").GetString());
        Assert.Equal(DocumentKindText, message.GetProperty("text").GetString());
        Assert.Empty(fakes.Journal);
    }

    [Theory]
    [InlineData("invoice", "OI-11: ")]
    [InlineData("patient-card", "OI-47: ")]
    public async Task Documents_KnownKind_ReachesItsOpenItem(string kind, string prefix)
    {
        var error = await Assert.ThrowsAsync<NotImplementedException>(
            () => Controller<InvoicesController>(new FakeDataPorts(), NewContext()).Documents("5", kind));

        Assert.StartsWith(prefix, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_Saved_Returns201WithTheInvoiceNumber()
    {
        var fakes = new FakeDataPorts();
        fakes.Lookups.PatientCoverage = new PatientCoverageSnapshot { PatientNo = PatientNo, CompCode = "0" };
        var context = NewContext();
        var draft = CashDraft();
        draft = draft with { DraftSeal = fakes.InvoiceApi.SealDraftDate(draft.RequestId, draft.DraftDate) };

        var result = await Controller<InvoicesController>(fakes, context).Create(new CreateInvoiceRequest { Draft = draft });

        var created = Assert.IsType<CreatedResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var invoice = Assert.IsType<CreateInvoiceResponse>(created.Value);
        Assert.Equal($"/api/invoices/{invoice.InvNo.ToString(CultureInfo.InvariantCulture)}", created.Location);
        Assert.Single(fakes.CallsTo(nameof(FakeBilInvoiceApiGateway.CreateFullInvoice)));
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Theory]
    [InlineData("GET", "/api/invoices/abc", 422, "field-validation", "INV_NO")]
    [InlineData("GET", "/api/invoices/0/more", 422, "field-validation", "INV_NO")]
    [InlineData("PATCH", "/api/invoices/-3", 422, "field-validation", "INV_NO")]
    [InlineData("POST", "/api/invoices/5/documents/bogus", 422, "field-validation", "KIND")]
    [InlineData("GET", "/api/patients/P123456789012/coverage", 422, "field-validation", "PATIENTNO")]
    [InlineData("GET", "/api/invoices/5", 404, "not-found", null)]
    [InlineData("GET", "/api/lov/NOPE", 404, "not-found", null)]
    [InlineData("POST", "/api/invoices/5/sms", 501, "open-item", null)]
    public async Task Pipeline_WritesTheErrorContract(string method, string path, int status, string type, string? field)
    {
        var (written, contentType, body) = await SendAsync(new FakeDataPorts(), method, path);

        Assert.Equal(status, written);
        Assert.Equal(ProblemJson, contentType);
        Assert.Equal(type, body.GetProperty("type").GetString());
        if (field is not null)
        {
            var message = Assert.Single(body.GetProperty("messages").EnumerateArray());
            Assert.Equal(field, message.GetProperty("field").GetString());
        }
    }

    [Fact]
    public async Task Pipeline_LastRoutesToTheLastInvoiceAction()
    {
        var fakes = new FakeDataPorts();
        fakes.Invoices.LastInvoiceNo = 42;

        var (status, contentType, body) = await SendAsync(fakes, "GET", "/api/invoices/last");

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.StartsWith("application/json", contentType, StringComparison.Ordinal);
        Assert.Equal(new[] { "invNo" }, Members(body));
        Assert.Equal(42L, body.GetProperty("invNo").GetInt64());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    public async Task Pipeline_BundledOfferWithAnInvalidOfferId_IsRejectedOnOferIdWithoutReads(string offerId)
    {
        var fakes = new FakeDataPorts();

        var (status, _, body) = await SendAsync(fakes, "POST", "/api/imports/bundled-offer", $$"""{"draft":{},"offerId":"{{offerId}}"}""");

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        var error = Assert.Single(body.GetProperty("errors").EnumerateObject());
        Assert.Equal("OFERID", ModelStateFieldMap.FieldOf(error.Name));
        Assert.Equal(OfferIdText, Assert.Single(error.Value.EnumerateArray()).GetString());
        Assert.Empty(fakes.Journal);
    }

    [Theory]
    [InlineData("\"9007199254740993\"")]
    [InlineData("9007199254740993")]
    public async Task Pipeline_BundledOffer_PassesTheExactOfferIdToThePackage(string offerId)
    {
        var fakes = new FakeDataPorts();
        fakes.Lookups.PatientCoverage = new PatientCoverageSnapshot { PatientNo = PatientNo, CompCode = "0" };
        var draft = JsonSerializer.Serialize(CashDraft(), WebJson);

        var (status, _, _) = await SendAsync(fakes, "POST", "/api/imports/bundled-offer", $$"""{"draft":{{draft}},"offerId":{{offerId}},"bundleQty":"2"}""");

        Assert.Equal(StatusCodes.Status200OK, status);
        var call = Assert.Single(fakes.InvoiceApi.Calls, candidate => candidate.Method == "GetBundledOfferLines");
        Assert.Equal(LargeOfferId, Assert.IsType<decimal>(call.Args[3]));
        Assert.Equal(2m, Assert.IsType<decimal>(call.Args[4]));
    }

    [Theory]
    [Trait("Decision", "D-107")]
    [InlineData("/api/lov/OFFERS?payType=%2B1&draftDate=2026-03-31", "PAYTYPE", "PAYTYPE must be 1 (Cash) or 2 (Credit).")]
    [InlineData("/api/lov/OFFERS?payType=01&draftDate=2026-03-31", "PAYTYPE", "PAYTYPE must be 1 (Cash) or 2 (Credit).")]
    [InlineData("/api/lov/OFFERS?payType=%201&draftDate=2026-03-31", "PAYTYPE", "PAYTYPE must be 1 (Cash) or 2 (Credit).")]
    [InlineData("/api/lov/OFFERS?payType=abc&draftDate=2026-03-31", "PAYTYPE", "PAYTYPE must be 1 (Cash) or 2 (Credit).")]
    [InlineData("/api/lov/RESERV_NO?docIdx=%2012%20&patientNo=P1&draftDate=2026-03-31", "DOCIDX", "DOCIDX must be a positive whole number.")]
    [InlineData("/api/lov/RESERV_NO?docIdx=%2B12&patientNo=P1&draftDate=2026-03-31", "DOCIDX", "DOCIDX must be a positive whole number.")]
    [InlineData("/api/lov/RESERV_NO?docIdx=012&patientNo=P1&draftDate=2026-03-31", "DOCIDX", "DOCIDX must be a positive whole number.")]
    public async Task Pipeline_LovWithANonCanonicalNumericBind_Writes422OnItsItemWithoutReads(string path, string field, string text)
    {
        var fakes = new FakeDataPorts();

        var (status, contentType, body) = await SendAsync(fakes, "GET", path);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, status);
        Assert.Equal(ProblemJson, contentType);
        Assert.Equal("field-validation", body.GetProperty("type").GetString());
        var message = Assert.Single(body.GetProperty("messages").EnumerateArray());
        Assert.Equal(field, message.GetProperty("field").GetString());
        Assert.Equal(text, message.GetProperty("text").GetString());
        Assert.Equal(ValidationMessage.Blocking, message.GetProperty("severity").GetString());
        Assert.Empty(fakes.Journal);
    }

    [Theory]
    [Trait("Decision", "D-107")]
    [InlineData("/api/lov/OFFERS?payType=2&draftDate=2026-03-31", "Offers", 2)]
    [InlineData("/api/lov/RESERV_NO?docIdx=12&patientNo=P1&draftDate=2026-03-31", "ReservNo", 12)]
    public async Task Pipeline_LovWithACanonicalNumericBind_QueriesTheListWithThatNumber(string path, string method, int number)
    {
        var fakes = new FakeDataPorts();

        var (status, _, body) = await SendAsync(fakes, "GET", path);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Empty(body.GetProperty("rows").EnumerateArray());
        var call = Assert.Single(fakes.Lovs.Calls);
        Assert.Equal(method, call.Method);
        Assert.Contains(number, call.Args.OfType<int>());
    }

    [Theory]
    [Trait("Decision", "D-111")]
    [InlineData("/api/invoices/5?localDocType=783", "GetInvoice", 783)]
    [InlineData("/api/invoices/5/more?localDocType=532", "GetMoreDetails", 532)]
    [InlineData("/api/invoices/5", "GetInvoice", 505)]
    [InlineData("/api/invoices/5/more", "GetMoreDetails", 505)]
    public async Task Pipeline_SavedInvoiceRead_PassesTheQueryLocalDocTypeToTheRead(string path, string method, int localDocType)
    {
        var fakes = new FakeDataPorts();

        var (status, _, body) = await SendAsync(fakes, "GET", path);

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Equal("not-found", body.GetProperty("type").GetString());
        var call = Assert.Single(fakes.Invoices.Calls);
        Assert.Equal(method, call.Method);
        Assert.Equal(new object?[] { 5L, localDocType }, call.Args);
    }

    [Theory]
    [Trait("Decision", "D-111")]
    [InlineData("/api/invoices/5?localDocType=0")]
    [InlineData("/api/invoices/5/more?localDocType=999")]
    public async Task Pipeline_SavedInvoiceReadWithAnUnmappedLocalDocType_Writes422OnLocalDocTypeWithoutReads(string path)
    {
        var fakes = new FakeDataPorts();

        var (status, contentType, body) = await SendAsync(fakes, "GET", path);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, status);
        Assert.Equal(ProblemJson, contentType);
        Assert.Equal("field-validation", body.GetProperty("type").GetString());
        var message = Assert.Single(body.GetProperty("messages").EnumerateArray());
        Assert.Equal("LOCAL_DOC_TYPE", message.GetProperty("field").GetString());
        Assert.Equal(LocalDocTypeText, message.GetProperty("text").GetString());
        Assert.Equal(ValidationMessage.Blocking, message.GetProperty("severity").GetString());
        Assert.Empty(fakes.Journal);
    }

    [Theory]
    [Trait("Decision", "D-111")]
    [InlineData("/api/invoices/5?localDocType=abc")]
    [InlineData("/api/invoices/5/more?localDocType=abc")]
    public async Task Pipeline_SavedInvoiceReadWithANonIntegerLocalDocType_IsRejectedOnLocalDocTypeWithoutReads(string path)
    {
        var fakes = new FakeDataPorts();

        var (status, _, body) = await SendAsync(fakes, "GET", path);

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        var error = Assert.Single(body.GetProperty("errors").EnumerateObject());
        Assert.Equal("LOCAL_DOC_TYPE", ModelStateFieldMap.FieldOf(error.Name));
        Assert.Empty(fakes.Journal);
    }

    [Theory]
    [InlineData("/api/lov/OFFERS?payType=1&draftDate=2026-09-30T01:00:00%2B03:00", "2026-09-30T01:00:00+03:00")]
    [InlineData("/api/lov/OFFERS?payType=1&draftDate=2026-09-30T01:00:00Z", "2026-09-30T01:00:00Z")]
    [InlineData("/api/lov/RESERV_NO?docIdx=12&patientNo=P1&draftDate=2026-09-30T01:00:00-05:00", "2026-09-30T01:00:00-05:00")]
    [InlineData("/api/lov/OFFERS?payType=1&draftDate=2026-09-30T00:00:00%2B00:00", "2026-09-30T00:00:00+00:00")]
    [InlineData("/api/patients/P100/coverage?draftDate=2026-09-30T01:00:00%2B03:00", "2026-09-30T01:00:00+03:00")]
    [InlineData("/api/patients/P100/coverage?draftDate=2026-09-30T01:00:00Z", "2026-09-30T01:00:00Z")]
    [InlineData("/api/lov/OFFERS?payType=1&draftDate=not-a-date", "not-a-date")]
    [InlineData("/api/patients/P100/coverage?draftDate=not-a-date", "not-a-date")]
    public async Task Pipeline_DraftDateQueryWithATimeZoneDesignatorOrMalformed_IsRejectedOnInvDateWithoutReads(string path, string draftDate)
    {
        var fakes = new FakeDataPorts();

        var (status, _, body) = await SendAsync(fakes, "GET", path);

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        var error = Assert.Single(body.GetProperty("errors").EnumerateObject());
        Assert.Equal("INVDATE", ModelStateFieldMap.FieldOf(error.Name));
        Assert.Equal($"The value '{draftDate}' is not valid for draftDate.", Assert.Single(error.Value.EnumerateArray()).GetString());
        Assert.Empty(fakes.Journal);
    }

    [Theory]
    [InlineData("2026-09-30T01:00:00")]
    [InlineData("2026-03-31")]
    [InlineData("2026-09-30T01:00:00.1234567")]
    [InlineData("%202026-09-30T01:00:00%20")]
    public async Task Pipeline_LovWithAWallClockDraftDate_QueriesTheListOnThatDate(string draftDate)
    {
        var fakes = new FakeDataPorts();
        var expected = DateTime.Parse(Uri.UnescapeDataString(draftDate), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces);

        var (status, _, body) = await SendAsync(fakes, "GET", $"/api/lov/OFFERS?payType=1&draftDate={draftDate}");

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Empty(body.GetProperty("rows").EnumerateArray());
        var call = Assert.Single(fakes.Lovs.Calls);
        Assert.Equal("Offers", call.Method);
        var invDate = call.Arg<DateTime>();
        Assert.Equal(expected.Date, invDate.Date);
        Assert.Equal(DateTimeKind.Unspecified, invDate.Kind);
    }

    [Theory]
    [InlineData("?draftDate=2026-09-30T01:00:00", false)]
    [InlineData("?draftDate=2026-03-31", false)]
    [InlineData("", true)]
    [InlineData("?draftDate=", true)]
    public async Task Pipeline_CoverageWithAWallClockOrNoDraftDate_ReadsTheCoverage(string query, bool readsDatabaseTime)
    {
        var fakes = new FakeDataPorts();
        fakes.Lookups.PatientCoverage = new PatientCoverageSnapshot { PatientNo = PatientNo, CompCode = "0" };

        var (status, _, _) = await SendAsync(fakes, "GET", $"/api/patients/{PatientNo}/coverage{query}");

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal(PatientNo, Assert.Single(fakes.CallsTo(nameof(FakeLookupQueries.GetPatientCoverage))).Arg<string>());
        Assert.Equal(readsDatabaseTime, fakes.Journal.Contains($"ILookupQueries.{nameof(FakeLookupQueries.GetDatabaseTime)}"));
    }

    [Fact]
    public async Task Pipeline_LovWithAFormDraftDate_BindsTheQueryDateOnly()
    {
        var fakes = new FakeDataPorts();

        var context = await SendForContextAsync(
            fakes, "GET", "/api/lov/OFFERS?payType=1&draftDate=2026-09-30T01:00:00", "draftDate=2026-09-29T23:00:00%2B03:00", FormContentType);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        var invDate = Assert.Single(fakes.Lovs.Calls).Arg<DateTime>();
        Assert.Equal(new DateTime(2026, 9, 30), invDate.Date);
        Assert.Equal(DateTimeKind.Unspecified, invDate.Kind);
    }

    [Theory]
    [InlineData("/api/lov/OFFERS?payType=1&draftDate=2026-09-30T01:00:00%2B03:00")]
    [InlineData("/api/patients/P100/coverage?draftDate=2026-09-30T01:00:00%2B03:00")]
    public async Task Pipeline_DraftDateQueryWithADesignatorAndAValidFormDate_IsRejectedOnTheQueryValue(string path)
    {
        var fakes = new FakeDataPorts();

        var context = await SendForContextAsync(fakes, "GET", path, "draftDate=2026-09-30T01:00:00", FormContentType);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        var error = Assert.Single((await Body(context)).GetProperty("errors").EnumerateObject());
        Assert.Equal("INVDATE", ModelStateFieldMap.FieldOf(error.Name));
        Assert.Equal("The value '2026-09-30T01:00:00+03:00' is not valid for draftDate.", Assert.Single(error.Value.EnumerateArray()).GetString());
        Assert.Empty(fakes.Journal);
    }

    [Theory]
    [InlineData("?draftDate=2026-09-30T01:00:00", false)]
    [InlineData("", true)]
    public async Task Pipeline_CoverageWithAFormDraftDate_IgnoresTheFormValue(string query, bool readsDatabaseTime)
    {
        var fakes = new FakeDataPorts();
        fakes.Lookups.PatientCoverage = new PatientCoverageSnapshot { PatientNo = PatientNo, CompCode = "0" };

        var context = await SendForContextAsync(
            fakes, "GET", $"/api/patients/{PatientNo}/coverage{query}", "draftDate=2026-09-29T23:00:00%2B03:00", FormContentType);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(readsDatabaseTime, fakes.Journal.Contains($"ILookupQueries.{nameof(FakeLookupQueries.GetDatabaseTime)}"));
    }

    [Fact]
    public async Task Pipeline_LovWithOnlyAFormDraftDate_ReadsNoListDate()
    {
        var fakes = new FakeDataPorts();

        var context = await SendForContextAsync(fakes, "GET", "/api/lov/OFFERS?payType=1", "draftDate=2026-09-30T01:00:00", FormContentType);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, context.Response.StatusCode);
        Assert.Contains(
            (await Body(context)).GetProperty("messages").EnumerateArray(),
            message => message.GetProperty("field").GetString() == "INVDATE");
        Assert.Empty(fakes.Lovs.Calls);
    }


    [Theory]
    [InlineData("2026-09-29T23:59:59+03:00")]
    [InlineData("2026-09-29T23:59:59-05:00")]
    [InlineData("2026-09-29T23:59:59Z")]
    public async Task Pipeline_PreviewWithADraftDateCarryingATimeZoneDesignator_IsRejectedOnInvDateWithoutReads(string draftDate)
    {
        var fakes = new FakeDataPorts();
        fakes.Lookups.PatientCoverage = new PatientCoverageSnapshot { PatientNo = PatientNo, CompCode = "0" };

        var (status, _, body) = await SendAsync(fakes, "POST", "/api/invoices/preview", DraftJson(draftDate));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        var fields = body.GetProperty("errors").EnumerateObject().Select(error => ModelStateFieldMap.FieldOf(error.Name)).ToArray();
        Assert.Contains("INVDATE", fields);
        Assert.All(fields, field => Assert.True(field is null or "INVDATE"));
        Assert.Empty(fakes.Journal);
    }

    [Theory]
    [InlineData("2026-09-29T23:59:59")]
    [InlineData("2026-09-29T23:59:59.1234567")]
    [InlineData("2026-03-31")]
    public async Task Pipeline_PreviewWithAWallClockDraftDate_PreviewsWithThatWallClock(string draftDate)
    {
        var fakes = new FakeDataPorts();
        fakes.Lookups.PatientCoverage = new PatientCoverageSnapshot { PatientNo = PatientNo, CompCode = "0" };
        var expected = DateTime.Parse(draftDate, CultureInfo.InvariantCulture);

        var (status, _, _) = await SendAsync(fakes, "POST", "/api/invoices/preview", DraftJson(draftDate));

        Assert.Equal(StatusCodes.Status200OK, status);
        var header = Assert.Single(fakes.CallsTo(nameof(FakeBilInvoiceApiGateway.CalculatePreview))).Arg<InvoiceHeaderDraft>();
        Assert.Equal(expected.Ticks, header.DraftDate.Ticks);
        Assert.Equal(DateTimeKind.Unspecified, header.DraftDate.Kind);
        Assert.Equal(expected.Ticks, header.InvDate!.Value.Ticks);
        Assert.Equal(DateTimeKind.Unspecified, header.InvDate.Value.Kind);
    }

    [Theory]
    [InlineData("GET", "/api/nope")]
    [InlineData("GET", "/api/invoices//9001")]
    [InlineData("POST", "/API/Nope")]
    public async Task Pipeline_RouteMissUnderApi_Writes404NotFound(string method, string path)
    {
        var fakes = new FakeDataPorts();

        var context = await SendForContextAsync(fakes, method, path, null, null);

        await AssertNotFound(context, RouteNotFoundText);
        Assert.Empty(fakes.Journal);
    }

    [Theory]
    [InlineData("DELETE", "/api/invoices/1", "GET, PATCH")]
    [InlineData("PUT", "/api/invoices/1", "GET, PATCH")]
    [InlineData("GET", "/api/drafts/validate", "POST")]
    [InlineData("POST", "/api/drafts/new", "GET")]
    [InlineData("GET", "/api/invoices", "POST")]
    [InlineData("POST", "/api/lov/CAT", "GET")]
    public async Task Pipeline_MethodNotAllowed_Writes405MethodNotAllowedKeepingTheAllowHeader(string method, string path, string allow)
    {
        var fakes = new FakeDataPorts();

        var context = await SendForContextAsync(fakes, method, path, null, null);

        Assert.Equal(StatusCodes.Status405MethodNotAllowed, context.Response.StatusCode);
        Assert.Equal(allow, context.Response.Headers.Allow.ToString());
        Assert.Equal(ProblemJson, context.Response.ContentType);
        var body = await Body(context);
        Assert.Equal(new[] { "type", "title", "status", "message" }, Members(body));
        Assert.Equal("method-not-allowed", body.GetProperty("type").GetString());
        Assert.Equal("Method not allowed", body.GetProperty("title").GetString());
        Assert.Equal(405, body.GetProperty("status").GetInt32());
        Assert.Equal(MethodNotAllowedText, body.GetProperty("message").GetString());
        Assert.Empty(fakes.Journal);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    [InlineData(null)]
    public async Task Pipeline_ValidateWithANonJsonBody_Writes415UnsupportedMediaTypeWithoutTraceId(string? contentType)
    {
        var fakes = new FakeDataPorts();

        var context = await SendForContextAsync(fakes, "POST", "/api/drafts/validate", "x", contentType);

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, context.Response.StatusCode);
        Assert.Equal(ProblemJson, context.Response.ContentType);
        var body = await Body(context);
        Assert.Equal(new[] { "type", "title", "status", "message" }, Members(body));
        Assert.Equal("unsupported-media-type", body.GetProperty("type").GetString());
        Assert.Equal("Unsupported media type", body.GetProperty("title").GetString());
        Assert.Equal(415, body.GetProperty("status").GetInt32());
        Assert.Equal(UnsupportedMediaTypeText, body.GetProperty("message").GetString());
        Assert.False(body.TryGetProperty("traceId", out _));
        Assert.Empty(fakes.Journal);
    }

    [Theory]
    [InlineData("/api/invoices", "multipart/form-data; boundary=x")]
    [InlineData("/api/invoices", "multipart/form-data")]
    [InlineData("/api/drafts/validate", "multipart/form-data; boundary=x")]
    [InlineData("/api/drafts/validate", "multipart/form-data")]
    public async Task Pipeline_JsonBodyUnderAMalformedMultipartType_Writes415UnsupportedMediaTypeWithoutTheFormReaderText(string path, string contentType)
    {
        var fakes = new FakeDataPorts();
        var json = path == "/api/invoices"
            ? JsonSerializer.Serialize(new CreateInvoiceRequest { Draft = CashDraft() }, WebJson)
            : JsonSerializer.Serialize(new ValidateDraftRequest { Draft = CashDraft(), Target = "PATIENTNO" }, WebJson);

        var context = await SendForContextAsync(fakes, "POST", path, json, contentType);

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, context.Response.StatusCode);
        Assert.Equal(ProblemJson, context.Response.ContentType);
        var body = await Body(context);
        Assert.Equal(new[] { "type", "title", "status", "message" }, Members(body));
        Assert.Equal("unsupported-media-type", body.GetProperty("type").GetString());
        Assert.Equal("Unsupported media type", body.GetProperty("title").GetString());
        Assert.Equal(415, body.GetProperty("status").GetInt32());
        Assert.Equal(UnsupportedMediaTypeText, body.GetProperty("message").GetString());
        Assert.DoesNotContain("Failed to read the request form", body.GetRawText(), StringComparison.Ordinal);
        Assert.Empty(fakes.Journal);
    }

    [Theory]
    [InlineData("multipart/form-data; boundary=x")]
    [InlineData("multipart/form-data")]
    public async Task Pipeline_BodilessActionUnderAMalformedMultipartType_ReachesItsOpenItem(string contentType)
    {
        var fakes = new FakeDataPorts();

        var context = await SendForContextAsync(fakes, "POST", "/api/invoices/9001/sms", "{}", contentType);

        Assert.Equal(StatusCodes.Status501NotImplemented, context.Response.StatusCode);
        Assert.Equal(ProblemJson, context.Response.ContentType);
        var body = await Body(context);
        Assert.Equal("open-item", body.GetProperty("type").GetString());
        Assert.DoesNotContain("Failed to read the request form", body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pipeline_UnknownLov_KeepsItsOwnNotFoundMessage()
    {
        var fakes = new FakeDataPorts();

        var context = await SendForContextAsync(fakes, "GET", "/api/lov/NOPE", null, null);

        await AssertNotFound(context, "List of values not found.");
        Assert.Empty(fakes.Journal);
    }

    [Theory]
    [InlineData("GET", "/nope")]
    [InlineData("GET", "/apix/x")]
    [InlineData("DELETE", "/")]
    public async Task Pipeline_RouteMissOutsideApi_StaysAnEmpty404(string method, string path)
    {
        var context = await SendForContextAsync(new FakeDataPorts(), method, path, null, null);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Null(context.Response.ContentType);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Theory]
    [InlineData("/api/drafts/validate", 413, "content-too-large", "Content too large", "The request body is larger than the Api accepts.")]
    [InlineData("/api/invoices/preview", 413, "content-too-large", "Content too large", "The request body is larger than the Api accepts.")]
    [InlineData("/api/invoices", 413, "content-too-large", "Content too large", "The request body is larger than the Api accepts.")]
    [InlineData("/api/imports/requests", 413, "content-too-large", "Content too large", "The request body is larger than the Api accepts.")]
    [InlineData("/api/imports/visit-line", 413, "content-too-large", "Content too large", "The request body is larger than the Api accepts.")]
    [InlineData("/api/imports/package", 413, "content-too-large", "Content too large", "The request body is larger than the Api accepts.")]
    [InlineData("/api/imports/bundled-offer", 413, "content-too-large", "Content too large", "The request body is larger than the Api accepts.")]
    [InlineData("/api/drafts/validate", 400, "bad-request", "Bad request", "The request body could not be read.")]
    [InlineData("/api/invoices/preview", 400, "bad-request", "Bad request", "The request body could not be read.")]
    [InlineData("/api/invoices", 400, "bad-request", "Bad request", "The request body could not be read.")]
    [InlineData("/api/imports/requests", 400, "bad-request", "Bad request", "The request body could not be read.")]
    [InlineData("/api/imports/visit-line", 400, "bad-request", "Bad request", "The request body could not be read.")]
    [InlineData("/api/imports/package", 400, "bad-request", "Bad request", "The request body could not be read.")]
    [InlineData("/api/imports/bundled-offer", 400, "bad-request", "Bad request", "The request body could not be read.")]
    public async Task Pipeline_RefusedRequestBodyRead_WritesItsClientErrorWithoutCallingAPort(
        string path,
        int status,
        string type,
        string title,
        string message)
    {
        var fakes = new FakeDataPorts();

        var context = await SendForContextAsync(fakes, "POST", path, null, JsonContentType, new RefusedReadStream(status));

        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal(ProblemJson, context.Response.ContentType);
        var body = await Body(context);
        Assert.Equal(new[] { "type", "title", "status", "message" }, Members(body));
        Assert.Equal(type, body.GetProperty("type").GetString());
        Assert.Equal(title, body.GetProperty("title").GetString());
        Assert.Equal(status, body.GetProperty("status").GetInt32());
        Assert.Equal(message, body.GetProperty("message").GetString());
        Assert.DoesNotContain(RefusedReadStream.Marker, body.GetRawText(), StringComparison.Ordinal);
        Assert.Empty(fakes.Journal);
    }

    /// <summary>Sends one request with the operator headers and an optional JSON body through the controllers, the exception handler and the operator-context middleware, in-process.</summary>
    private static async Task<(int Status, string? ContentType, JsonElement Body)> SendAsync(FakeDataPorts fakes, string method, string path, string? json = null)
    {
        var context = await SendForContextAsync(fakes, method, path, json, json is null ? null : JsonContentType);

        return (context.Response.StatusCode, context.Response.ContentType, await Body(context));
    }

    /// <summary>Sends one request with the operator headers through the controllers, the exception handler, the status-code pages and the operator-context middleware, in-process.</summary>
    /// <param name="fakes">Port fakes behind the workflow service.</param>
    /// <param name="method">HTTP method.</param>
    /// <param name="path">Request path with an optional query string.</param>
    /// <param name="requestBody">Request body text; none when null.</param>
    /// <param name="contentType">Request content type; none when null.</param>
    /// <param name="requestStream">Request body stream, used in place of <paramref name="requestBody"/>; none when null.</param>
    /// <returns>The request after the pipeline has answered it.</returns>
    private static async Task<HttpContext> SendForContextAsync(
        FakeDataPorts fakes,
        string method,
        string path,
        string? requestBody,
        string? contentType,
        Stream? requestStream = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();
        services.AddSingleton(new DiagnosticListener(nameof(ControllerContractTests)));
        services.AddSingleton<DiagnosticSource>(provider => provider.GetRequiredService<DiagnosticListener>());
        services.AddControllers(o =>
            {
                o.ValueProviderFactories.RemoveType<FormValueProviderFactory>();
                o.ValueProviderFactories.RemoveType<JQueryFormValueProviderFactory>();
                o.ValueProviderFactories.RemoveType<FormFileValueProviderFactory>();
            })
            .ConfigureApiBehaviorOptions(o => o.SuppressMapClientErrors = true)
            .AddApplicationPart(typeof(InvoicesController).Assembly);
        services.AddSingleton(new ProblemDetailsWriter(new OracleFailureTranslator()));
        services.AddSingleton(fakes.CreateService());
        await using var provider = services.BuildServiceProvider();

        var app = new ApplicationBuilder(provider);
        app.UseRouting();
        app.UseExceptionHandler(handler => handler.Run(context =>
            context.RequestServices.GetRequiredService<ProblemDetailsWriter>().WriteAsync(context)));
        app.UseStatusCodePages(status => status.HttpContext.RequestServices.GetRequiredService<ProblemDetailsWriter>().WriteAsync(status));
        app.UseMiddleware<OperatorContextMiddleware>();
        app.UseEndpoints(endpoints => endpoints.MapControllers());
        RequestDelegate pipeline = app.Build();

        await using var scope = provider.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = method;
        var query = path.IndexOf('?', StringComparison.Ordinal);
        context.Request.Path = query < 0 ? path : path[..query];
        context.Request.QueryString = query < 0 ? QueryString.Empty : new QueryString(path[query..]);
        context.Request.Headers["X-His-User-No"] = "1";
        context.Request.Headers["X-His-User-Name"] = "dev";
        context.Request.Headers["X-His-Info-Center-Id"] = "1";
        context.Request.Headers["X-His-Machine"] = "clone23";
        context.Request.Headers["X-His-Session-Id"] = Operator.SessionId;
        context.Response.Body = new MemoryStream();
        if (contentType is not null)
        {
            context.Request.ContentType = contentType;
        }

        if (requestStream is not null)
        {
            context.Request.Body = requestStream;
        }
        else if (requestBody is not null)
        {
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));
        }

        await pipeline(context);

        return context;
    }

    [Fact]
    public async Task Create_MalformedRequestId_Writes422RequestIdWithoutReads()
    {
        var fakes = new FakeDataPorts();
        var context = NewContext();
        var request = new CreateInvoiceRequest { Draft = CashDraft() with { RequestId = "not-a-request-id" } };

        var result = await Controller<InvoicesController>(fakes, context).Create(request);

        Assert.IsType<EmptyResult>(result.Result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, context.Response.StatusCode);
        var body = await Body(context);
        Assert.Equal("field-validation", body.GetProperty("type").GetString());
        var message = Assert.Single(body.GetProperty("messages").EnumerateArray());
        Assert.Equal("REQUEST_ID", message.GetProperty("field").GetString());
        Assert.Equal(RequestIdText, message.GetProperty("text").GetString());
        Assert.Empty(fakes.Journal);
    }

    /// <summary>Request body whose every read throws a <see cref="BadHttpRequestException"/> with the given status, as the server does for an oversized or misframed body.</summary>
    /// <param name="status">Status the refusal carries.</param>
    private sealed class RefusedReadStream(int status) : MemoryStream
    {
        /// <summary>Text carried in the refusal message.</summary>
        public const string Marker = "SECRET-BODY-TEXT";

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count) => throw Refusal();

        /// <inheritdoc/>
        public override int Read(Span<byte> buffer) => throw Refusal();

        /// <inheritdoc/>
        public override int ReadByte() => throw Refusal();

        /// <inheritdoc/>
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromException<int>(Refusal());

        /// <inheritdoc/>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(Refusal());

        /// <summary>Builds the refusal the server raises for the body.</summary>
        /// <returns>The refusal.</returns>
        private BadHttpRequestException Refusal() => new($"Request body refused. {Marker}", status);
    }
}
