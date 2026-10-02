using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Domain.Model;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Billing.Invoicing.Api.Errors;

/// <summary>Writes failures and validation results as <c>application/problem+json</c> bodies, carrying the <c>MESSAG</c> texts with field, severity and rule.</summary>
public sealed class ProblemDetailsWriter
{
    /// <summary>Exception data key holding the <see cref="IReadOnlyList{T}"/> of <see cref="MessageDto"/> written with an open-item or request-validation failure.</summary>
    public const string MessagesDataKey = "Billing.Invoicing.Api.Messages";

    /// <summary>Exception data key holding the <see cref="IReadOnlyList{T}"/> of open-item ids written with an open-item failure.</summary>
    public const string OpenItemsDataKey = "Billing.Invoicing.Api.OpenItems";

    private const string ProblemJsonContentType = "application/problem+json";
    private const string FieldValidationType = "field-validation";
    private const string NotFoundType = "not-found";
    private const string MethodNotAllowedType = "method-not-allowed";
    private const string UnsupportedMediaTypeType = "unsupported-media-type";
    private const string ContentTooLargeType = "content-too-large";
    private const string BadRequestType = "bad-request";
    private const string AboutBlankType = "about:blank";
    private const string ApiPathPrefix = "/api";

    private const string OracleBusinessErrorTitle = "Oracle business error";
    private const string OperatorContextMissingTitle = "Operator context missing";
    private const string OpenItemTitle = "Not available in this build";
    private const string OracleUnavailableTitle = "Oracle database is unavailable";
    private const string OracleErrorTitle = "Oracle error";
    private const string FieldValidationTitle = "Validation failed";
    private const string NotFoundTitle = "Not found";
    private const string MethodNotAllowedTitle = "Method not allowed";
    private const string UnsupportedMediaTypeTitle = "Unsupported media type";
    private const string ContentTooLargeTitle = "Content too large";
    private const string BadRequestTitle = "Bad request";
    private const string InternalServerErrorTitle = "Internal Server Error";

    private const string RouteNotFoundMessage = "No resource matches the request path.";
    private const string MethodNotAllowedMessage = "The request method is not allowed for this resource.";
    private const string UnsupportedMediaTypeMessage = "The request body must be sent as application/json.";
    private const string ContentTooLargeMessage = "The request body is larger than the Api accepts.";
    private const string BadRequestMessage = "The request body could not be read.";

    /// <summary>Web-default serializer options with string enums, relaxed escaping and dictionary keys written as given.</summary>
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private readonly OracleFailureTranslator _translator;

    /// <summary>Creates a writer that classifies handled exceptions with the given translator.</summary>
    /// <param name="translator">Translator of Data-layer exceptions into failures.</param>
    public ProblemDetailsWriter(OracleFailureTranslator translator)
    {
        ArgumentNullException.ThrowIfNull(translator);
        _translator = translator;
    }

    /// <summary>Writes the exception held by the request's <see cref="IExceptionHandlerFeature"/> as its error-contract body: a 422 <c>field-validation</c> for an <see cref="ArgumentException"/> carrying blocking messages, a 413 <c>content-too-large</c> or 4xx <c>bad-request</c> for a refused request-body read, a bare 500 for any other untranslated exception.</summary>
    /// <param name="context">The failed request.</param>
    /// <returns>A task that completes when the body is written, or at once when the response has started.</returns>
    public Task WriteAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        Exception? error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (error is null)
        {
            return WriteBareServerErrorAsync(context);
        }

        DataFailure? failure = _translator.Translate(error);
        if (failure is null)
        {
            // A request body the server refused to read is answered with a fixed message, never the exception text.
            if (error is BadHttpRequestException refusal)
            {
                return WriteBodyReadRefusalAsync(context, refusal.StatusCode);
            }

            return TryReadRequestValidation(error, out IReadOnlyList<MessageDto>? messages, out IReadOnlyList<string> openItems)
                ? WriteAsync(context, messages, openItems)
                : WriteBareServerErrorAsync(context);
        }

