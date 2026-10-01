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
    [Trait("Decision", "D-49")]
    public async Task GetLov_ReservNoWithoutDoctorOrDate_ReportsTheDoctorThenTheDateBind()
    {
        var ports = new FakePorts();

        var response = await Service(ports).GetLov("RESERV_NO", new Dictionary<string, string?> { ["PATIENTNO"] = "P1" }, null, Operator);

        Assert.NotNull(response);
        Assert.Empty(response.Rows);
        Assert.Collection(
            response.Messages,
            message => AssertBlocking(new[] { message }, "DOCIDX", "DOCIDX is required for the RESERV_NO list."),
            message => AssertBlocking(new[] { message }, "INVDATE", "INVDATE is required for the RESERV_NO list."));
        Assert.Empty(ports.Calls);
    }

    [Theory]
    [InlineData("P123456789012", "PATIENTNO has 13 characters; at most 12 can be bound.")]
    [InlineData("\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9", "PATIENTNO has 14 bytes in UTF-8; at most 12 can be bound.")]
    public async Task GetCoverage_PatientNoOverTwelveBytes_ReturnsBlockingPatientNoWithoutLookups(string patientNo, string text)
    {
        var ports = new FakePorts();

        var response = await Service(ports).GetCoverage(patientNo, null, new InvoiceEntryParameters(), Operator);

        AssertBlocking(response.Messages, "PATIENTNO", text);
        Assert.Null(response.Coverage);
        Assert.Empty(response.OpenItems);
        Assert.Empty(ports.Calls);
    }

    [Theory]
    [InlineData("P12345678901")]
    [InlineData("\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9")]
    public async Task GetCoverage_PatientNoOfTwelveBytes_ReadsTheCoverage(string patientNo)
    {
        var ports = new FakePorts();

        var response = await Service(ports).GetCoverage(patientNo, null, new InvoiceEntryParameters(), Operator);

        Assert.Equal("GetPatientCoverage", ports.Calls[0]);
        Assert.DoesNotContain(response.Messages, message => message.Text.EndsWith("can be bound.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("P123456789012", "PATIENTNO has 13 characters; at most 12 can be bound.")]
    [InlineData("\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9", "PATIENTNO has 14 bytes in UTF-8; at most 12 can be bound.")]
    public async Task GetLov_ReservNoPatientNoOverTwelveBytes_ReturnsBlockingPatientNoWithoutReads(string patientNo, string text)
    {
        var ports = new FakePorts();
        var binds = new Dictionary<string, string?> { ["DOCIDX"] = "12", ["PATIENTNO"] = patientNo };

        var response = await Service(ports).GetLov("RESERV_NO", binds, new DateTime(2026, 3, 31), Operator);

        Assert.NotNull(response);
        Assert.Equal("RESERV_NO", response.Name);
        Assert.Empty(response.Rows);
        AssertBlocking(response.Messages, "PATIENTNO", text);
        Assert.Empty(ports.Calls);
    }

    [Theory]
    [InlineData("P12345678901")]
    [InlineData(" P12345678901 ")]
    [InlineData("\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9")]
    public async Task GetLov_ReservNoPatientNoOfTwelveBytes_QueriesTheList(string patientNo)
    {
        var ports = new FakePorts();
        var binds = new Dictionary<string, string?> { ["DOCIDX"] = "12", ["PATIENTNO"] = patientNo };

        var response = await Service(ports).GetLov("RESERV_NO", binds, new DateTime(2026, 3, 31), Operator);

        Assert.NotNull(response);
        Assert.DoesNotContain(response.Messages, message => message.Field == "PATIENTNO");
        Assert.Equal(new[] { "ReservNo" }, ports.Calls);
    }

    [Theory]
    [Trait("Decision", "D-107")]
    [InlineData("SUB_COMPANY", "COMP_CODE", "12345678901", "COMP_CODE has 11 characters; at most 10 can be bound.")]
    [InlineData("SUB_COMPANY", "COMP_CODE", "\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9", "COMP_CODE has 12 bytes in UTF-8; at most 10 can be bound.")]
    [InlineData("THE_CLASS", "SUB_COMP_CODE", "12345678901", "SUB_COMP_CODE has 11 characters; at most 10 can be bound.")]
    [InlineData("THE_CLASS", "SUB_COMP_CODE", "\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9", "SUB_COMP_CODE has 12 bytes in UTF-8; at most 10 can be bound.")]
    public async Task GetLov_DependentCompanyListParentOverTenBytes_ReturnsBlockingParentWithoutReads(string name, string item, string value, string text)
    {
        var ports = new FakePorts();
        var binds = new Dictionary<string, string?> { [item] = value };

        var response = await Service(ports).GetLov(name, binds, null, Operator);

        Assert.NotNull(response);
        Assert.Equal(name, response.Name);
        Assert.Empty(response.Rows);
        AssertBlocking(response.Messages, item, text);
        Assert.Empty(ports.Calls);
    }

    [Theory]
    [Trait("Decision", "D-107")]
    [InlineData("SUB_COMPANY", "COMP_CODE", "1234567890", "SubCompany")]
    [InlineData("SUB_COMPANY", "COMP_CODE", " 1234567890 ", "SubCompany")]
    [InlineData("SUB_COMPANY", "COMP_CODE", "\u00E9\u00E9\u00E9\u00E9\u00E9", "SubCompany")]
    [InlineData("THE_CLASS", "SUB_COMP_CODE", "1234567890", "TheClass")]
    [InlineData("THE_CLASS", "SUB_COMP_CODE", "\u00E9\u00E9\u00E9\u00E9\u00E9", "TheClass")]
    public async Task GetLov_DependentCompanyListParentOfTenBytes_QueriesTheList(string name, string item, string value, string query)
    {
        var ports = new FakePorts();
        var binds = new Dictionary<string, string?> { [item] = value };

        var response = await Service(ports).GetLov(name, binds, null, Operator);

        Assert.NotNull(response);
        Assert.Empty(response.Messages);
        Assert.Equal(new[] { query }, ports.Calls);
    }

    [Theory]
    [Trait("Decision", "D-107")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("+12")]
    [InlineData("012")]
    [InlineData("1.5")]
    [InlineData("12a")]
    [InlineData("99999999999")]
    [InlineData(" 12")]
    [InlineData("12 ")]
    [InlineData(" 12 ")]
    [InlineData("\t12")]
    public async Task GetLov_ReservNoDoctorNotACanonicalPositiveNumber_ReturnsBlockingDocIdxWithoutReads(string docIdx)
    {
        var ports = new FakePorts();
        var binds = new Dictionary<string, string?> { ["DOCIDX"] = docIdx, ["PATIENTNO"] = "P1" };

        var response = await Service(ports).GetLov("RESERV_NO", binds, new DateTime(2026, 3, 31), Operator);

        Assert.NotNull(response);
        Assert.Empty(response.Rows);
        AssertBlocking(response.Messages, "DOCIDX", "DOCIDX must be a positive whole number.");
        Assert.Empty(ports.Calls);
    }

    [Theory]
    [Trait("Decision", "D-107")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("01")]
    [InlineData("3")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData(" 2 ")]
    [InlineData("abc")]
    public async Task GetLov_OffersPayTypeNotCashOrCredit_ReturnsBlockingPayTypeWithoutReads(string payType)
    {
        var ports = new FakePorts();
        var binds = new Dictionary<string, string?> { ["PAYTYPE"] = payType };

        var response = await Service(ports).GetLov("OFFERS", binds, new DateTime(2026, 3, 31), Operator);

        Assert.NotNull(response);
        Assert.Empty(response.Rows);
        AssertBlocking(response.Messages, "PAYTYPE", "PAYTYPE must be 1 (Cash) or 2 (Credit).");
        Assert.Empty(ports.Calls);
    }

    [Theory]
    [Trait("Decision", "D-107")]
    [InlineData("1")]
    [InlineData("2")]
    public async Task GetLov_OffersPayTypeCashOrCredit_QueriesTheList(string payType)
    {
        var ports = new FakePorts();
        var binds = new Dictionary<string, string?> { ["PAYTYPE"] = payType };

        var response = await Service(ports).GetLov("OFFERS", binds, new DateTime(2026, 3, 31), Operator);

        Assert.NotNull(response);
        Assert.Empty(response.Messages);
        Assert.Equal(new[] { "Offers" }, ports.Calls);
    }

    [Theory]
    [Trait("Decision", "D-77")]
    [InlineData("PATIENTNO", "P123456789012", "PATIENTNO has 13 characters; at most 12 can be bound.")]
    [InlineData("PATIENTNO", "\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9", "PATIENTNO has 14 bytes in UTF-8; at most 12 can be bound.")]
    [InlineData("COMP_CODE", "P123456789012", "PATIENTNO has 13 characters; at most 12 can be bound.")]
    public async Task Validate_PatientNoOverTwelveBytes_WritesFieldValidation422WithoutReads(string target, string patientNo, string text)
    {
        var ports = new FakePorts();
        var request = new ValidateDraftRequest
        {
            Target = target,
            Draft = new DraftDto { Header = new InvoiceHeaderDraft { PatientNo = patientNo, CompCode = "0" } },
        };

        var error = await Assert.ThrowsAsync<ArgumentException>(() => Service(ports).Validate(request, Operator));
        var written = await ProblemDetailsWriterTests.WriteHandled(error);

        AssertPatientNoRefused(written, text);
        Assert.Empty(ports.Calls);
    }

    [Theory]
    [Trait("Decision", "D-77")]
    [InlineData("P12345678901")]
    [InlineData("\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9")]
    public async Task Validate_PatientNoOfTwelveBytes_ReadsTheCoverage(string patientNo)
    {
        var ports = new FakePorts();
        var request = new ValidateDraftRequest
        {
            Target = "PATIENTNO",
            Draft = new DraftDto { Header = new InvoiceHeaderDraft { PatientNo = patientNo } },
        };

        var response = await Service(ports).Validate(request, Operator);

        Assert.Contains("GetPatientCoverage", ports.Calls);
        Assert.DoesNotContain(response.Messages, message => message.Text.EndsWith("can be bound.", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Decision", "D-77")]
    public async Task Preview_PatientNoOverTwelveBytes_WritesFieldValidation422WithoutReads()
    {
        var ports = new FakePorts();
        var draft = new DraftDto { Header = new InvoiceHeaderDraft { PatientNo = "P123456789012" } };

        var error = await Assert.ThrowsAsync<ArgumentException>(() => Service(ports).Preview(draft, Operator));
        var written = await ProblemDetailsWriterTests.WriteHandled(error);

        AssertPatientNoRefused(written, "PATIENTNO has 13 characters; at most 12 can be bound.");
        Assert.Empty(ports.Calls);
    }

    [Fact]
    [Trait("Decision", "D-77")]
    public async Task ImportRequests_PatientNoOverTwelveBytes_WritesFieldValidation422WithoutReads()
    {
        var ports = new FakePorts();

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => Service(ports).ImportRequests(RequestImport("P123456789012", "V1", 12), Operator));
        var written = await ProblemDetailsWriterTests.WriteHandled(error);

        AssertPatientNoRefused(written, "PATIENTNO has 13 characters; at most 12 can be bound.");
        Assert.Empty(ports.Calls);
    }

    [Theory]
    [Trait("Decision", "D-108")]
    [InlineData("CLAIM_NO", 'C', 41, "CLAIM_NO has 41 characters; at most 40 can be bound.")]
    [InlineData("CLAIM_NO", '\u00E9', 21, "CLAIM_NO has 42 bytes in UTF-8; at most 40 can be bound.")]
    [InlineData("VISIT_UNIQUE", '9', 40, "VISIT_UNIQUE has 40 characters; at most 39 can be bound.")]
    [InlineData("VISIT_UNIQUE", '\u00E9', 20, "VISIT_UNIQUE has 40 bytes in UTF-8; at most 39 can be bound.")]
    public async Task NewDraft_ParameterOverItsWidth_WritesFieldValidation422WithoutReads(string item, char character, int length, string text)
    {
        var ports = new FakePorts();

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => Service(ports).NewDraft(EntryParameters(item, new string(character, length)), Operator));
        var written = await ProblemDetailsWriterTests.WriteHandled(error);

        AssertWidthRefused(written, (item, text));
        Assert.Empty(ports.Calls);
    }

    [Theory]
    [Trait("Decision", "D-108")]
    [InlineData('C', 40)]
    [InlineData('\u00E9', 20)]
    public async Task NewDraft_ParametersAtTheirWidths_ReadTheClaimPreloadAndTheVisitDoctor(char character, int claimNoLength)
    {
        var ports = new FakePorts();
        var parameters = new InvoiceEntryParameters { ClaimNo = new string(character, claimNoLength), VisitUnique = new string('9', 39) };

        await Service(ports).NewDraft(parameters, Operator);

        Assert.Contains("GetClaimPreload", ports.Calls);
        Assert.Contains("GetVisitDoctor", ports.Calls);
    }

    [Theory]
    [Trait("Decision", "D-108")]
    [InlineData("CLAIM_NO", 'C', 41, "CLAIM_NO has 41 characters; at most 40 can be bound.")]
    [InlineData("CLAIM_NO", '\u00E9', 21, "CLAIM_NO has 42 bytes in UTF-8; at most 40 can be bound.")]
    [InlineData("VISIT_UNIQUE", '9', 40, "VISIT_UNIQUE has 40 characters; at most 39 can be bound.")]
    public async Task GetCoverage_ParameterOverItsWidth_ReturnsTheBlockingMessageWithoutLookups(string item, char character, int length, string text)
    {
        var ports = new FakePorts();

        var response = await Service(ports).GetCoverage("P1", null, EntryParameters(item, new string(character, length)), Operator);

        AssertBlocking(response.Messages, item, text);
        Assert.Null(response.Coverage);
        Assert.Empty(ports.Calls);
    }

    [Fact]
    [Trait("Decision", "D-108")]
    public async Task GetCoverage_ParametersAtTheirWidths_ReadTheCoverage()
    {
        var ports = new FakePorts();
        var parameters = new InvoiceEntryParameters { ClaimNo = new string('C', 40), VisitUnique = new string('9', 39) };

        var response = await Service(ports).GetCoverage("P1", null, parameters, Operator);

        Assert.Equal("GetPatientCoverage", ports.Calls[0]);
        Assert.DoesNotContain(response.Messages, message => message.Text.EndsWith("can be bound.", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Decision", "D-108")]
    [InlineData("validate", "CLAIM_NO", 'C', 41, "CLAIM_NO has 41 characters; at most 40 can be bound.")]
    [InlineData("validate", "VISIT_UNIQUE", '9', 40, "VISIT_UNIQUE has 40 characters; at most 39 can be bound.")]
    [InlineData("preview", "CLAIM_NO", '\u00E9', 21, "CLAIM_NO has 42 bytes in UTF-8; at most 40 can be bound.")]
    [InlineData("preview", "VISIT_UNIQUE", '9', 40, "VISIT_UNIQUE has 40 characters; at most 39 can be bound.")]
    public async Task DraftRequest_ParameterOverItsWidth_WritesFieldValidation422WithoutReads(
        string operation,
        string item,
        char character,
        int length,
        string text)
    {
        var ports = new FakePorts();
        var draft = new DraftDto
        {
            Header = new InvoiceHeaderDraft { PatientNo = "P1" },
            Parameters = EntryParameters(item, new string(character, length)),
        };

        var error = await Assert.ThrowsAsync<ArgumentException>(() => DraftRequest(Service(ports), operation, draft));
        var written = await ProblemDetailsWriterTests.WriteHandled(error);

        AssertWidthRefused(written, (item, text));
        Assert.Empty(ports.Calls);
    }

    [Fact]
    [Trait("Decision", "D-108")]
    public async Task Validate_PatientParametersAtTheirWidths_ReadTheClaimPreload()
    {
        var ports = new FakePorts();
        var request = new ValidateDraftRequest
        {
            Target = "PATIENTNO",
            Draft = new DraftDto
            {
                Header = new InvoiceHeaderDraft { PatientNo = "P1" },
                Parameters = new InvoiceEntryParameters { ClaimNo = new string('C', 40), VisitUnique = new string('9', 39) },
            },
        };

        await Service(ports).Validate(request, Operator);

        Assert.Equal("GetClaimPreload", ports.Calls[0]);
    }

    [Theory]
    [Trait("Decision", "D-108")]
    [InlineData('C', 11, "COMP_CODE has 11 characters; at most 10 can be bound.")]
    [InlineData('\u00E9', 6, "COMP_CODE has 12 bytes in UTF-8; at most 10 can be bound.")]
    public async Task Validate_CompCodeOverItsWidth_WritesFieldValidation422WithoutReads(char character, int length, string text)
    {
        var ports = new FakePorts();
        var request = new ValidateDraftRequest
        {
            Target = "COMP_CODE",
            Draft = new DraftDto { Header = new InvoiceHeaderDraft { PatientNo = "P1", CompCode = new string(character, length) } },
        };

        var error = await Assert.ThrowsAsync<ArgumentException>(() => Service(ports).Validate(request, Operator));
        var written = await ProblemDetailsWriterTests.WriteHandled(error);

        AssertWidthRefused(written, ("COMP_CODE", text));
        Assert.Empty(ports.Calls);
    }

    [Fact]
    [Trait("Decision", "D-108")]
    public async Task Validate_CompCodeAtItsWidth_ReadsTheCompanyType()
    {
        var ports = new FakePorts();
        var request = new ValidateDraftRequest
        {
            Target = "COMP_CODE",
            Draft = new DraftDto { Header = new InvoiceHeaderDraft { PatientNo = "P1", CompCode = new string('C', 10) } },
        };

        await Service(ports).Validate(request, Operator);

        Assert.Contains("GetCompanyType", ports.Calls);
    }

    [Theory]
    [Trait("Decision", "D-108")]
    [InlineData('S', 21, "SERVICEID on line 1 has 21 characters; at most 20 can be bound.")]
    [InlineData('\u00E9', 11, "SERVICEID on line 1 has 22 bytes in UTF-8; at most 20 can be bound.")]
    public async Task Preview_LineServiceIdOverItsWidth_WritesFieldValidation422NamingTheLineWithoutReads(char character, int length, string text)
    {
        var ports = new FakePorts();
        var draft = new DraftDto
        {
            Header = new InvoiceHeaderDraft { PatientNo = "P1" },
            Lines = new[] { new InvoiceLineDraft { ServiceId = new string(character, length), Qty = 1m, ClientId = "C1" } },
        };

        var error = await Assert.ThrowsAsync<ArgumentException>(() => Service(ports).Preview(draft, Operator));
        var written = await ProblemDetailsWriterTests.WriteHandled(error);

        AssertWidthRefused(written, ("SERVICEID", text));
        Assert.Empty(ports.Calls);
    }

    [Fact]
    [Trait("Decision", "D-108")]
    public async Task Preview_LineServiceIdAtItsWidth_ProceedsToTheReads()
    {
        var ports = new FakePorts();
        var draft = new DraftDto
        {
            Header = new InvoiceHeaderDraft { PatientNo = "P1" },
            Lines = new[] { new InvoiceLineDraft { ServiceId = new string('S', 20), Qty = 1m, ClientId = "C1" } },
        };

        var failure = await Record.ExceptionAsync(() => Service(ports).Preview(draft, Operator));

        Assert.False(failure is ArgumentException { Data: var data } && data.Contains(ProblemDetailsWriter.MessagesDataKey));
        Assert.NotEmpty(ports.Calls);
    }

    [Fact]
    [Trait("Decision", "D-108")]
    public async Task ImportRequests_VisitUniqueOverItsWidth_WritesFieldValidation422WithoutReads()
    {
        var ports = new FakePorts();

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => Service(ports).ImportRequests(RequestImport("P1", new string('9', 40), docId: 5), Operator));
        var written = await ProblemDetailsWriterTests.WriteHandled(error);

        AssertWidthRefused(written, ("VISIT_UNIQUE", "VISIT_UNIQUE has 40 characters; at most 39 can be bound."));
        Assert.Empty(ports.Calls);
    }

    [Fact]
    [Trait("Decision", "D-108")]
    public async Task ImportRequests_VisitUniqueAtItsWidth_ReadsTheSelectedRequestRows()
    {
        var ports = new FakePorts();

        await Service(ports).ImportRequests(RequestImport("P1", new string('9', 39), docId: 5), Operator);

        Assert.Contains("GetSelectedRequestRows", ports.Calls);
    }

    [Theory]
    [Trait("Decision", "D-108")]
    [InlineData('S', 21, "SERVICEID has 21 characters; at most 20 can be bound.")]
    [InlineData('\u00E9', 11, "SERVICEID has 22 bytes in UTF-8; at most 20 can be bound.")]
    public async Task ImportPackage_PackageServiceIdOverItsWidth_ReturnsBlockingServiceIdMessageWithoutReads(char character, int length, string text)
    {
        var ports = new FakePorts();

        var response = await Service(ports).ImportPackage(
            new PackageImportRequest { PackageServiceId = " " + new string(character, length) + " " },
            Operator);

        AssertBlocking(response.Messages, "SERVICEID", text);
        Assert.Empty(response.Lines);
        Assert.Null(response.Result);
        Assert.Empty(ports.Calls);
    }

    [Fact]
    [Trait("Decision", "D-108")]
    public async Task ImportPackage_TrimmedPackageServiceIdAtItsWidth_ProceedsToTheReads()
    {
        var ports = new FakePorts();

        var failure = await Record.ExceptionAsync(() => Service(ports).ImportPackage(
            new PackageImportRequest { PackageServiceId = " " + new string('S', 20) + " " },
            Operator));

        Assert.False(failure is ArgumentException { Data: var data } && data.Contains(ProblemDetailsWriter.MessagesDataKey));
        Assert.NotEmpty(ports.Calls);
    }

    [Fact]
    [Trait("Decision", "D-108")]
    public async Task Validate_SeveralItemsOverTheirWidths_WritesEveryMessageInItemThenLineOrder()
    {
        var ports = new FakePorts();
        var request = new ValidateDraftRequest
        {
            Target = "RECORD",
            Draft = new DraftDto
            {
                Header = new InvoiceHeaderDraft { PatientNo = "P123456789012", CompCode = new string('C', 11) },
                Parameters = new InvoiceEntryParameters { ClaimNo = new string('C', 41), VisitUnique = new string('9', 40) },
                Lines = new[]
                {
                    new InvoiceLineDraft { ServiceId = "S1", Qty = 1m, ClientId = "C1" },
                    new InvoiceLineDraft { ServiceId = new string('S', 21), Qty = 1m, ClientId = "C2" },
                },
            },
        };

        var error = await Assert.ThrowsAsync<ArgumentException>(() => Service(ports).Validate(request, Operator));
        var written = await ProblemDetailsWriterTests.WriteHandled(error);

        AssertWidthRefused(
            written,
            ("PATIENTNO", "PATIENTNO has 13 characters; at most 12 can be bound."),
            ("CLAIM_NO", "CLAIM_NO has 41 characters; at most 40 can be bound."),
            ("VISIT_UNIQUE", "VISIT_UNIQUE has 40 characters; at most 39 can be bound."),
            ("COMP_CODE", "COMP_CODE has 11 characters; at most 10 can be bound."),
            ("SERVICEID", "SERVICEID on line 2 has 21 characters; at most 20 can be bound."));
        Assert.Empty(ports.Calls);
    }

    [Theory]
    [Trait("Decision", "D-108")]
    [InlineData("CLAIM_NO", 'C', 41, "CLAIM_NO has 41 characters; at most 40 can be bound.")]
    [InlineData("VISIT_UNIQUE", '9', 40, "VISIT_UNIQUE has 40 characters; at most 39 can be bound.")]
    [InlineData("COMP_CODE", 'K', 11, "COMP_CODE has 11 characters; at most 10 can be bound.")]
    public async Task Create_ItemOverItsWidth_WritesFieldValidation422BeforeAnyDraftRead(string item, char character, int length, string text)
    {
        var ports = new FakePorts();
        var value = new string(character, length);
        var request = item == "COMP_CODE"
            ? CreateRequest(new InvoiceHeaderDraft { PatientNo = "P1", CompCode = value }, new InvoiceEntryParameters())
            : CreateRequest(new InvoiceHeaderDraft { PatientNo = "P1" }, EntryParameters(item, value));

        var error = await Assert.ThrowsAsync<ArgumentException>(() => Service(ports).Create(request, Operator));
        var written = await ProblemDetailsWriterTests.WriteHandled(error);

        AssertWidthRefused(written, (item, text));
        Assert.Equal(nameof(IInvoiceQueries.GetCreateRequest), ports.Calls[0]);
        Assert.DoesNotContain(nameof(IInvoiceQueries.GetClaimPreload), ports.Calls);
        Assert.DoesNotContain(nameof(ILookupQueries.GetPatientCoverage), ports.Calls);
        Assert.DoesNotContain(nameof(ILookupQueries.GetCompanyType), ports.Calls);
        Assert.DoesNotContain(nameof(ILookupQueries.GetVisitDoctor), ports.Calls);
        Assert.DoesNotContain(nameof(IOracleSessionFactory.Open), ports.Calls);
    }

    [Fact]
    [Trait("Decision", "D-108")]
    public async Task Create_ItemsAtTheirWidths_ReadTheClaimPreload()
    {
        var ports = new FakePorts();
        var request = CreateRequest(
            new InvoiceHeaderDraft { PatientNo = "P1", CompCode = new string('K', 10) },
            new InvoiceEntryParameters { ClaimNo = new string('C', 40), VisitUnique = new string('9', 39) });

        await Service(ports).Create(request, Operator);

        Assert.Contains(nameof(IInvoiceQueries.GetClaimPreload), ports.Calls);
    }

    /// <summary>Header items over their Form widths in characters and in UTF-8 bytes, for validate and preview.</summary>
    public static TheoryData<string, string, string, string> HeaderItemsOverTheirWidths => DraftOperations(
        ("CURR_CODE", "SARX", "CURR_CODE has 4 characters; at most 3 can be bound."),
        ("CURR_CODE", "\u00E9\u00E9", "CURR_CODE has 4 bytes in UTF-8; at most 3 can be bound."),
        ("CLAIM_FLAG", "OOO", "CLAIM_FLAG has 3 characters; at most 2 can be bound."),
        ("CLAIM_FLAG", "\u00E9\u00E9", "CLAIM_FLAG has 4 bytes in UTF-8; at most 2 can be bound."),
        ("NOTE_NO", new string('N', 41), "NOTE_NO has 41 characters; at most 40 can be bound."),
        ("NOTE_NO", new string('\u0627', 21), "NOTE_NO has 42 bytes in UTF-8; at most 40 can be bound."));

    /// <summary>Line items over their Form widths in characters and in UTF-8 bytes, for validate and preview.</summary>
    public static TheoryData<string, string, string, string> LineItemsOverTheirWidths => DraftOperations(
        ("LDISCT", "VV", "LDISCT on line 1 has 2 characters; at most 1 can be bound."),
        ("LDISCT", "\u00E9", "LDISCT on line 1 has 2 bytes in UTF-8; at most 1 can be bound."),
        ("TEETH_NO", new string('1', 500), "TEETH_NO on line 1 has 500 characters; at most 2 can be bound."),
        ("TEETH_NO", "\u00E9\u00E9", "TEETH_NO on line 1 has 4 bytes in UTF-8; at most 2 can be bound."),
        ("TOOTH_SURFACE", "MODBLFXY", "TOOTH_SURFACE on line 1 has 8 characters; at most 7 can be bound."),
        ("TOOTH_SURFACE", "\u00E9\u00E9\u00E9\u00E9", "TOOTH_SURFACE on line 1 has 8 bytes in UTF-8; at most 7 can be bound."),
        ("TEETH_NO2", "123", "TEETH_NO2 on line 1 has 3 characters; at most 2 can be bound."),
        ("TEETH_NO2", "\u00E9\u00E9", "TEETH_NO2 on line 1 has 4 bytes in UTF-8; at most 2 can be bound."),
        ("APPROV_REF_NO", new string('R', 5000), "APPROV_REF_NO on line 1 has 5000 characters; at most 20 can be bound."),
        ("APPROV_REF_NO", new string('\u00E9', 11), "APPROV_REF_NO on line 1 has 22 bytes in UTF-8; at most 20 can be bound."));

    [Theory]
    [Trait("Decision", "D-108")]
    [MemberData(nameof(HeaderItemsOverTheirWidths))]
    public async Task DraftRequest_HeaderItemOverItsWidth_WritesFieldValidation422OnTheItemWithoutReads(
        string operation,
        string item,
        string value,
        string text)
    {
        var ports = new FakePorts();
        var draft = new DraftDto { Header = HeaderWith(item, value) };

        var error = await Assert.ThrowsAsync<ArgumentException>(() => DraftRequest(Service(ports), operation, draft));
        var written = await ProblemDetailsWriterTests.WriteHandled(error);

        AssertWidthRefused(written, (item, text));
        Assert.Empty(ports.Calls);
    }

    [Theory]
    [Trait("Decision", "D-108")]
    [MemberData(nameof(LineItemsOverTheirWidths))]
    public async Task DraftRequest_LineItemOverItsWidth_WritesFieldValidation422OnTheItemNamingTheLineWithoutReads(
        string operation,
        string item,
        string value,
        string text)
    {
        var ports = new FakePorts();
        var draft = new DraftDto
        {
            Header = new InvoiceHeaderDraft { PatientNo = "P1" },
            Lines = new[] { LineWith(item, value) },
        };

        var error = await Assert.ThrowsAsync<ArgumentException>(() => DraftRequest(Service(ports), operation, draft));
        var written = await ProblemDetailsWriterTests.WriteHandled(error);

        AssertWidthRefused(written, (item, text));
        Assert.Empty(ports.Calls);
    }

    [Theory]
    [Trait("Decision", "D-108")]
    [InlineData("validate")]
    [InlineData("preview")]
    public async Task DraftRequest_LineItemOverItsWidthAfterALineWithoutService_NamesTheDraftLine(string operation)
    {
        var ports = new FakePorts();
        var draft = new DraftDto
        {
            Header = new InvoiceHeaderDraft { PatientNo = "P1" },
            Lines = new[]
            {
                new InvoiceLineDraft { ServiceId = " ", Qty = 1m, ClientId = "C0" },
                LineWith("TEETH_NO", "123"),
            },
        };

        var error = await Assert.ThrowsAsync<ArgumentException>(() => DraftRequest(Service(ports), operation, draft));
        var written = await ProblemDetailsWriterTests.WriteHandled(error);

        AssertWidthRefused(written, ("TEETH_NO", "TEETH_NO on line 2 has 3 characters; at most 2 can be bound."));
        Assert.Empty(ports.Calls);
    }

    [Theory]
    [Trait("Decision", "D-108")]
    [InlineData('A')]
    [InlineData('\u00E9')]
    public async Task Preview_HeaderAndLineItemsAtTheirWidths_ProceedToTheReads(char character)
    {
        var ports = new FakePorts();
        var draft = new DraftDto
        {
            Header = new InvoiceHeaderDraft
            {
                PatientNo = "P1",
                CurrCode = AtWidth(character, 3),
                ClaimFlag = AtWidth(character, 2),
                NoteNo = AtWidth(character, 40),
            },
            Lines = new[]
            {
                new InvoiceLineDraft
                {
                    ServiceId = "S1",
                    Qty = 1m,
                    ClientId = "C1",
                    DiscountType = AtWidth(character, 1),
                    TeethNo = AtWidth(character, 2),
                    ToothSurface = AtWidth(character, 7),
                    TeethNo2 = AtWidth(character, 2),
                    ApprovRefNo = AtWidth(character, 20),
                },
            },
        };

        var failure = await Record.ExceptionAsync(() => Service(ports).Preview(draft, Operator));

        Assert.False(failure is ArgumentException { Data: var data } && data.Contains(ProblemDetailsWriter.MessagesDataKey));
        Assert.NotEmpty(ports.Calls);
    }

    [Fact]
    [Trait("Decision", "D-108")]
    public async Task Validate_EveryHeldItemOverItsWidth_WritesTheHeaderMessagesThenEachLineInItemOrder()
    {
        var ports = new FakePorts();
        var overWidthLine = new InvoiceLineDraft
        {
            ServiceId = new string('S', 21),
            Qty = 1m,
            DiscountType = "VV",
            TeethNo = "123",
            ToothSurface = "MODBLFXY",
            TeethNo2 = "123",
            ApprovRefNo = new string('R', 21),
        };
        var request = new ValidateDraftRequest
        {
            Target = "RECORD",
            Draft = new DraftDto
            {
                Header = new InvoiceHeaderDraft
                {
                    PatientNo = "P123456789012",
                    CompCode = new string('C', 11),
                    CurrCode = "SARX",
                    ClaimFlag = "OOO",
                    NoteNo = new string('N', 41),
                },
                Parameters = new InvoiceEntryParameters { ClaimNo = new string('C', 41), VisitUnique = new string('9', 40) },
                Lines = new[] { overWidthLine with { ClientId = "C1" }, overWidthLine with { ClientId = "C2" } },
            },
        };
        static (string Field, string Text)[] LineMessages(int line) =>
        [
            ("SERVICEID", $"SERVICEID on line {line} has 21 characters; at most 20 can be bound."),
            ("LDISCT", $"LDISCT on line {line} has 2 characters; at most 1 can be bound."),
            ("TEETH_NO", $"TEETH_NO on line {line} has 3 characters; at most 2 can be bound."),
            ("TOOTH_SURFACE", $"TOOTH_SURFACE on line {line} has 8 characters; at most 7 can be bound."),
            ("TEETH_NO2", $"TEETH_NO2 on line {line} has 3 characters; at most 2 can be bound."),
            ("APPROV_REF_NO", $"APPROV_REF_NO on line {line} has 21 characters; at most 20 can be bound."),
        ];

        var error = await Assert.ThrowsAsync<ArgumentException>(() => Service(ports).Validate(request, Operator));
        var written = await ProblemDetailsWriterTests.WriteHandled(error);

        (string Field, string Text)[] header =
        [
            ("PATIENTNO", "PATIENTNO has 13 characters; at most 12 can be bound."),
            ("CLAIM_NO", "CLAIM_NO has 41 characters; at most 40 can be bound."),
            ("VISIT_UNIQUE", "VISIT_UNIQUE has 40 characters; at most 39 can be bound."),
            ("COMP_CODE", "COMP_CODE has 11 characters; at most 10 can be bound."),
            ("CURR_CODE", "CURR_CODE has 4 characters; at most 3 can be bound."),
            ("CLAIM_FLAG", "CLAIM_FLAG has 3 characters; at most 2 can be bound."),
            ("NOTE_NO", "NOTE_NO has 41 characters; at most 40 can be bound."),
        ];
        AssertWidthRefused(written, [.. header, .. LineMessages(1), .. LineMessages(2)]);
        Assert.Empty(ports.Calls);
    }

    [Theory]
    [Trait("Decision", "D-108")]
    [InlineData("CURR_CODE", 'C', 4, "CURR_CODE has 4 characters; at most 3 can be bound.")]
    [InlineData("CLAIM_FLAG", 'O', 3, "CLAIM_FLAG has 3 characters; at most 2 can be bound.")]
    [InlineData("NOTE_NO", 'N', 41, "NOTE_NO has 41 characters; at most 40 can be bound.")]
    [InlineData("NOTE_NO", '\u0627', 21, "NOTE_NO has 42 bytes in UTF-8; at most 40 can be bound.")]
    public async Task Create_HeaderItemOverItsWidth_WritesFieldValidation422OnTheItemBeforeAnyDraftRead(string item, char character, int length, string text)
    {
        var ports = new FakePorts();
        var request = CreateRequest(HeaderWith(item, new string(character, length)), new InvoiceEntryParameters());

        var error = await Assert.ThrowsAsync<ArgumentException>(() => Service(ports).Create(request, Operator));
        var written = await ProblemDetailsWriterTests.WriteHandled(error);

        AssertWidthRefused(written, (item, text));
        Assert.Equal(nameof(IInvoiceQueries.GetCreateRequest), ports.Calls[0]);
        Assert.DoesNotContain(nameof(IInvoiceQueries.GetClaimPreload), ports.Calls);
        Assert.DoesNotContain(nameof(ILookupQueries.GetPatientCoverage), ports.Calls);
        Assert.DoesNotContain(nameof(ILookupQueries.GetCompanyType), ports.Calls);
        Assert.DoesNotContain(nameof(ILookupQueries.GetVisitDoctor), ports.Calls);
        Assert.DoesNotContain(nameof(IOracleSessionFactory.Open), ports.Calls);
    }

    /// <summary>Returns each (item, value, text) case once per draft-carrying operation, validate first.</summary>
    private static TheoryData<string, string, string, string> DraftOperations(params (string Item, string Value, string Text)[] cases)
    {
        var data = new TheoryData<string, string, string, string>();
        foreach (var operation in new[] { "validate", "preview" })
        {
            foreach (var (item, value, text) in cases)
            {
                data.Add(operation, item, value, text);
            }
        }

        return data;
    }

    private static InvoiceHeaderDraft HeaderWith(string item, string value) => item switch
    {
        "CURR_CODE" => new InvoiceHeaderDraft { PatientNo = "P1", CurrCode = value },
        "CLAIM_FLAG" => new InvoiceHeaderDraft { PatientNo = "P1", ClaimFlag = value },
        "NOTE_NO" => new InvoiceHeaderDraft { PatientNo = "P1", NoteNo = value },
        _ => throw new ArgumentOutOfRangeException(nameof(item), item, null),
    };

    private static InvoiceLineDraft LineWith(string item, string value)
    {
        var line = new InvoiceLineDraft { ServiceId = "S1", Qty = 1m, ClientId = "C1" };
        return item switch
        {
            "LDISCT" => line with { DiscountType = value },
            "TEETH_NO" => line with { TeethNo = value },
            "TOOTH_SURFACE" => line with { ToothSurface = value },
            "TEETH_NO2" => line with { TeethNo2 = value },
            "APPROV_REF_NO" => line with { ApprovRefNo = value },
            _ => throw new ArgumentOutOfRangeException(nameof(item), item, null),
        };
    }

    /// <summary>Returns a value of exactly <paramref name="width"/> UTF-8 bytes: the character repeated, completed with 'A' where it cannot fill the width.</summary>
    private static string AtWidth(char character, int width)
    {
        var size = System.Text.Encoding.UTF8.GetByteCount(character.ToString());
        return new string(character, width / size) + new string('A', width % size);
    }

    private static CreateInvoiceRequest CreateRequest(InvoiceHeaderDraft header, InvoiceEntryParameters parameters) => new()
    {
        Draft = new DraftDto
        {
            RequestId = "0123456789ABCDEF0123456789ABCDEF",
            DraftSeal = FakePorts.DraftSeal,
            Header = header,
            Parameters = parameters,
        },
    };

    private static InvoiceEntryParameters EntryParameters(string item, string value) => item switch
    {
        "CLAIM_NO" => new InvoiceEntryParameters { ClaimNo = value },
        "VISIT_UNIQUE" => new InvoiceEntryParameters { VisitUnique = value },
        _ => throw new ArgumentOutOfRangeException(nameof(item), item, null),
    };

    private static Task DraftRequest(InvoiceWorkflowService service, string operation, DraftDto draft) => operation switch
    {
        "validate" => service.Validate(new ValidateDraftRequest { Target = "PATIENTNO", Draft = draft }, Operator),
        "preview" => service.Preview(draft, Operator),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
    };

    private static void AssertWidthRefused(
        (int Status, string? ContentType, System.Text.Json.JsonElement Body) written,
        params (string Field, string Text)[] expected)
    {
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, written.Status);
        Assert.Equal("field-validation", written.Body.GetProperty("type").GetString());
        var messages = written.Body.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(
            expected,
            messages.Select(message => (message.GetProperty("field").GetString() ?? string.Empty, message.GetProperty("text").GetString() ?? string.Empty)).ToArray());
        Assert.All(messages, message => Assert.Equal(ValidationMessage.Blocking, message.GetProperty("severity").GetString()));
    }

    private static void AssertPatientNoRefused((int Status, string? ContentType, System.Text.Json.JsonElement Body) written, string text)
    {
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, written.Status);
        Assert.Equal("field-validation", written.Body.GetProperty("type").GetString());
        var message = Assert.Single(written.Body.GetProperty("messages").EnumerateArray());
        Assert.Equal("PATIENTNO", message.GetProperty("field").GetString());
        Assert.Equal(text, message.GetProperty("text").GetString());
        Assert.Equal(ValidationMessage.Blocking, message.GetProperty("severity").GetString());
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

        /// <summary>Seal returned by every draft-date seal, so a request carrying it passes the issued-date check.</summary>
        public static string DraftSeal { get; } = new('A', 64);

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
                case nameof(IBilInvoiceApiGateway.SealDraftDate):
                    return DraftSeal;
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
