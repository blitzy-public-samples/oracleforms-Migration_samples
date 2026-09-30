using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Api.Errors;
using Billing.Invoicing.Api.Services;
using Billing.Invoicing.Data.Blocked;
using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;
using Microsoft.AspNetCore.Http;

namespace Billing.Invoicing.Tests.Api;

/// <summary>Request-validation outcomes, open-item attachments, import results and LOV binds of <see cref="InvoiceWorkflowService"/> over hand-written port fakes.</summary>
[Trait("Category", "Orchestration")]
public sealed class InvoiceWorkflowRequestValidationTests
{
    private const string PatientRequiredText = "Select Patient No is required ";
    private const string DocumentKindText = "Document kind must be invoice, patient-card, barcode-sms or iqama-check.";

    private static readonly OperatorContext Operator = new()
    {
        UserNo = 1,
        UserName = "dev",
        InfoCenterId = "1",
        MachineName = "clone24",
        SessionId = "0123456789ABCDEF0123456789ABCDEF",
    };

    private static InvoiceWorkflowService Service(FakePorts ports) => ports.CreateService();

    private static void AssertBlocking(IReadOnlyList<MessageDto> messages, string field, string text, string? rule = null)
    {
        var message = Assert.Single(messages);
        Assert.Equal(field, message.Field);
        Assert.Equal(text, message.Text);
        Assert.Equal(ValidationMessage.Blocking, message.Severity);
        Assert.Equal(rule, message.Rule);
    }

