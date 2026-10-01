using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Billing.Invoicing.Api.Errors;
using Billing.Invoicing.Data.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;

namespace Billing.Invoicing.Tests.Api;

/// <summary>Responses and log records of <see cref="ProblemDetailsExceptionHandler"/> behind the Api's exception-handler middleware.</summary>
[Trait("Category", "Orchestration")]
public sealed class ProblemDetailsExceptionHandlerTests
{
    private const string ProblemJson = "application/problem+json";
    private const string CoverageRoute = "api/patients/{patientNo}/coverage";
    private const string PatientNo = "123456";
    private const string CoveragePath = "/api/patients/" + PatientNo + "/coverage";
    private const string InvoiceNo = "778899";
    private const string RequestId = "A1B2C3D4E5F60718293A4B5C6D7E8F90";
    private const string Secret = "SECRET";

    private const string HandlerCategory = "Billing.Invoicing.Api.Errors.ProblemDetailsExceptionHandler";
    private const string MiddlewareCategory = "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware";
    private const string HandledExceptionEvent = "Microsoft.AspNetCore.Diagnostics.HandledException";

    private const string IdempotencyText = "Invoice request " + RequestId + " refers to unavailable invoice " + InvoiceNo + ".";
    private const string OracleChain = "Oracle.ManagedDataAccess.Client.OracleException -> System.InvalidOperationException";

    private static readonly ConstructorInfo? OracleExceptionConstructor = typeof(OracleException).GetConstructor(
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        new[] { typeof(int), typeof(string), typeof(string), typeof(string), typeof(Exception) },
        modifiers: null);

    [Fact]
    public async Task IdempotencyBusinessError_Writes422AndLogsOnlyRedactedMetadata()
    {
        var run = await SendAsync(IdempotencyError());

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, run.Status);
        Assert.Equal(ProblemJson, run.ContentType);
        JsonElement body = Assert.NotNull(run.Body);
        Assert.Equal(DataFailure.OracleBusinessErrorType, body.GetProperty("type").GetString());
        Assert.Equal(-20848, body.GetProperty("oracleErrorNumber").GetInt32());
        Assert.Equal(OracleErrorCatalog.ApiPackage, body.GetProperty("package").GetString());
        Assert.Equal(IdempotencyText, body.GetProperty("message").GetString());

