using System.Text.Json;
using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Api.Errors;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Domain.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Billing.Invoicing.Tests.Api;

/// <summary>Error-contract bodies <see cref="ProblemDetailsWriter"/> writes for the exception held by the exception-handler feature.</summary>
[Trait("Category", "Orchestration")]
public sealed class ProblemDetailsWriterTests
{
    private const string ProblemJson = "application/problem+json";

    private static readonly MessageDto KindMessage = new()
    {
        Field = "KIND",
        Text = "Document kind must be invoice, patient-card, barcode-sms or iqama-check.",
        Severity = ValidationMessage.Blocking,
        Rule = null,
    };

    private static readonly MessageDto NoticeMessage = new()
    {
        Field = "SERVICEID",
        Text = "S1 Rejected ",
        Severity = ValidationMessage.Warning,
        Rule = "DR-18",
    };

    /// <summary>Writes the exception through the writer and returns the status, content type and parsed body.</summary>
    /// <param name="error">Exception placed in the exception-handler feature.</param>
    /// <returns>The written status, content type and body.</returns>
    internal static async Task<(int Status, string? ContentType, JsonElement Body)> WriteHandled(Exception error)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Features.Set<IExceptionHandlerFeature>(new HandledError(error));

        await new ProblemDetailsWriter(new OracleFailureTranslator()).WriteAsync(context);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, context.Response.ContentType, document.RootElement.Clone());
    }

    private static ArgumentException Tagged(ArgumentException error, IReadOnlyList<MessageDto> messages, IReadOnlyList<string>? openItems = null)
    {
        error.Data[ProblemDetailsWriter.MessagesDataKey] = messages;
        if (openItems is not null)
        {
            error.Data[ProblemDetailsWriter.OpenItemsDataKey] = openItems;
        }

        return error;
    }

    private static string[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();

    private static string[] Members(JsonElement body) =>
        body.EnumerateObject().Select(member => member.Name).ToArray();

    private static void AssertBareServerError((int Status, string? ContentType, JsonElement Body) written)
    {
        Assert.Equal(StatusCodes.Status500InternalServerError, written.Status);
        Assert.Equal(ProblemJson, written.ContentType);
        Assert.Equal(new[] { "type", "title", "status" }, Members(written.Body));
        Assert.Equal("about:blank", written.Body.GetProperty("type").GetString());
        Assert.Equal("Internal Server Error", written.Body.GetProperty("title").GetString());
        Assert.Equal(500, written.Body.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task ArgumentExceptionWithBlockingMessages_Writes422FieldValidation()
    {
        var error = Tagged(
            new ArgumentException("Unknown document kind 'receipt'.", "kind"),
            new[] { NoticeMessage, KindMessage },
            new[] { OpenItemIds.OI47 });

        var written = await WriteHandled(error);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, written.Status);
        Assert.Equal(ProblemJson, written.ContentType);
        Assert.Equal(new[] { "type", "title", "status", "messages", "openItems" }, Members(written.Body));
        Assert.Equal("field-validation", written.Body.GetProperty("type").GetString());
        Assert.Equal("Validation failed", written.Body.GetProperty("title").GetString());
        Assert.Equal(422, written.Body.GetProperty("status").GetInt32());

        var messages = written.Body.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(2, messages.Length);
        Assert.Equal("KIND", messages[0].GetProperty("field").GetString());
        Assert.Equal(KindMessage.Text, messages[0].GetProperty("text").GetString());
        Assert.Equal(ValidationMessage.Blocking, messages[0].GetProperty("severity").GetString());
        Assert.Equal(JsonValueKind.Null, messages[0].GetProperty("rule").ValueKind);
        Assert.Equal(ValidationMessage.Warning, messages[1].GetProperty("severity").GetString());
        Assert.Equal(new[] { OpenItemIds.OI47 }, Strings(written.Body.GetProperty("openItems")));
    }

    [Fact]
    public async Task ArgumentExceptionSubclassWithBlockingMessages_Writes422WithEmptyOpenItems()
    {
        var error = Tagged(new ArgumentNullException("kind"), new[] { KindMessage });

        var written = await WriteHandled(error);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, written.Status);
        Assert.Equal("field-validation", written.Body.GetProperty("type").GetString());
        Assert.Equal("KIND", Assert.Single(written.Body.GetProperty("messages").EnumerateArray()).GetProperty("field").GetString());
        Assert.Empty(Strings(written.Body.GetProperty("openItems")));
    }

    [Fact]
    public async Task ArgumentExceptionWithoutMessages_WritesBare500()
    {
        AssertBareServerError(await WriteHandled(new ArgumentException("Draft lines must not contain null entries.", "draft")));
    }

    [Fact]
    public async Task ArgumentExceptionWithWarningsOnly_WritesBare500()
    {
        AssertBareServerError(await WriteHandled(Tagged(new ArgumentException("Notice only."), new[] { NoticeMessage })));
    }

    [Fact]
    public async Task ArgumentExceptionWithMessagesOfAnotherType_WritesBare500()
    {
        var error = new ArgumentException("Wrong data shape.");
        error.Data[ProblemDetailsWriter.MessagesDataKey] = "KIND";

        AssertBareServerError(await WriteHandled(error));
    }

    [Fact]
    public async Task OtherExceptionWithBlockingMessages_WritesBare500()
    {
        var error = new InvalidOperationException("Not a request-validation failure.");
        error.Data[ProblemDetailsWriter.MessagesDataKey] = new[] { KindMessage };

        AssertBareServerError(await WriteHandled(error));
    }

    [Theory]
    [InlineData("OI-12: BIL_MESSAGE invoice SMS is not available (OI-45 inv_small_cash.jsp).", "OI-12", new[] { "OI-12", "OI-45" })]
    [InlineData("OI-10: BIL_STOCK_POSTING store transfer is not available (OI-44 SILENT_COMMET00).", "OI-10", new[] { "OI-10", "OI-44" })]
    [InlineData("OI-47: PAT_CARD_INV.jsp barcode SMS is not available (OI-26 SEND_MESSAG).", "OI-47", new[] { "OI-47", "OI-26" })]
    public async Task NotImplementedExceptionWithAttachedOpenItems_Writes501WithSecondaryIds(
        string message,
        string primary,
        string[] attached)
    {
        var error = new NotImplementedException(message);
        error.Data[ProblemDetailsWriter.OpenItemsDataKey] = attached;

        var written = await WriteHandled(error);

        Assert.Equal(StatusCodes.Status501NotImplemented, written.Status);
        Assert.Equal(ProblemJson, written.ContentType);
        Assert.Equal("open-item", written.Body.GetProperty("type").GetString());
        Assert.Equal(primary, written.Body.GetProperty("openItemId").GetString());
        Assert.Equal(message, written.Body.GetProperty("message").GetString());
        Assert.Empty(written.Body.GetProperty("messages").EnumerateArray());
        Assert.Equal(attached, Strings(written.Body.GetProperty("openItems")));
    }

    [Fact]
    public async Task WriteNotFoundAsync_Writes404NotFoundWithTheMessage()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await new ProblemDetailsWriter(new OracleFailureTranslator()).WriteNotFoundAsync(context, "Invoice 5 was not found.");

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var body = document.RootElement;
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal(ProblemJson, context.Response.ContentType);
        Assert.Equal(new[] { "type", "title", "status", "message" }, Members(body));
        Assert.Equal("not-found", body.GetProperty("type").GetString());
        Assert.Equal("Not found", body.GetProperty("title").GetString());
        Assert.Equal(404, body.GetProperty("status").GetInt32());
        Assert.Equal("Invoice 5 was not found.", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task WriteNotFoundAsync_StartedResponse_WritesNothing()
    {
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;
        context.Features.Set<IHttpResponseFeature>(new StartedResponse());

        await new ProblemDetailsWriter(new OracleFailureTranslator()).WriteNotFoundAsync(context, "Invoice 5 was not found.");

        Assert.Equal(0, body.Length);
    }

    [Theory]
    [InlineData(404, "/api/nope", "not-found", "Not found", "No resource matches the request path.")]
    [InlineData(404, "/api/invoices//9001", "not-found", "Not found", "No resource matches the request path.")]
    [InlineData(405, "/api/invoices/1", "method-not-allowed", "Method not allowed", "The request method is not allowed for this resource.")]
    [InlineData(415, "/api/drafts/validate", "unsupported-media-type", "Unsupported media type", "The request body must be sent as application/json.")]
    [InlineData(404, "/API/Nope", "not-found", "Not found", "No resource matches the request path.")]
    [InlineData(415, "/Api/drafts/validate", "unsupported-media-type", "Unsupported media type", "The request body must be sent as application/json.")]
    [InlineData(404, "/api", "not-found", "Not found", "No resource matches the request path.")]
    public async Task StatusCodePage_ApiRefusal_WritesTheContractBody(int status, string path, string type, string title, string message)
    {
        var context = await WriteStatusCodePage(status, path);

        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal(ProblemJson, context.Response.ContentType);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var body = document.RootElement;
        Assert.Equal(new[] { "type", "title", "status", "message" }, Members(body));
        Assert.Equal(type, body.GetProperty("type").GetString());
        Assert.Equal(title, body.GetProperty("title").GetString());
        Assert.Equal(status, body.GetProperty("status").GetInt32());
        Assert.Equal(message, body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task StatusCodePage_MethodNotAllowed_KeepsTheAllowHeader()
    {
        var context = await WriteStatusCodePage(
            StatusCodes.Status405MethodNotAllowed,
            "/api/invoices/1",
            response => response.Headers.Allow = "GET, PATCH");

        Assert.Equal(StatusCodes.Status405MethodNotAllowed, context.Response.StatusCode);
        Assert.Equal("GET, PATCH", context.Response.Headers.Allow.ToString());
        Assert.Equal(ProblemJson, context.Response.ContentType);
        Assert.True(context.Response.Body.Length > 0);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(406)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task StatusCodePage_OtherStatus_WritesNothing(int status)
    {
        var context = await WriteStatusCodePage(status, "/api/invoices/1");

        Assert.Equal(status, context.Response.StatusCode);
        Assert.Null(context.Response.ContentType);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/apix/x")]
    [InlineData("/index.html")]
    [InlineData("/nope/api")]
    public async Task StatusCodePage_PathOutsideApi_WritesNothing(string path)
    {
        var context = await WriteStatusCodePage(StatusCodes.Status404NotFound, path);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Null(context.Response.ContentType);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task StatusCodePage_StartedResponse_WritesNothing()
    {
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Request.Path = "/api/nope";
        context.Response.Body = body;
        context.Features.Set<IHttpResponseFeature>(new StartedResponse { StatusCode = StatusCodes.Status404NotFound });

        await new ProblemDetailsWriter(new OracleFailureTranslator())
            .WriteAsync(new StatusCodeContext(context, new StatusCodePagesOptions(), _ => Task.CompletedTask));

        Assert.Equal(0, body.Length);
    }

    [Fact]
    public async Task StatusCodePage_NullContext_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new ProblemDetailsWriter(new OracleFailureTranslator()).WriteAsync((StatusCodeContext)null!));
    }

    /// <summary>Runs the status-code-pages overload over a bodiless response with the given status and request path.</summary>
    /// <param name="status">Status already set on the response.</param>
    /// <param name="path">Request path.</param>
    /// <param name="arrange">Sets response headers before the write; none when null.</param>
    /// <returns>The request after the write.</returns>
    private static async Task<DefaultHttpContext> WriteStatusCodePage(int status, string path, Action<HttpResponse>? arrange = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.Response.StatusCode = status;
        arrange?.Invoke(context.Response);

        await new ProblemDetailsWriter(new OracleFailureTranslator())
            .WriteAsync(new StatusCodeContext(context, new StatusCodePagesOptions(), _ => Task.CompletedTask));

        return context;
    }

    /// <summary>Response feature whose response has already started.</summary>
    private sealed class StartedResponse : HttpResponseFeature
    {
        /// <inheritdoc/>
        public override bool HasStarted => true;
    }

    /// <summary>Exception-handler feature holding the handled exception.</summary>
    /// <param name="Error">The handled exception.</param>
    private sealed record HandledError(Exception Error) : IExceptionHandlerFeature;
}
