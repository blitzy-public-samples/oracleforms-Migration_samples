using Billing.Invoicing.Data.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Billing.Invoicing.Api.Errors;

/// <summary>Answers a handled exception with the <see cref="ProblemDetailsWriter"/> error-contract body and logs only its redacted metadata.</summary>
public sealed partial class ProblemDetailsExceptionHandler : IExceptionHandler
{
    private const string TypeChainSeparator = " -> ";

    private readonly ProblemDetailsWriter _writer;
    private readonly OracleFailureTranslator _translator;
    private readonly ILogger<ProblemDetailsExceptionHandler> _logger;

    /// <summary>Creates a handler that writes with the given writer and classifies with the given translator.</summary>
    /// <param name="writer">Writer of the error-contract body.</param>
    /// <param name="translator">Translator of Data-layer exceptions into failures.</param>
    /// <param name="logger">Logger receiving the redacted metadata.</param>
    public ProblemDetailsExceptionHandler(
        ProblemDetailsWriter writer,
        OracleFailureTranslator translator,
        ILogger<ProblemDetailsExceptionHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(logger);
        _writer = writer;
        _translator = translator;
        _logger = logger;
    }

    /// <summary>Writes the exception's error-contract body, or an empty 500 when writing fails, and logs its metadata without message text.</summary>
    /// <param name="httpContext">The failed request.</param>
    /// <param name="exception">The handled exception.</param>
    /// <param name="cancellationToken">Token of the request.</param>
    /// <returns>False when the response has already started; otherwise true.</returns>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        IExceptionHandlerFeature feature = HandledFeature(httpContext, exception);
        string method = httpContext.Request.Method;
        string? routeTemplate = RouteTemplate(feature.Endpoint);
        string exceptionTypes = TypeChain(exception);

        DataFailure? failure;
        try
        {
            failure = _translator.Translate(exception);
            await _writer.WriteAsync(httpContext);
        }
        catch (Exception fault)
        {
            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.Clear();
                httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            }