        AssertNoFrameworkDiagnostics(run);
        AssertRedacted(run.Logs, RequestId, PatientNo, InvoiceNo);
        LogRecord record = Assert.Single(run.Logs, candidate => candidate.Category == HandlerCategory);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Null(record.Exception);
        Assert.Equal("GET", Value(record, "Method"));
        Assert.Equal(CoverageRoute, Value(record, "RouteTemplate"));
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, Value(record, "StatusCode"));
        Assert.Equal(DataFailure.OracleBusinessErrorType, Value(record, "FailureType"));
        Assert.Equal(-20848, Value(record, "OracleErrorNumber"));
        Assert.Equal(OracleErrorCatalog.ApiPackage, Value(record, "Package"));
        Assert.Equal(OracleErrorCatalog.IdempotencyConflictKind, Value(record, "Kind"));
        Assert.Null(Value(record, "OpenItemId"));
        Assert.Equal(OracleChain, Value(record, "ExceptionTypes"));
        Assert.False(string.IsNullOrEmpty(Value(record, "TraceId") as string));
    }

    [Fact]
    public async Task DelegateOnly_FrameworkLogsTheExceptionText()
    {
        var run = await SendAsync(IdempotencyError(), registerHandler: false);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, run.Status);
        Assert.Contains(HandledExceptionEvent, run.Events);
        LogRecord record = Assert.Single(run.Logs, candidate => candidate.Category == MiddlewareCategory);
        Assert.Contains(RequestId, record.Exception?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnexpectedException_WritesBare500AndLogsTypesAndStackAtError()
    {
        var run = await SendAsync(new InvalidOperationException($"Invoice {InvoiceNo} of patient {PatientNo} failed: {Secret}."));

        Assert.Equal(StatusCodes.Status500InternalServerError, run.Status);
        Assert.Equal(ProblemJson, run.ContentType);
        JsonElement body = Assert.NotNull(run.Body);
        Assert.Equal(new[] { "type", "title", "status" }, body.EnumerateObject().Select(member => member.Name).ToArray());
        Assert.Equal("about:blank", body.GetProperty("type").GetString());

        AssertNoFrameworkDiagnostics(run);
        AssertRedacted(run.Logs, Secret, PatientNo, InvoiceNo);
        LogRecord record = Assert.Single(run.Logs, candidate => candidate.Category == HandlerCategory);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Null(record.Exception);
        Assert.Equal(StatusCodes.Status500InternalServerError, Value(record, "StatusCode"));
        Assert.Null(Value(record, "FailureType"));
        Assert.Equal(typeof(InvalidOperationException).FullName, Value(record, "ExceptionTypes"));
        Assert.False(string.IsNullOrWhiteSpace(Value(record, "StackTrace") as string));
    }

    [Fact]
    public async Task OpenItem_Writes501AndLogsTheOpenItemAtInformation()
    {
        var run = await SendAsync(new NotImplementedException($"OI-12: BIL_MESSAGE invoice SMS for patient {PatientNo} is not available ({Secret})."));

        Assert.Equal(StatusCodes.Status501NotImplemented, run.Status);
        JsonElement body = Assert.NotNull(run.Body);
        Assert.Equal(DataFailure.OpenItemType, body.GetProperty("type").GetString());
        Assert.Equal("OI-12", body.GetProperty("openItemId").GetString());

        AssertNoFrameworkDiagnostics(run);
        AssertRedacted(run.Logs, Secret, PatientNo);
        LogRecord record = Assert.Single(run.Logs, candidate => candidate.Category == HandlerCategory);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Null(record.Exception);
        Assert.Equal(StatusCodes.Status501NotImplemented, Value(record, "StatusCode"));
        Assert.Equal(DataFailure.OpenItemType, Value(record, "FailureType"));
        Assert.Equal("OI-12", Value(record, "OpenItemId"));
        Assert.Equal(typeof(NotImplementedException).FullName, Value(record, "ExceptionTypes"));
    }

    [Fact]
    public async Task OracleUnavailable_Writes503AndLogsAtWarning()
    {
        const string ListenerHost = "10.1.2.3";
        var run = await SendAsync(Driver(12541, $"ORA-12541: Cannot connect. No listener at host {ListenerHost} port 1521."));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, run.Status);
        JsonElement body = Assert.NotNull(run.Body);
        Assert.Equal(DataFailure.OracleUnavailableType, body.GetProperty("type").GetString());

        AssertNoFrameworkDiagnostics(run);
        AssertRedacted(run.Logs, ListenerHost, PatientNo);
        LogRecord record = Assert.Single(run.Logs, candidate => candidate.Category == HandlerCategory);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Null(record.Exception);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Value(record, "StatusCode"));
        Assert.Equal(DataFailure.OracleUnavailableType, Value(record, "FailureType"));
        Assert.Equal(12541, Value(record, "OracleErrorNumber"));
    }

    [Fact]
    public async Task WriterFailure_Answers500AndLogsTypesOnly()
    {
        var run = await SendAsync(IdempotencyError(), responseBody: new RefusingStream());

        Assert.Equal(StatusCodes.Status500InternalServerError, run.Status);
        Assert.Null(run.ContentType);
        Assert.Null(run.Body);

        AssertNoFrameworkDiagnostics(run);
        AssertRedacted(run.Logs, RequestId, PatientNo, InvoiceNo, Secret);
        LogRecord record = Assert.Single(run.Logs, candidate => candidate.Category == HandlerCategory);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Equal("ExceptionWriterFailed", record.EventId.Name);
        Assert.Null(record.Exception);
        Assert.Equal(StatusCodes.Status500InternalServerError, Value(record, "StatusCode"));
        Assert.Equal(OracleChain, Value(record, "ExceptionTypes"));
        Assert.Equal(typeof(IOException).FullName, Value(record, "WriterExceptionTypes"));
    }

    [Fact]
    public async Task TryHandleAsync_WriterFailure_ReturnsTrueWithAnEmpty500()
    {
        using var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Response.Body = new RefusingStream();

        bool handled = await Handler(factory).TryHandleAsync(context, IdempotencyError(), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Null(context.Response.ContentType);
        IReadOnlyList<LogRecord> records = logs.Records;
        AssertRedacted(records, RequestId, InvoiceNo, Secret);
        LogRecord record = Assert.Single(records);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Null(record.Exception);
        Assert.Equal("POST", Value(record, "Method"));
        Assert.Null(Value(record, "RouteTemplate"));
        Assert.Equal(OracleChain, Value(record, "ExceptionTypes"));
        Assert.Equal(typeof(IOException).FullName, Value(record, "WriterExceptionTypes"));
    }

    [Fact]
    public async Task TryHandleAsync_WithoutHandlerFeature_WritesTheGivenException()
    {
        using var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Response.Body = new MemoryStream();

        bool handled = await Handler(factory).TryHandleAsync(context, IdempotencyError(), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, context.Response.StatusCode);
        JsonElement body = Assert.NotNull(await Body(context.Response.Body));
        Assert.Equal(-20848, body.GetProperty("oracleErrorNumber").GetInt32());
        LogRecord record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, Value(record, "StatusCode"));
    }

    [Fact]
    public async Task TryHandleAsync_StartedResponse_DeclinesWithoutWritingOrLogging()
    {
        using var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        var context = new DefaultHttpContext();
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;
        context.Features.Set<IHttpResponseFeature>(new StartedResponse());

        bool handled = await Handler(factory).TryHandleAsync(context, IdempotencyError(), CancellationToken.None);

        Assert.False(handled);
        Assert.Equal(0, responseBody.Length);
        Assert.Empty(logs.Records);
    }

    [Fact]
    public void Constructor_NullArgument_Throws()
    {
        using var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        var translator = new OracleFailureTranslator();
        var writer = new ProblemDetailsWriter(translator);
        ILogger<ProblemDetailsExceptionHandler> logger = factory.CreateLogger<ProblemDetailsExceptionHandler>();

        Assert.Equal("writer", Assert.Throws<ArgumentNullException>(() => new ProblemDetailsExceptionHandler(null!, translator, logger)).ParamName);
        Assert.Equal("translator", Assert.Throws<ArgumentNullException>(() => new ProblemDetailsExceptionHandler(writer, null!, logger)).ParamName);
        Assert.Equal("logger", Assert.Throws<ArgumentNullException>(() => new ProblemDetailsExceptionHandler(writer, translator, null!)).ParamName);
    }

    /// <summary>Sends a GET that throws the exception through routing, the Api's exception-handler delegate and, when registered, the handler, in-process.</summary>
    /// <param name="error">Exception the matched endpoint throws.</param>
    /// <param name="registerHandler">Whether <see cref="ProblemDetailsExceptionHandler"/> is registered.</param>
    /// <param name="responseBody">Response body stream; a new memory stream when null.</param>
    /// <returns>The written status, content type, parsed body, captured log records and diagnostic event names.</returns>
    private static async Task<Run> SendAsync(Exception error, bool registerHandler = true, Stream? responseBody = null)
    {
        using var logs = new CapturingLoggerProvider();
        var events = new EventRecorder();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Information).AddProvider(logs));
        services.AddMetrics();
        services.AddSingleton(new DiagnosticListener("Microsoft.AspNetCore"));
        services.AddSingleton<DiagnosticSource>(provider => provider.GetRequiredService<DiagnosticListener>());
        services.AddRouting();
        services.AddSingleton<OracleFailureTranslator>();
        services.AddSingleton<ProblemDetailsWriter>();
        if (registerHandler)
        {
            services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
        }

        await using var provider = services.BuildServiceProvider();
        using IDisposable subscription = provider.GetRequiredService<DiagnosticListener>().Subscribe(events);

        RequestDelegate throwing = _ => throw error;
        var app = new ApplicationBuilder(provider);
        app.UseRouting();
        app.UseExceptionHandler(handler => handler.Run(context =>
            context.RequestServices.GetRequiredService<ProblemDetailsWriter>().WriteAsync(context)));
        app.UseEndpoints(endpoints => endpoints.MapGet(CoverageRoute, throwing));
        RequestDelegate pipeline = app.Build();

        await using var scope = provider.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = CoveragePath;
        context.Response.Body = responseBody ?? new MemoryStream();

        await pipeline(context);

        Assert.Empty(events.Errors);
        JsonElement? body = context.Response.Body is RefusingStream ? null : await Body(context.Response.Body);
        return new Run(context.Response.StatusCode, context.Response.ContentType, body, logs.Records, events.Names);
    }

    /// <summary>Creates a handler over a fresh writer and translator that logs through the given factory.</summary>
    /// <param name="factory">Factory of the handler's logger.</param>
    /// <returns>The handler.</returns>
    private static ProblemDetailsExceptionHandler Handler(ILoggerFactory factory)
    {
        var translator = new OracleFailureTranslator();
        return new ProblemDetailsExceptionHandler(
            new ProblemDetailsWriter(translator),
            translator,
            factory.CreateLogger<ProblemDetailsExceptionHandler>());
    }

    /// <summary>Parses a written response body, or returns null when nothing was written.</summary>
    /// <param name="stream">Response body stream.</param>
    /// <returns>The parsed body, or null.</returns>
    private static async Task<JsonElement?> Body(Stream stream)
    {
        if (stream.Length == 0)
        {
            return null;
        }

        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }

    /// <summary>Asserts that no record carries a sensitive value in its category, text, state or exception.</summary>
    /// <param name="records">Captured log records.</param>
    /// <param name="sensitive">Values no record may contain.</param>
    private static void AssertRedacted(IEnumerable<LogRecord> records, params string[] sensitive)
    {
        foreach (LogRecord record in records)
        {
            var texts = new List<string?> { record.Category, record.Message, record.Exception?.ToString() };
            texts.AddRange(record.State.Select(pair => Convert.ToString(pair.Value, CultureInfo.InvariantCulture)));
            foreach (string value in sensitive)
            {
                Assert.DoesNotContain(texts, text => text is not null && text.Contains(value, StringComparison.Ordinal));
            }
        }
    }

    /// <summary>Asserts that the exception-handler middleware logged nothing and raised no handled-exception diagnostic event.</summary>
    /// <param name="run">The completed request.</param>
    private static void AssertNoFrameworkDiagnostics(Run run)
    {
        Assert.DoesNotContain(run.Logs, record => record.Category == MiddlewareCategory);
        Assert.DoesNotContain(HandledExceptionEvent, run.Events);
    }

    /// <summary>Returns the value of a named member of a record's structured state.</summary>
    /// <param name="record">Captured log record.</param>
    /// <param name="name">State member name.</param>
    /// <returns>The member value.</returns>
    private static object? Value(LogRecord record, string name) =>
        Assert.Single(record.State, pair => pair.Key == name).Value;

    /// <summary>Returns the ORA-20848 idempotency error raised at BIL_INVOICE_API line 1291, whose text embeds the request id and invoice number.</summary>
    /// <returns>The driver exception.</returns>
    private static OracleException IdempotencyError() => Driver(
        20848,
        "ORA-20848: " + IdempotencyText + "\n" + "ORA-06512: at \"HIS.BIL_INVOICE_API\", line 1291");

    /// <summary>Builds an ODP.NET exception through its internal constructor.</summary>
    /// <param name="number">ORA code as the driver reports it.</param>
    /// <param name="message">Driver error message.</param>
    /// <returns>The driver exception.</returns>
    private static OracleException Driver(int number, string message)
    {
        Assert.NotNull(OracleExceptionConstructor);
        return (OracleException)OracleExceptionConstructor.Invoke(new object[]
        {
            number,
            "HISDB",
            "BIL_INVOICE_API.CREATE_FULL_INVOICE",
            message,
            new InvalidOperationException("driver detail"),
        });
    }

    /// <summary>A completed request.</summary>
    /// <param name="Status">Written status.</param>
    /// <param name="ContentType">Written content type.</param>
    /// <param name="Body">Parsed body; null when nothing was written.</param>
    /// <param name="Logs">Captured log records.</param>
    /// <param name="Events">Names of the diagnostic events raised.</param>
    private sealed record Run(int Status, string? ContentType, JsonElement? Body, IReadOnlyList<LogRecord> Logs, IReadOnlyList<string> Events);

    /// <summary>A captured log record.</summary>
    /// <param name="Category">Logger category.</param>
    /// <param name="Level">Log level.</param>
    /// <param name="EventId">Event id.</param>
    /// <param name="Message">Formatted message.</param>
    /// <param name="State">Structured state members.</param>
    /// <param name="Exception">Exception passed to the logger.</param>
    private sealed record LogRecord(
        string Category,
        LogLevel Level,
        EventId EventId,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>> State,
        Exception? Exception);

    /// <summary>Logger provider that captures every record at or above the configured level.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogRecord> _records = new();

        /// <summary>The records captured so far, in logging order.</summary>
        public IReadOnlyList<LogRecord> Records => _records.ToArray();

        /// <inheritdoc/>
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _records);

        /// <summary>Releases the captured records.</summary>
        public void Dispose() => _records.Clear();
    }

    /// <summary>Logger that appends each record to its provider's queue.</summary>
    /// <param name="category">Logger category.</param>
    /// <param name="records">Queue receiving the records.</param>
    private sealed class CapturingLogger(string category, ConcurrentQueue<LogRecord> records) : ILogger
    {
        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        /// <inheritdoc/>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            KeyValuePair<string, object?>[] values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToArray()
                : Array.Empty<KeyValuePair<string, object?>>();
            records.Enqueue(new LogRecord(category, logLevel, eventId, formatter(state, exception), values, exception));
        }
    }

    /// <summary>Observer recording the names of the diagnostic events raised and any error the listener reports.</summary>
    private sealed class EventRecorder : IObserver<KeyValuePair<string, object?>>
    {
        private readonly ConcurrentQueue<string> _names = new();
        private readonly ConcurrentQueue<Exception> _errors = new();

        /// <summary>Names of the events raised, in order.</summary>
        public IReadOnlyList<string> Names => _names.ToArray();

        /// <summary>Errors the listener reported.</summary>
        public IReadOnlyList<Exception> Errors => _errors.ToArray();

        /// <summary>Whether the listener completed.</summary>
        public bool Completed { get; private set; }

        /// <inheritdoc/>
        public void OnCompleted() => Completed = true;

        /// <inheritdoc/>
        public void OnError(Exception error) => _errors.Enqueue(error);

        /// <inheritdoc/>
        public void OnNext(KeyValuePair<string, object?> value) => _names.Enqueue(value.Key);
    }

    /// <summary>Response body whose writes fail with an <see cref="IOException"/>.</summary>
    private sealed class RefusingStream : MemoryStream
    {
        private const string RefusalText = "The response body refused the write (" + Secret + ").";

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException(RefusalText);

        /// <inheritdoc/>
        public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException(RefusalText);

        /// <inheritdoc/>
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromException(new IOException(RefusalText));

        /// <inheritdoc/>
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new IOException(RefusalText));
    }

    /// <summary>Response feature whose response has already started.</summary>
    private sealed class StartedResponse : HttpResponseFeature
    {
        /// <inheritdoc/>
        public override bool HasStarted => true;
    }
}
