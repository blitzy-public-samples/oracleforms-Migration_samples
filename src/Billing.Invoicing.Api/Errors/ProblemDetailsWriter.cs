using System.Collections;
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
    /// <summary>Exception data key holding the <see cref="IReadOnlyList{T}"/> of <see cref="MessageDto"/> written with an open-item failure.</summary>
    public const string MessagesDataKey = "Billing.Invoicing.Api.Messages";

    /// <summary>Exception data key holding the <see cref="IReadOnlyList{T}"/> of open-item ids written with an open-item failure.</summary>
    public const string OpenItemsDataKey = "Billing.Invoicing.Api.OpenItems";

    private const string ProblemJsonContentType = "application/problem+json";
    private const string FieldValidationType = "field-validation";
    private const string AboutBlankType = "about:blank";

    private const string OracleBusinessErrorTitle = "Oracle business error";
    private const string OperatorContextMissingTitle = "Operator context missing";
    private const string OpenItemTitle = "Not available in this build";
    private const string OracleUnavailableTitle = "Oracle database is unavailable";
    private const string OracleErrorTitle = "Oracle error";
    private const string FieldValidationTitle = "Validation failed";
    private const string InternalServerErrorTitle = "Internal Server Error";

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

    /// <summary>Writes the exception held by the request's <see cref="IExceptionHandlerFeature"/> as its error-contract body, or a bare 500 when it is not translated.</summary>
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
            return WriteBareServerErrorAsync(context);
        }

        Dictionary<string, object?>? body = failure.Type switch
        {
            DataFailure.OracleBusinessErrorType => OracleBusinessErrorBody(failure),
            DataFailure.OperatorContextMissingType => OperatorContextErrorBody(failure),
            DataFailure.OpenItemType => OpenItemBody(failure, error),
            DataFailure.OracleUnavailableType => Problem(failure.Type, OracleUnavailableTitle, failure.Status),
            DataFailure.OracleErrorType => OracleErrorBody(failure),
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

    /// <summary>Builds the 422 body of a package error raised for missing application, session or user context.</summary>
    /// <param name="failure">The translated failure.</param>
    /// <returns>The body members in contract order.</returns>
    private static Dictionary<string, object?> OperatorContextErrorBody(DataFailure failure)
    {
        Dictionary<string, object?> body = Problem(failure.Type, OperatorContextMissingTitle, failure.Status);
        body["oracleErrorNumber"] = failure.Number;
        body["package"] = failure.Package;
        body["message"] = failure.Message;
        return body;
    }

    /// <summary>Builds the 501 body of a blocked operation with the messages and open-item ids attached to the exception.</summary>
    /// <param name="failure">The translated failure.</param>
    /// <param name="error">The handled exception.</param>
    /// <returns>The body members in contract order.</returns>
    private static Dictionary<string, object?> OpenItemBody(DataFailure failure, Exception error)
    {
        IReadOnlyList<MessageDto>? attachedMessages = ReadData<IReadOnlyList<MessageDto>>(error, MessagesDataKey);
        IReadOnlyList<string>? attachedOpenItems = ReadData<IReadOnlyList<string>>(error, OpenItemsDataKey);

        Dictionary<string, object?> body = Problem(failure.Type, OpenItemTitle, failure.Status);
        body["openItemId"] = failure.OpenItemId;
        body["message"] = failure.Message;
        body["messages"] = attachedMessages is null ? new List<MessageDto>() : NonNull(attachedMessages);
        body["openItems"] = OpenItems(failure.OpenItemId, attachedOpenItems);
        return body;
    }

    /// <summary>Builds the 500 body of an unclassified Oracle error, carrying its number only.</summary>
    /// <param name="failure">The translated failure.</param>
    /// <returns>The body members in contract order.</returns>
    private static Dictionary<string, object?> OracleErrorBody(DataFailure failure)
    {
        Dictionary<string, object?> body = Problem(failure.Type, OracleErrorTitle, failure.Status);
        body["oracleErrorNumber"] = failure.Number;
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

    /// <summary>Reads a data value of the expected type from the exception, else from the first <see cref="NotImplementedException"/> among its inner exceptions.</summary>
    /// <typeparam name="T">Expected type of the value.</typeparam>
    /// <param name="error">The handled exception.</param>
    /// <param name="key">Data key to read.</param>
    /// <returns>The value, or null when absent or of another type.</returns>
    private static T? ReadData<T>(Exception error, string key)
        where T : class
    {
        if (TryReadData(error.Data, key, out T? value))
        {
            return value;
        }

        for (Exception? inner = error.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is NotImplementedException)
            {
                return TryReadData(inner.Data, key, out T? innerValue) ? innerValue : null;
            }
        }

        return null;
    }

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