    [Theory]
    [InlineData("NOPE")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Validate_UnknownTarget_ReturnsBlockingTargetMessage(string target)
    {
        var ports = new FakePorts();

        var response = await Service(ports).Validate(new ValidateDraftRequest { Target = target }, Operator);

        AssertBlocking(response.Messages, "TARGET", $"Unknown validation target '{target}'.");
        Assert.Empty(response.Adjusted);
        Assert.Empty(response.OpenItems);
        Assert.Empty(ports.Calls);
    }

    [Theory]
    [InlineData("LINE", null)]
    [InlineData("QTY", -1)]
    [InlineData("SERVICEID", 1)]
    [InlineData("PRICE", 5)]
    public async Task Validate_LineTargetOutsideTheDraft_ReturnsBlockingLineMessage(string target, int? lineIndex)
    {
        var ports = new FakePorts();
        var request = new ValidateDraftRequest
        {
            Target = target,
            LineIndex = lineIndex,
            Draft = new DraftDto { Lines = new[] { new InvoiceLineDraft { ServiceId = "S1", Qty = 1m, ClientId = "C1" } } },
        };

        var response = await Service(ports).Validate(request, Operator);

        AssertBlocking(response.Messages, "LINE", "LineIndex must address a line of the draft.");
        Assert.Empty(ports.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task GetCoverage_BlankPatient_ReturnsDr01WithoutLookups(string? patientNo)
    {
        var ports = new FakePorts();

        var response = await Service(ports).GetCoverage(patientNo!, null, new InvoiceEntryParameters(), Operator);

        AssertBlocking(response.Messages, "PATIENTNO", PatientRequiredText, "DR-01");
        Assert.Null(response.Coverage);
        Assert.Equal(0, response.PayType);
        Assert.Empty(response.OpenItems);
        Assert.Empty(ports.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ImportPackage_BlankPackageService_ReturnsBlockingServiceIdMessage(string packageServiceId)
    {
        var ports = new FakePorts();

        var response = await Service(ports).ImportPackage(new PackageImportRequest { PackageServiceId = packageServiceId }, Operator);

        AssertBlocking(response.Messages, "SERVICEID", "Package service id is required.");
        Assert.Empty(response.Lines);
        Assert.Null(response.Result);
        Assert.Empty(ports.Calls);
    }

    [Theory]
    [InlineData("receipt")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public async Task BuildDocument_BlankOrUnknownKind_ThrowsArgumentExceptionCarryingBlockingKind(string? kind)
    {
        var ports = new FakePorts();

        var error = await Assert.ThrowsAsync<ArgumentException>(() => Service(ports).BuildDocument(7, kind!, Operator));

        var messages = Assert.IsAssignableFrom<IReadOnlyList<MessageDto>>(error.Data[ProblemDetailsWriter.MessagesDataKey]);
        AssertBlocking(messages, "KIND", DocumentKindText);
        Assert.Empty(ports.Calls);
    }

    [Fact]
    public async Task BuildDocument_UnknownKind_WritesFieldValidation422()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => Service(new FakePorts()).BuildDocument(7, "receipt", Operator));

        var written = await ProblemDetailsWriterTests.WriteHandled(error);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, written.Status);
        Assert.Equal("field-validation", written.Body.GetProperty("type").GetString());
        var message = Assert.Single(written.Body.GetProperty("messages").EnumerateArray());
        Assert.Equal("KIND", message.GetProperty("field").GetString());
        Assert.Equal(DocumentKindText, message.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Preview_NullDraftLine_WritesFieldValidation422WithoutReads()
    {
        var ports = new FakePorts();
        var draft = new DraftDto { Lines = new InvoiceLineDraft[] { null! } };

        var error = await Assert.ThrowsAsync<ArgumentException>(() => Service(ports).Preview(draft, Operator));
        var written = await ProblemDetailsWriterTests.WriteHandled(error);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, written.Status);
        Assert.Equal("field-validation", written.Body.GetProperty("type").GetString());
        var message = Assert.Single(written.Body.GetProperty("messages").EnumerateArray());
        Assert.Equal("LINE", message.GetProperty("field").GetString());
        Assert.Equal("Draft lines must not contain null entries.", message.GetProperty("text").GetString());
        Assert.Empty(ports.Calls);
    }

    [Fact]
    public async Task SendSms_AttachesOi12AndOi45ToTheBlockedFailure()
    {
        var ports = new FakePorts();

        var error = await Assert.ThrowsAsync<NotImplementedException>(() => Service(ports).SendSms(7, Operator));

        Assert.StartsWith("OI-12: ", error.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { "OI-12", "OI-45" }, AttachedOpenItems(error));
        Assert.Equal(new[] { "SendInvoiceSms" }, ports.Calls);
    }

    [Fact]
    public async Task TransferStock_AttachesOi10AndOi44ToTheBlockedFailure()
    {
        var ports = new FakePorts();

        var error = await Assert.ThrowsAsync<NotImplementedException>(() => Service(ports).TransferStock(7, Operator));

        Assert.StartsWith("OI-10: ", error.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { "OI-10", "OI-44" }, AttachedOpenItems(error));
        Assert.Equal(new[] { "TransferStock" }, ports.Calls);
    }

    [Theory]
    [InlineData("patient-card", "OI-47: ", new[] { "OI-47" }, "BuildLegacyDocument")]
    [InlineData("barcode-sms", "OI-47: ", new[] { "OI-47", "OI-26" }, "BuildLegacyDocument")]
    [InlineData(" Barcode-SMS ", "OI-47: ", new[] { "OI-47", "OI-26" }, "BuildLegacyDocument")]
    [InlineData("iqama-check", "OI-48: ", new[] { "OI-48" }, "BuildLegacyDocument")]
    [InlineData("invoice", "OI-11: ", new[] { "OI-11", "OI-45", "OI-46", "OI-49" }, "BuildPrintUrl")]
    public async Task BuildDocument_KnownKind_AttachesItsOpenItemsToTheBlockedFailure(
        string kind,
        string messagePrefix,
        string[] openItems,
        string portCalled)
    {
        var ports = new FakePorts();

        var error = await Assert.ThrowsAsync<NotImplementedException>(() => Service(ports).BuildDocument(7, kind, Operator));

        Assert.StartsWith(messagePrefix, error.Message, StringComparison.Ordinal);
        Assert.Equal(openItems, AttachedOpenItems(error));
        Assert.Equal(new[] { portCalled }, ports.Calls);
    }

    [Theory]
    [InlineData("sms", "OI-12", new[] { "OI-12", "OI-45" })]
    [InlineData("stock-transfer", "OI-10", new[] { "OI-10", "OI-44" })]
    [InlineData("barcode-sms", "OI-47", new[] { "OI-47", "OI-26" })]
    public async Task BlockedOperation_Writes501WithSecondaryOpenItems(string operation, string primary, string[] openItems)
    {
        var service = Service(new FakePorts());
        Func<Task> call = operation switch
        {
            "sms" => () => service.SendSms(7, Operator),
            "stock-transfer" => () => service.TransferStock(7, Operator),
            _ => () => service.BuildDocument(7, operation, Operator),
        };

        var error = await Assert.ThrowsAsync<NotImplementedException>(call);
        var written = await ProblemDetailsWriterTests.WriteHandled(error);

        Assert.Equal(StatusCodes.Status501NotImplemented, written.Status);
        Assert.Equal("open-item", written.Body.GetProperty("type").GetString());
        Assert.Equal(primary, written.Body.GetProperty("openItemId").GetString());
        Assert.Equal(
            openItems,
            written.Body.GetProperty("openItems").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task ImportRequests_NoSelectedRows_ReturnsZeroCountImportResult()
    {
        var ports = new FakePorts();

        var response = await Service(ports).ImportRequests(RequestImport("P1", "V1", docId: 5), Operator);

        var result = Assert.IsType<ImportResultRow>(response.Result);
        Assert.Equal("REQUEST", result.SourceType);
        Assert.Equal(0, result.SourceCount);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(0, result.SkippedRejectedCount);
        Assert.Equal(0, result.SkippedNeedApprovalCount);
        Assert.Equal(0, result.SkippedInvalidCount);
        Assert.Equal("N", result.HasPriceOverrides);
        Assert.Equal("Invoice request import completed. Expanded 0 request rows into 0 invoice lines.", result.Message);
        Assert.Empty(response.Lines);
        Assert.Empty(response.Messages);
        Assert.Equal(new[] { "GetPatientCoverage", "GetCompanyType", "GetPreferences", "GetSelectedRequestRows" }, ports.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ImportRequests_MissingPatient_ReturnsDr01WithoutReads(string? patientNo)
    {
        var ports = new FakePorts();

        var response = await Service(ports).ImportRequests(RequestImport(patientNo, "V1", docId: 5), Operator);

        AssertBlocking(response.Messages, "PATIENTNO", PatientRequiredText, "DR-01");
        Assert.Empty(response.Lines);
        Assert.Null(response.Result);
        Assert.Empty(ports.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ImportRequests_MissingVisit_ReturnsBlockingVisitUniqueMessageWithoutReads(string? visitUnique)
    {
        var ports = new FakePorts();

        var response = await Service(ports).ImportRequests(RequestImport("P1", visitUnique, docId: 5), Operator);

        AssertBlocking(response.Messages, "VISIT_UNIQUE", "Request import failed: visit unique is required.");
        Assert.Null(response.Result);
        Assert.Empty(ports.Calls);
    }

    [Fact]
    public async Task ImportRequests_MissingDoctorPatientAndVisit_ReportsTheDr18DoctorMessageFirst()
    {
        var ports = new FakePorts();

        var response = await Service(ports).ImportRequests(RequestImport(null, null, docId: null), Operator);

        AssertBlocking(response.Messages, "DOCIDX", "Select doctor First", "DR-18");
        Assert.Empty(ports.Calls);
    }

    [Theory]
    [InlineData("RESERV_NO", "DOCIDX", "12", "PATIENTNO", "P1")]
    [InlineData("offers", "PAYTYPE", "1", null, null)]
    public async Task GetLov_DateFilteredListWithoutDraftDate_ReturnsMissingInvDateWithoutReads(
        string name,
        string firstItem,
        string firstValue,
        string? secondItem,
        string? secondValue)
    {
        var ports = new FakePorts();
        var binds = new Dictionary<string, string?> { [firstItem] = firstValue };
        if (secondItem is not null)
        {
            binds[secondItem] = secondValue;
        }

        var response = await Service(ports).GetLov(name, binds, null, Operator);

        Assert.NotNull(response);
        Assert.Equal(name.ToUpperInvariant(), response.Name);
        Assert.Empty(response.Rows);
        AssertBlocking(response.Messages, "INVDATE", $"INVDATE is required for the {name.ToUpperInvariant()} list.");
        Assert.Empty(ports.Calls);
    }

    [Theory]
    [InlineData("RESERV_NO", "ReservNo")]
    [InlineData("OFFERS", "Offers")]
    public async Task GetLov_DateFilteredListWithDraftDate_BindsTheDraftDate(string name, string query)
    {
        var ports = new FakePorts();
        var draftDate = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Unspecified);
        var binds = new Dictionary<string, string?> { ["DOCIDX"] = "12", ["PATIENTNO"] = "P1", ["PAYTYPE"] = "1" };

        var response = await Service(ports).GetLov(name, binds, draftDate, Operator);

        Assert.NotNull(response);
        Assert.Empty(response.Messages);
        Assert.Equal(draftDate, ports.LovDate);
        Assert.Equal(new[] { query }, ports.Calls);
    }

    [Fact]
    public async Task GetLov_ReservNoWithoutDoctorOrDate_ReportsTheDoctorBindFirst()
    {
        var ports = new FakePorts();

        var response = await Service(ports).GetLov("RESERV_NO", new Dictionary<string, string?> { ["PATIENTNO"] = "P1" }, null, Operator);

        Assert.NotNull(response);
        AssertBlocking(response.Messages, "DOCIDX", "DOCIDX is required for the RESERV_NO list.");
        Assert.Empty(ports.Calls);
    }

    private static ImportRequestsRequest RequestImport(string? patientNo, string? visitUnique, int? docId) => new()
    {
        Draft = new DraftDto
        {
            Header = new InvoiceHeaderDraft { PatientNo = patientNo, DocId = docId, PayType = 1, CompCode = "0" },
            Parameters = new InvoiceEntryParameters { VisitUnique = visitUnique },
        },
    };

    private static IReadOnlyList<string> AttachedOpenItems(Exception error) =>
        Assert.IsAssignableFrom<IReadOnlyList<string>>(error.Data[ProblemDetailsWriter.OpenItemsDataKey]);

    /// <summary>Records every port member the service calls; each port is a <see cref="DispatchProxy"/> over this record, and blocked members run the real <see cref="LegacyExternalCalls"/>.</summary>
    private sealed class FakePorts
    {
        private const string SessionPrefix = "Session.";

        private readonly List<string> _calls = new();
        private readonly LegacyExternalCalls _blocked = new();
        private readonly NullabilityInfoContext _nullability = new();

        /// <summary>Members called, in call order.</summary>
        public IReadOnlyList<string> Calls => _calls;

        /// <summary>Database time returned by the lookups' database-time read.</summary>
        public DateTime DatabaseTime { get; } = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);

        /// <summary>Date passed to the last date-filtered LOV query.</summary>
        public DateTime? LovDate { get; private set; }

        /// <summary>Creates the service with one port proxy per constructor parameter.</summary>
        /// <returns>The service under test.</returns>
        public InvoiceWorkflowService CreateService()
        {
            ConstructorInfo constructor = typeof(InvoiceWorkflowService).GetConstructors().Single();
            object[] ports = constructor.GetParameters().Select(parameter => Port(parameter.ParameterType, string.Empty)).ToArray();
            return (InvoiceWorkflowService)constructor.Invoke(ports);
        }

        private object Port(Type port, string prefix)
        {
            object proxy = DispatchProxy.Create(port, typeof(PortProxy));
            ((PortProxy)proxy).Bind(this, prefix);
            return proxy;
        }

        private object? Handle(MethodInfo method, object?[] args, string prefix)
        {
            if (prefix.Length > 0)
            {
                _calls.Add(args.FirstOrDefault() is string savepoint ? $"{prefix}{method.Name}:{savepoint}" : prefix + method.Name);
                return Default(method);
            }

            _calls.Add(method.Name);
            if (method.DeclaringType == typeof(ILegacyExternalCalls))
            {
                try
                {
                    return method.Invoke(_blocked, args);
                }
                catch (TargetInvocationException failure) when (failure.InnerException is not null)
                {
                    ExceptionDispatchInfo.Throw(failure.InnerException);
                    throw;
                }
            }

            switch (method.Name)
            {
                case nameof(IBilInvoiceApiGateway.BuildPrintUrl):
                    throw new NotImplementedException($"{OpenItemIds.OI11}: invoice print URL is not available.");
                case nameof(IBilImportGateway.ApprovalCheckMode):
                    return args.FirstOrDefault() is 2 ? 0 : 1;
                case nameof(IOracleSessionFactory.Open):
                    return Task.FromResult((IOracleSession)Port(typeof(IOracleSession), SessionPrefix));
                case nameof(ILookupQueries.GetDatabaseTime):
                    return Task.FromResult(DatabaseTime);
                case nameof(ILovQueries.ReservNo) or nameof(ILovQueries.Offers):
                    LovDate = args.OfType<DateTime>().First();
                    break;
            }

            return Default(method);
        }

        private object? Default(MethodInfo method)
        {
            Type type = method.ReturnType;
            if (type == typeof(void))
            {
                return null;
            }

            if (type == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (type == typeof(ValueTask))
            {
                return ValueTask.CompletedTask;
            }

            NullabilityInfo info = _nullability.Create(method.ReturnParameter);
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            {
                Type result = type.GetGenericArguments()[0];
                return typeof(Task).GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(result)
                    .Invoke(null, new[] { Value(info.GenericTypeArguments[0]) });
            }

            return Value(info);
        }

        private static object? Value(NullabilityInfo info)
        {
            Type type = info.Type;
            if (info.ReadState == NullabilityState.Nullable || Nullable.GetUnderlyingType(type) is not null)
            {
                return null;
            }

            if (type.IsArray)
            {
                return Array.CreateInstance(type.GetElementType()!, 0);
            }

            if (type.IsGenericType)
            {
                Type definition = type.GetGenericTypeDefinition();
                Type[] arguments = type.GetGenericArguments();
                if (definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>) || definition == typeof(IEnumerable<>))
                {
                    return Array.CreateInstance(arguments[0], 0);
                }

                if (definition == typeof(IReadOnlyDictionary<,>))
                {
                    return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(arguments));
                }

                if (type.IsValueType && typeof(ITuple).IsAssignableFrom(type))
                {
                    return Activator.CreateInstance(type, info.GenericTypeArguments.Select(Value).ToArray());
                }
            }

            if (type == typeof(string))
            {
                return string.Empty;
            }

            if (type.IsValueType)
            {
                return Activator.CreateInstance(type);
            }

            return type.GetConstructor(Type.EmptyTypes) is not null ? Activator.CreateInstance(type) : null;
        }

        /// <summary>Port proxy forwarding each call to its <see cref="FakePorts"/>.</summary>
        public class PortProxy : DispatchProxy
        {
            private FakePorts? _owner;
            private string _prefix = string.Empty;

            /// <summary>Binds the proxy to its owner and call-name prefix.</summary>
            /// <param name="owner">Fake recording the calls.</param>
            /// <param name="prefix">Prefix of the recorded call names.</param>
            public void Bind(FakePorts owner, string prefix)
            {
                _owner = owner;
                _prefix = prefix;
            }

            /// <inheritdoc/>
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
                _owner!.Handle(targetMethod!, args ?? Array.Empty<object?>(), _prefix);
        }
    }
}
