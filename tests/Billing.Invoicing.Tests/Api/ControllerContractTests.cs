using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
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
    private const string PatientNo = "P100";

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
            "more" => (await controller.More("abc")).Result,
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

        var result = await Controller<InvoicesController>(fakes, context).More("12");

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
        fakes.InvoiceApi.InvoiceNo = 4321;
        var context = NewContext();
        var draft = CashDraft();
        draft = draft with { DraftSeal = fakes.InvoiceApi.SealDraftDate(draft.RequestId, draft.DraftDate) };

        var result = await Controller<InvoicesController>(fakes, context).Create(new CreateInvoiceRequest { Draft = draft });

        var created = Assert.IsType<CreatedResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal("/api/invoices/4321", created.Location);
        var invoice = Assert.IsType<CreateInvoiceResponse>(created.Value);
        Assert.Equal(4321L, invoice.InvNo);
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

    /// <summary>Sends one request with the operator headers through the controllers, the exception handler and the operator-context middleware, in-process.</summary>
    private static async Task<(int Status, string? ContentType, JsonElement Body)> SendAsync(FakeDataPorts fakes, string method, string path)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();
        services.AddSingleton(new DiagnosticListener(nameof(ControllerContractTests)));
        services.AddSingleton<DiagnosticSource>(provider => provider.GetRequiredService<DiagnosticListener>());
        services.AddControllers().AddApplicationPart(typeof(InvoicesController).Assembly);
        services.AddSingleton(new ProblemDetailsWriter(new OracleFailureTranslator()));
        services.AddSingleton(fakes.CreateService());
        await using var provider = services.BuildServiceProvider();

        var app = new ApplicationBuilder(provider);
        app.UseRouting();
        app.UseExceptionHandler(handler => handler.Run(context =>
            context.RequestServices.GetRequiredService<ProblemDetailsWriter>().WriteAsync(context)));
        app.UseMiddleware<OperatorContextMiddleware>();
        app.UseEndpoints(endpoints => endpoints.MapControllers());
        RequestDelegate pipeline = app.Build();

        await using var scope = provider.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Headers["X-His-User-No"] = "1";
        context.Request.Headers["X-His-User-Name"] = "dev";
        context.Request.Headers["X-His-Info-Center-Id"] = "1";
        context.Request.Headers["X-His-Machine"] = "clone23";
        context.Request.Headers["X-His-Session-Id"] = Operator.SessionId;
        context.Response.Body = new MemoryStream();

        await pipeline(context);

        return (context.Response.StatusCode, context.Response.ContentType, await Body(context));
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
}