            LogWriterFailed(
                _logger,
                method,
                routeTemplate,
                httpContext.Response.StatusCode,
                exceptionTypes,
                TypeChain(fault),
                httpContext.TraceIdentifier);
            return true;
        }

        int status = httpContext.Response.StatusCode;
        LogLevel level = LevelOf(status);
        if (level == LogLevel.Error)
        {
            LogServerFault(
                _logger,
                method,
                routeTemplate,
                status,
                failure?.Type,
                failure?.Number,
                failure?.Package,
                failure?.Kind,
                failure?.OpenItemId,
                exceptionTypes,
                httpContext.TraceIdentifier,
                exception.StackTrace);
        }
        else
        {
            LogAnswered(
                _logger,
                level,
                method,
                routeTemplate,
                status,
                failure?.Type,
                failure?.Number,
                failure?.Package,
                failure?.Kind,
                failure?.OpenItemId,
                exceptionTypes,
                httpContext.TraceIdentifier);
        }

        return true;
    }

    /// <summary>Returns the request's exception-handler feature, first setting one that holds the exception when it is absent or holds another.</summary>
    /// <param name="httpContext">The failed request.</param>
    /// <param name="exception">The handled exception.</param>
    /// <returns>The feature holding the exception.</returns>
    private static IExceptionHandlerFeature HandledFeature(HttpContext httpContext, Exception exception)
    {
        IExceptionHandlerFeature? existing = httpContext.Features.Get<IExceptionHandlerFeature>();
        if (existing is not null && ReferenceEquals(existing.Error, exception))
        {
            return existing;
        }

        var feature = new ExceptionHandlerFeature
        {
            Error = exception,
            Path = httpContext.Request.Path.Value ?? string.Empty,
            Endpoint = httpContext.GetEndpoint(),
            RouteValues = httpContext.Features.Get<IRouteValuesFeature>()?.RouteValues,
        };
        httpContext.Features.Set<IExceptionHandlerFeature>(feature);
        httpContext.Features.Set<IExceptionHandlerPathFeature>(feature);
        return feature;
    }

    /// <summary>Returns the route template of the failed endpoint, else its display name.</summary>
    /// <param name="endpoint">Endpoint the request matched; null when none.</param>
    /// <returns>The template, the display name, or null.</returns>
    private static string? RouteTemplate(Endpoint? endpoint) =>
        (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? endpoint?.DisplayName;

    /// <summary>Returns the full type names of an exception and its inner exceptions, outermost first.</summary>
    /// <param name="exception">The outermost exception.</param>
    /// <returns>The type names joined by arrows.</returns>
    private static string TypeChain(Exception exception)
    {
        var names = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            Type type = current.GetType();
            names.Add(type.FullName ?? type.Name);
        }

        return string.Join(TypeChainSeparator, names);
    }

    /// <summary>Returns the log level of a written status: Warning for 503, Error for 500 and other server errors except 501, otherwise Information.</summary>
    /// <param name="status">HTTP status written.</param>
    /// <returns>The log level.</returns>
    private static LogLevel LevelOf(int status) => status switch
    {
        StatusCodes.Status501NotImplemented => LogLevel.Information,
        StatusCodes.Status503ServiceUnavailable => LogLevel.Warning,
        >= StatusCodes.Status500InternalServerError => LogLevel.Error,
        _ => LogLevel.Information,
    };

    /// <summary>Logs an answered exception's metadata at the given level.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="level">Log level.</param>
    /// <param name="method">HTTP method.</param>
    /// <param name="routeTemplate">Route template or endpoint display name; null when no endpoint matched.</param>
    /// <param name="statusCode">HTTP status written.</param>
    /// <param name="failureType">Error-contract type; null when the exception was not translated.</param>
    /// <param name="oracleErrorNumber">Signed Oracle error number; null when none.</param>
    /// <param name="package">Attributed package; null when none.</param>
    /// <param name="kind">Catalogue kind; null when uncatalogued.</param>
    /// <param name="openItemId">Open-item id; null when none.</param>
    /// <param name="exceptionTypes">Exception type chain, outermost first.</param>
    /// <param name="traceId">Request trace identifier.</param>
    [LoggerMessage(
        EventId = 1,
        EventName = "ExceptionAnswered",
        Message = "{Method} {RouteTemplate} answered {StatusCode} {FailureType} (Oracle number {OracleErrorNumber}, package {Package}, kind {Kind}, open item {OpenItemId}) for {ExceptionTypes}; trace {TraceId}")]
    private static partial void LogAnswered(
        ILogger logger,
        LogLevel level,
        string method,
        string? routeTemplate,
        int statusCode,
        string? failureType,
        int? oracleErrorNumber,
        string? package,
        string? kind,
        string? openItemId,
        string exceptionTypes,
        string traceId);

    /// <summary>Logs a server-fault exception's metadata and stack frames at Error.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="method">HTTP method.</param>
    /// <param name="routeTemplate">Route template or endpoint display name; null when no endpoint matched.</param>
    /// <param name="statusCode">HTTP status written.</param>
    /// <param name="failureType">Error-contract type; null when the exception was not translated.</param>
    /// <param name="oracleErrorNumber">Signed Oracle error number; null when none.</param>
    /// <param name="package">Attributed package; null when none.</param>
    /// <param name="kind">Catalogue kind; null when uncatalogued.</param>
    /// <param name="openItemId">Open-item id; null when none.</param>
    /// <param name="exceptionTypes">Exception type chain, outermost first.</param>
    /// <param name="traceId">Request trace identifier.</param>
    /// <param name="stackTrace">Stack frames of the outermost exception; null when it was never thrown.</param>
    [LoggerMessage(
        EventId = 2,
        EventName = "ServerFaultAnswered",
        Level = LogLevel.Error,
        Message = "{Method} {RouteTemplate} answered {StatusCode} {FailureType} (Oracle number {OracleErrorNumber}, package {Package}, kind {Kind}, open item {OpenItemId}) for {ExceptionTypes}; trace {TraceId}; stack {StackTrace}")]
    private static partial void LogServerFault(
        ILogger logger,
        string method,
        string? routeTemplate,
        int statusCode,
        string? failureType,
        int? oracleErrorNumber,
        string? package,
        string? kind,
        string? openItemId,
        string exceptionTypes,
        string traceId,
        string? stackTrace);

    /// <summary>Logs at Error that writing the error-contract body failed, naming only the exception types.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="method">HTTP method.</param>
    /// <param name="routeTemplate">Route template or endpoint display name; null when no endpoint matched.</param>
    /// <param name="statusCode">HTTP status of the response.</param>
    /// <param name="exceptionTypes">Handled exception type chain, outermost first.</param>
    /// <param name="writerExceptionTypes">Type chain of the exception raised while translating or writing, outermost first.</param>
    /// <param name="traceId">Request trace identifier.</param>
    [LoggerMessage(
        EventId = 3,
        EventName = "ExceptionWriterFailed",
        Level = LogLevel.Error,
        Message = "{Method} {RouteTemplate} answered {StatusCode} after writing the error body failed with {WriterExceptionTypes} for {ExceptionTypes}; trace {TraceId}")]
    private static partial void LogWriterFailed(
        ILogger logger,
        string method,
        string? routeTemplate,
        int statusCode,
        string exceptionTypes,
        string writerExceptionTypes,
        string traceId);
}