        Dictionary<string, object?>? body = failure.Type switch
        {
            DataFailure.OracleBusinessErrorType => OracleBusinessErrorBody(failure),
            DataFailure.OperatorContextMissingType => OperatorContextErrorBody(failure),
            DataFailure.OpenItemType => OpenItemBody(failure, error),
            DataFailure.OracleUnavailableType => Problem(failure.Type, OracleUnavailableTitle, failure.Status),
            DataFailure.OracleErrorType => OracleErrorBody(failure),
            DataFailure.FieldValidationType => FieldValidationBody(failure),
            _ => null,
        };

        return body is null
            ? WriteBareServerErrorAsync(context)
            : WriteBodyAsync(context, failure.Status, body);
    }

    /// <summary>Writes a 422 <c>field-validation</c> body with blocking messages first, the open-item ids and, when given, the adjusted values.</summary>
    /// <param name="context">The request to answer.</param>
    /// <param name="messages">Blocking and warning messages, each written with its legacy text unchanged.</param>
    /// <param name="openItems">Open-item ids written as given.</param>
    /// <param name="adjusted">Adjusted values keyed by legacy item name; omitted when null.</param>
    /// <returns>A task that completes when the body is written, or at once when the response has started.</returns>
    public Task WriteAsync(
        HttpContext context,
        IReadOnlyList<MessageDto> messages,
        IReadOnlyList<string> openItems,
        IReadOnlyDictionary<string, object?>? adjusted = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(openItems);

        if (context.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        Dictionary<string, object?> body = Problem(FieldValidationType, FieldValidationTitle, StatusCodes.Status422UnprocessableEntity);
        body["messages"] = BlockingFirst(messages);
        body["openItems"] = NonNull(openItems);

        if (adjusted is not null)
        {
            var values = new Dictionary<string, object?>(adjusted.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> entry in adjusted)
            {
                values[entry.Key] = entry.Value;
            }

            body["adjusted"] = values;
        }

        return WriteBodyAsync(context, StatusCodes.Status422UnprocessableEntity, body);
    }

    /// <summary>Writes a 422 <c>operator-context-missing</c> body naming the absent operator-context headers.</summary>
    /// <param name="context">The request to answer.</param>
    /// <param name="missingHeaders">Names of the absent headers, written as given.</param>
    /// <returns>A task that completes when the body is written, or at once when the response has started.</returns>
    public Task WriteAsync(HttpContext context, IReadOnlyList<string> missingHeaders)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(missingHeaders);

        if (context.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        Dictionary<string, object?> body = Problem(
            DataFailure.OperatorContextMissingType,
            OperatorContextMissingTitle,
            StatusCodes.Status422UnprocessableEntity);
        body["missing"] = NonNull(missingHeaders);

        return WriteBodyAsync(context, StatusCodes.Status422UnprocessableEntity, body);
    }

    /// <summary>Writes a 404 <c>not-found</c> body carrying the message.</summary>
    /// <param name="context">The request to answer.</param>
    /// <param name="message">Text naming what was not found, written as given.</param>
    /// <returns>A task that completes when the body is written, or at once when the response has started.</returns>
    public Task WriteNotFoundAsync(HttpContext context, string message)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);

        if (context.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        Dictionary<string, object?> body = Problem(NotFoundType, NotFoundTitle, StatusCodes.Status404NotFound);
        body["message"] = message;

        return WriteBodyAsync(context, StatusCodes.Status404NotFound, body);
    }

    /// <summary>Writes the bodiless 404, 405 or 415 the framework left on an <c>/api</c> request as a <c>not-found</c>, <c>method-not-allowed</c> or <c>unsupported-media-type</c> body, keeping the status and headers.</summary>
    /// <param name="statusCodeContext">The status-code-pages context of the request.</param>
    /// <returns>A task that completes when the body is written, or at once for another status, a path outside <c>/api</c> or a started response.</returns>
    public Task WriteAsync(StatusCodeContext statusCodeContext)
    {
        ArgumentNullException.ThrowIfNull(statusCodeContext);

        HttpContext context = statusCodeContext.HttpContext;
        if (context.Response.HasStarted
            || !context.Request.Path.StartsWithSegments(ApiPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        int status = context.Response.StatusCode;
        (string Type, string Title, string Message)? refusal = status switch
        {
            StatusCodes.Status404NotFound => (NotFoundType, NotFoundTitle, RouteNotFoundMessage),
            StatusCodes.Status405MethodNotAllowed => (MethodNotAllowedType, MethodNotAllowedTitle, MethodNotAllowedMessage),
            StatusCodes.Status415UnsupportedMediaType => (UnsupportedMediaTypeType, UnsupportedMediaTypeTitle, UnsupportedMediaTypeMessage),
            _ => null,
        };

        if (refusal is not { } written)
        {
            return Task.CompletedTask;
        }

        Dictionary<string, object?> body = Problem(written.Type, written.Title, status);
        body["message"] = written.Message;

        return WriteBodyAsync(context, status, body);
    }

    /// <summary>Builds the 422 body of an Oracle application error; field, legacy text and kind only when set.</summary>
    /// <param name="failure">The translated failure.</param>
    /// <returns>The body members in contract order.</returns>
    private static Dictionary<string, object?> OracleBusinessErrorBody(DataFailure failure)
    {
        Dictionary<string, object?> body = Problem(failure.Type, OracleBusinessErrorTitle, failure.Status);
        body["oracleErrorNumber"] = failure.Number;
        body["package"] = failure.Package;
        body["message"] = failure.Message;
        AddWhenSet(body, "field", failure.Field);
        AddWhenSet(body, "legacyText", failure.LegacyText);
        AddWhenSet(body, "kind", failure.Kind);
        return body;
    }

    /// <summary>Builds the 422 body of a package error raised for missing application, session or user context; kind only when set.</summary>
    /// <param name="failure">The translated failure.</param>
    /// <returns>The body members in contract order.</returns>
    private static Dictionary<string, object?> OperatorContextErrorBody(DataFailure failure)
    {
        Dictionary<string, object?> body = Problem(failure.Type, OperatorContextMissingTitle, failure.Status);
        body["oracleErrorNumber"] = failure.Number;
        body["package"] = failure.Package;
        body["message"] = failure.Message;
        AddWhenSet(body, "kind", failure.Kind);
        return body;
    }

    /// <summary>Builds the 501 body of a blocked operation with the messages and open-item ids attached to the exception.</summary>
    /// <param name="failure">The translated failure.</param>
    /// <param name="error">The handled exception.</param>
    /// <returns>The body members in contract order, or null when the failure names no open item.</returns>
    private static Dictionary<string, object?>? OpenItemBody(DataFailure failure, Exception error)
    {
        if (failure.OpenItemId is not { } openItemId)
        {
            return null;
        }

        IReadOnlyList<MessageDto>? attachedMessages = ReadData<IReadOnlyList<MessageDto>>(error, openItemId, MessagesDataKey);
        IReadOnlyList<string>? attachedOpenItems = ReadData<IReadOnlyList<string>>(error, openItemId, OpenItemsDataKey);

        Dictionary<string, object?> body = Problem(failure.Type, OpenItemTitle, failure.Status);
        body["openItemId"] = openItemId;
        body["message"] = failure.Message;
        body["messages"] = attachedMessages is null ? new List<MessageDto>() : NonNull(attachedMessages);
        body["openItems"] = OpenItems(openItemId, attachedOpenItems);
        return body;
    }

    /// <summary>Builds the 500 body of an unclassified Oracle error, carrying its number only, or the fixed message when it has no number.</summary>
    /// <param name="failure">The translated failure.</param>
    /// <returns>The body members in contract order.</returns>
    private static Dictionary<string, object?> OracleErrorBody(DataFailure failure)
    {
        Dictionary<string, object?> body = Problem(failure.Type, OracleErrorTitle, failure.Status);
        body["oracleErrorNumber"] = failure.Number;
        if (failure.Number is null)
        {
            body["message"] = failure.Message;
        }

        return body;
    }

    /// <summary>Builds the 422 <c>field-validation</c> body of a request value the Data layer refused, as one blocking message.</summary>
    /// <param name="failure">The translated failure.</param>
    /// <returns>The body members in contract order.</returns>
    private static Dictionary<string, object?> FieldValidationBody(DataFailure failure)
    {
        Dictionary<string, object?> body = Problem(FieldValidationType, FieldValidationTitle, failure.Status);
        body["messages"] = new List<MessageDto>
        {
            new() { Field = failure.Field, Text = failure.Message, Severity = ValidationMessage.Blocking },
        };
        body["openItems"] = new List<string>();
        return body;
    }

    /// <summary>Returns the attached open-item ids, with the failure's id prepended when it is set and not already listed.</summary>
    /// <param name="openItemId">Open-item id of the failure; null when the message names none.</param>
    /// <param name="attached">Open-item ids attached to the exception; null when absent.</param>
    /// <returns>The ids to write.</returns>
    private static List<string> OpenItems(string? openItemId, IReadOnlyList<string>? attached)
    {
        List<string> ids = attached is null ? new List<string>() : NonNull(attached);
        if (openItemId is not null && !ids.Contains(openItemId, StringComparer.Ordinal))
        {
            ids.Insert(0, openItemId);
        }

        return ids;
    }

    /// <summary>Reads a data value of the expected type from the exception, else from the first <see cref="NotImplementedException"/> in its chain whose message names the open item.</summary>
    /// <typeparam name="T">Expected type of the value.</typeparam>
    /// <param name="error">The handled exception.</param>
    /// <param name="openItemId">Open-item id of the failure.</param>
    /// <param name="key">Data key to read.</param>
    /// <returns>The value, or null when absent or of another type.</returns>
    private static T? ReadData<T>(Exception error, string openItemId, string key)
        where T : class
    {
        if (TryReadData(error.Data, key, out T? value))
        {
            return value;
        }

        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is NotImplementedException && NamesOpenItem(current.Message, openItemId))
            {
                return TryReadData(current.Data, key, out T? openItemValue) ? openItemValue : null;
            }
        }

        return null;
    }

    /// <summary>Reads the messages and open-item ids attached to an <see cref="ArgumentException"/> raised for invalid request input.</summary>
    /// <param name="error">The handled exception.</param>
    /// <param name="messages">The attached messages when at least one is blocking.</param>
    /// <param name="openItems">The attached open-item ids; empty when absent.</param>
    /// <returns>True when the exception is an <see cref="ArgumentException"/> whose attached messages hold a blocking message.</returns>
    private static bool TryReadRequestValidation(
        Exception error,
        [NotNullWhen(true)] out IReadOnlyList<MessageDto>? messages,
        out IReadOnlyList<string> openItems)
    {
        messages = null;
        openItems = Array.Empty<string>();
        if (error is not ArgumentException
            || !TryReadData(error.Data, MessagesDataKey, out IReadOnlyList<MessageDto>? attachedMessages)
            || attachedMessages is null
            || !attachedMessages.Any(message => message is not null
                && string.Equals(message.Severity, ValidationMessage.Blocking, StringComparison.Ordinal)))
        {
            return false;
        }

        if (TryReadData(error.Data, OpenItemsDataKey, out IReadOnlyList<string>? attachedOpenItems) && attachedOpenItems is not null)
        {
            openItems = attachedOpenItems;
        }

        messages = attachedMessages;
        return true;
    }

    /// <summary>Returns whether a message starts with the open-item id followed by its end or a non-digit.</summary>
    /// <param name="message">Exception message.</param>
    /// <param name="openItemId">Open-item id such as OI-11.</param>
    /// <returns>True when the message names the open item.</returns>
    private static bool NamesOpenItem(string message, string openItemId) =>
        message.StartsWith(openItemId, StringComparison.Ordinal)
        && (message.Length == openItemId.Length || !char.IsAsciiDigit(message[openItemId.Length]));

    /// <summary>Reads a data value of the expected type.</summary>
    /// <typeparam name="T">Expected type of the value.</typeparam>
    /// <param name="data">Exception data to read.</param>
    /// <param name="key">Data key to read.</param>
    /// <param name="value">The value when present with the expected type.</param>
    /// <returns>True when the value is present with the expected type.</returns>
    private static bool TryReadData<T>(IDictionary data, string key, out T? value)
        where T : class
    {
        value = data.Contains(key) ? data[key] as T : null;
        return value is not null;
    }

    /// <summary>Returns the messages with blocking ones first, keeping input order within blocking and non-blocking messages.</summary>
    /// <param name="messages">Messages to order.</param>
    /// <returns>The ordered messages, without null entries.</returns>
    private static List<MessageDto> BlockingFirst(IReadOnlyList<MessageDto> messages)
    {
        var blocking = new List<MessageDto>(messages.Count);
        var others = new List<MessageDto>();
        foreach (MessageDto? message in messages)
        {
            if (message is null)
            {
                continue;
            }

            if (string.Equals(message.Severity, ValidationMessage.Blocking, StringComparison.Ordinal))
            {
                blocking.Add(message);
            }
            else
            {
                others.Add(message);
            }
        }

        blocking.AddRange(others);
        return blocking;
    }

    /// <summary>Copies a list without its null entries, keeping order.</summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="items">Items to copy.</param>
    /// <returns>The copied items.</returns>
    private static List<T> NonNull<T>(IReadOnlyList<T> items)
        where T : class
    {
        var copy = new List<T>(items.Count);
        foreach (T? item in items)
        {
            if (item is not null)
            {
                copy.Add(item);
            }
        }

        return copy;
    }

    /// <summary>Adds a member only when its value is set.</summary>
    /// <param name="body">Body to extend.</param>
    /// <param name="name">Member name.</param>
    /// <param name="value">Member value.</param>
    private static void AddWhenSet(Dictionary<string, object?> body, string name, string? value)
    {
        if (value is not null)
        {
            body[name] = value;
        }
    }

    /// <summary>Starts a body with its <c>type</c>, <c>title</c> and <c>status</c> members.</summary>
    /// <param name="type">Error-contract type.</param>
    /// <param name="title">Short fixed title.</param>
    /// <param name="status">HTTP status.</param>
    /// <returns>A body whose members serialize in insertion order.</returns>
    private static Dictionary<string, object?> Problem(string type, string title, int status) => new(StringComparer.Ordinal)
    {
        ["type"] = type,
        ["title"] = title,
        ["status"] = status,
    };

    /// <summary>Writes a 500 body carrying no exception detail.</summary>
    /// <param name="context">The request to answer.</param>
    /// <returns>A task that completes when the body is written.</returns>
    private static Task WriteBareServerErrorAsync(HttpContext context) => WriteBodyAsync(
        context,
        StatusCodes.Status500InternalServerError,
        Problem(AboutBlankType, InternalServerErrorTitle, StatusCodes.Status500InternalServerError));

    /// <summary>Writes a 413 <c>content-too-large</c> body, or a <c>bad-request</c> body with the refusal's 4xx status (else 400), each with a fixed message.</summary>
    /// <param name="context">The request to answer.</param>
    /// <param name="refusalStatus">Status carried by the request-body refusal.</param>
    /// <returns>A task that completes when the body is written.</returns>
    private static Task WriteBodyReadRefusalAsync(HttpContext context, int refusalStatus)
    {
        (string Type, string Title, int Status, string Message) refusal = refusalStatus switch
        {
            StatusCodes.Status413PayloadTooLarge => (ContentTooLargeType, ContentTooLargeTitle, refusalStatus, ContentTooLargeMessage),
            >= StatusCodes.Status400BadRequest and < StatusCodes.Status500InternalServerError => (BadRequestType, BadRequestTitle, refusalStatus, BadRequestMessage),
            _ => (BadRequestType, BadRequestTitle, StatusCodes.Status400BadRequest, BadRequestMessage),
        };

        Dictionary<string, object?> body = Problem(refusal.Type, refusal.Title, refusal.Status);
        body["message"] = refusal.Message;

        return WriteBodyAsync(context, refusal.Status, body);
    }

    /// <summary>Sets the status and problem-json content type, then serializes the body to the response.</summary>
    /// <param name="context">The request to answer.</param>
    /// <param name="status">HTTP status.</param>
    /// <param name="body">Body members in contract order.</param>
    /// <returns>A task that completes when the body is written.</returns>
    private static async Task WriteBodyAsync(HttpContext context, int status, Dictionary<string, object?> body)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = ProblemJsonContentType;
        await JsonSerializer.SerializeAsync(context.Response.Body, body, Options, context.RequestAborted);
    }

    /// <summary>Creates the read-only serializer options shared by every body.</summary>
    /// <returns>The options.</returns>
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DictionaryKeyPolicy = null,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
