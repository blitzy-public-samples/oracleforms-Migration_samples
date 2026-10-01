using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Tests.Data;

/// <summary>Entry rejections of <see cref="BilInvoiceApiGateway"/> for empty or whitespace request ids, destination widths, preview capacity and create capacity, and the session type check that follows them.</summary>
[Trait("Category", "DataUnit")]
public sealed class BilInvoiceApiGatewayTests
{
    private const int MaxOutputLines = 2;
    private const string RequestId = "7D3F2C1B9A8E4F6D8C2B1A0F9E8D7C6B";
    private const string SealKey = "QmlsSW5WQXBpR2F0ZXdheVRlc3RzIGRyYWZ0LXNlYWwga2V5IDAx";

    private static readonly DateTime DraftDate = new(2026, 9, 28, 10, 30, 0);

    [Theory]
    [InlineData("request-id-characters", "requestId", "request_id has 65 characters; at most 64 can be bound.")]
    [InlineData("request-id-bytes", "requestId", "request_id has 66 bytes in UTF-8; at most 64 can be bound.")]
    [InlineData("package-service-id", "packageServiceId", "package_serviceid has 21 characters; at most 20 can be bound.")]
    [InlineData("parent-source-id", "parentSourceId", "parent_source_id has 101 characters; at most 100 can be bound.")]
    [InlineData("offer-patient-no", "header", "patientno has 13 characters; at most 12 can be bound.")]
    [InlineData("offer-info-center-id", "operatorContext", "info_center_id has 11 characters; at most 10 can be bound.")]
    [InlineData("preview-over-capacity", "lines", "The draft has 3 lines; a preview returns at most 2 lines (Invoicing:MaxOutputLines).")]
    [InlineData("create-over-capacity", "lines", "The draft expands to 3 invoice lines; an invoice can be created with at most 2 lines (Invoicing:MaxOutputLines).")]
    [InlineData("create-bundle-over-capacity", "lines", "The draft expands to 3 invoice lines; an invoice can be created with at most 2 lines (Invoicing:MaxOutputLines).")]
    public async Task Rejection_CarriesItsTextAndTranslatesToFormLevelFieldValidation(string rejectionCase, string paramName, string expectedText)
    {
        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => Invoke(rejectionCase));

        Assert.Equal(paramName, error.ParamName);
        Assert.Equal(expectedText, error.Data[OracleFailureTranslator.BindingRejectionKey]);

        DataFailure? failure = new OracleFailureTranslator().Translate(error);

        Assert.NotNull(failure);
        Assert.Equal(422, failure.Status);
        Assert.Equal("field-validation", failure.Type);
        Assert.Equal(expectedText, failure.Message);
        Assert.Null(failure.Field);
        Assert.Null(failure.Number);
        Assert.Null(failure.Package);
        Assert.Null(failure.Kind);
    }

    [Theory]
    [InlineData("request-id-at-width")]
    [InlineData("package-service-id-at-width")]
    [InlineData("parent-source-id-at-width")]
    [InlineData("offer-at-width")]
    [InlineData("preview-at-capacity")]
    [InlineData("create-at-capacity")]
    [InlineData("create-bundle-at-capacity")]
    public async Task BoundaryValue_PassesValidationAndReachesTheSessionTypeCheck(string boundaryCase)
    {
        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => Invoke(boundaryCase));

        Assert.Equal("session", error.ParamName);
        Assert.False(error.Data.Contains(OracleFailureTranslator.BindingRejectionKey));
    }

    [Theory]
    [Trait("Decision", "D-127")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateFullInvoice_EmptyOrWhitespaceRequestId_IsRefusedBeforeAnySessionUse(string requestId)
    {
        var gateway = new BilInvoiceApiGateway(new InvoicingDataOptions { MaxOutputLines = MaxOutputLines });

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => gateway.CreateFullInvoice(new FakeSession(), Header(), [], Operator(), requestId));

        Assert.Equal("requestId", error.ParamName);
        Assert.False(error.Data.Contains(OracleFailureTranslator.BindingRejectionKey));
    }

    [Fact]
    [Trait("Decision", "D-90")]
    public void Constructor_CommandTimeoutAtTheMaximum_IsAccepted()
    {
        var options = new InvoicingDataOptions { CommandTimeoutSeconds = InvoicingDataOptions.MaxCommandTimeoutSeconds };

        Assert.IsType<BilInvoiceApiGateway>(new BilInvoiceApiGateway(options));
    }

    [Theory]
    [Trait("Decision", "D-90")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(InvoicingDataOptions.MaxCommandTimeoutSeconds + 1)]
    [InlineData(int.MaxValue)]
    public void Constructor_CommandTimeoutOutOfRange_ThrowsNamingTheKey(int commandTimeoutSeconds)
    {
        var options = new InvoicingDataOptions { CommandTimeoutSeconds = commandTimeoutSeconds };

        var exception = Assert.Throws<ArgumentOutOfRangeException>("options", () => new BilInvoiceApiGateway(options));

        Assert.Equal(commandTimeoutSeconds, exception.ActualValue);
        Assert.Contains("Invoicing:CommandTimeoutSeconds", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MaxDraftLines_IsTheConfiguredOutputCapacity()
    {
        IBilInvoiceApiGateway gateway = new BilInvoiceApiGateway(new InvoicingDataOptions { MaxOutputLines = MaxOutputLines });

        Assert.Equal(MaxOutputLines, gateway.MaxDraftLines);
    }

    [Fact]
    [Trait("Decision", "D-39")]
    public void SealDraftDate_IsEqualOnEveryInstanceSharingTheConfiguredKey()
    {
        var issued = SealGateway(SealKey).SealDraftDate(RequestId, DraftDate);
        var afterRestart = SealGateway(SealKey).SealDraftDate(RequestId, DraftDate);

        Assert.Matches("^[0-9A-F]{64}$", issued);
        Assert.Equal(issued, afterRestart);
        Assert.NotEqual(issued, SealGateway(SealKey).SealDraftDate(RequestId, DraftDate.AddTicks(1)));
        Assert.NotEqual(issued, SealGateway(SealKey).SealDraftDate("8D3F2C1B9A8E4F6D8C2B1A0F9E8D7C6B", DraftDate));
    }

    [Fact]
    [Trait("Decision", "D-39")]
    public void SealDraftDate_DiffersUnderAnotherKeyAndWithoutAConfiguredKey()
    {
        var issued = SealGateway(SealKey).SealDraftDate(RequestId, DraftDate);

        Assert.NotEqual(issued, SealGateway(Convert.ToBase64String(new byte[BilInvoiceApiGateway.MinDraftSealKeyBytes])).SealDraftDate(RequestId, DraftDate));
        Assert.NotEqual(
            SealGateway(string.Empty).SealDraftDate(RequestId, DraftDate),
            SealGateway(string.Empty).SealDraftDate(RequestId, DraftDate));
    }

    [Theory]
    [Trait("Decision", "D-39")]
    [InlineData("not base64!")]
    [InlineData("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHg==")]
    public void Constructor_MalformedOrShortDraftSealKey_IsRejected(string draftSealKey)
    {
        var error = Assert.Throws<ArgumentException>(() => SealGateway(draftSealKey));

        Assert.Equal("options", error.ParamName);
        Assert.StartsWith(
            "Invoicing:DraftSealKey (InvoicingDataOptions.DraftSealKey) must be base64 of at least 32 bytes.",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewBlock_BindsTheOutputCapacityAndReportsTheFullPreviewCount()
    {
        Assert.Contains("max_output_lines in pls_integer", PlsqlBlocks.Preview, StringComparison.Ordinal);
        Assert.Contains("max_output_lines => :max_output_lines", PlsqlBlocks.Preview, StringComparison.Ordinal);
        Assert.Contains("if v_preview.count > max_output_lines then", PlsqlBlocks.Preview, StringComparison.Ordinal);
        Assert.Contains("pl_count := v_preview.count;", PlsqlBlocks.Preview, StringComparison.Ordinal);
        Assert.DoesNotContain("pl_count := v_out;", PlsqlBlocks.Preview, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(PlsqlBlocks.BundledOffer), "v_preview", "pl_count")]
    [InlineData(nameof(PlsqlBlocks.PackageLines), "v_engine", "el_count")]
    public void OutputBlock_BindsTheOutputCapacityAndReportsTheFullCount(string blockName, string collection, string countName)
    {
        var block = blockName == nameof(PlsqlBlocks.BundledOffer) ? PlsqlBlocks.BundledOffer : PlsqlBlocks.PackageLines;

        Assert.Contains("max_output_lines in pls_integer", block, StringComparison.Ordinal);
        Assert.Contains("max_output_lines => :max_output_lines", block, StringComparison.Ordinal);
        Assert.Contains($"if {collection}.count > max_output_lines then", block, StringComparison.Ordinal);
        Assert.Contains($"{countName} := {collection}.count;", block, StringComparison.Ordinal);
        Assert.DoesNotContain($"{countName} := v_out;", block, StringComparison.Ordinal);
    }

    private static Task Invoke(string caseName)
    {
        var gateway = new BilInvoiceApiGateway(new InvoicingDataOptions { MaxOutputLines = MaxOutputLines });
        var session = new FakeSession();

        return caseName switch
        {
            "request-id-characters" => gateway.CreateFullInvoice(session, Header(), [], Operator(), new string('A', 65)),
            "request-id-bytes" => gateway.CreateFullInvoice(session, Header(), [], Operator(), new string('\u00E9', 33)),
            "request-id-at-width" => gateway.CreateFullInvoice(session, Header(), [], Operator(), new string('A', 64)),
            "package-service-id" => gateway.GetPackageLines(session, new string('P', 21), 1m, null),
            "package-service-id-at-width" => gateway.GetPackageLines(session, new string('P', 20), 1m, null),
            "parent-source-id" => gateway.GetPackageLines(session, "PKG1", 1m, new string('S', 101)),
            "parent-source-id-at-width" => gateway.GetPackageLines(session, "PKG1", 1m, new string('S', 100)),
            "offer-patient-no" => gateway.GetBundledOfferLines(session, Header() with { PatientNo = new string('9', 13) }, Operator(), 3m, 1m),
            "offer-info-center-id" => gateway.GetBundledOfferLines(session, Header(), Operator() with { InfoCenterId = new string('7', 11) }, 3m, 1m),
            "offer-at-width" => gateway.GetBundledOfferLines(
                session,
                Header() with { PatientNo = new string('9', 12) },
                Operator() with { InfoCenterId = new string('7', 10) },
                3m,
                1m),
            "preview-over-capacity" => gateway.CalculatePreview(session, Header(), Lines(3), Operator(), amount1Auto: true),
            "preview-at-capacity" => gateway.CalculatePreview(session, Header(), Lines(2), Operator(), amount1Auto: true),
            "create-over-capacity" => gateway.CreateFullInvoice(session, Header(), Lines(3), Operator(), RequestId),
            "create-bundle-over-capacity" => gateway.CreateFullInvoice(session, Header(), BundleLines(" OFR-1", "OFR-1 "), Operator(), RequestId),
            "create-at-capacity" => gateway.CreateFullInvoice(session, Header(), Lines(2), Operator(), RequestId),
            "create-bundle-at-capacity" => gateway.CreateFullInvoice(session, Header(), BundleLines("OFR-1"), Operator(), RequestId),
            _ => throw new InvalidOperationException($"Unknown case '{caseName}'."),
        };
    }

    /// <summary>A gateway configured only with <paramref name="draftSealKey"/>, as a fresh process with that configuration builds it.</summary>
    private static BilInvoiceApiGateway SealGateway(string draftSealKey) =>
        new(new InvoicingDataOptions { DraftSealKey = draftSealKey });

    private static InvoiceHeaderDraft Header() => new()
    {
        PatientNo = "1001",
        PayType = 1,
        SubPayType = 1,
        ClinicId = 14,
        DocId = 12,
        CurrCode = "SAR",
        Amount1 = 100m,
        DraftDate = DraftDate,
    };

    private static OperatorContext Operator() => new()
    {
        UserNo = 501,
        UserName = "cashier1",
        InfoCenterId = "7",
        MachineName = "WS-FRONT-01",
        SessionId = Guid.NewGuid().ToString("N"),
    };

    private static IReadOnlyList<InvoiceLineDraft> Lines(int count) =>
        Enumerable.Range(1, count).Select(i => new InvoiceLineDraft { ServiceId = $"S{i}", ClientId = $"c-{i}", Qty = 1m }).ToArray();

    /// <summary>Visible components of one bundled offer, one per instance id given.</summary>
    private static IReadOnlyList<InvoiceLineDraft> BundleLines(params string[] offerInstanceIds) =>
        offerInstanceIds
            .Select((instanceId, i) => new InvoiceLineDraft
            {
                ServiceId = $"B{i + 1}",
                ClientId = $"b-{i + 1}",
                Qty = 1m,
                OfferId = 7,
                OfferType = 0,
                OfferInstanceId = instanceId,
                OfferLineRole = "COMPONENT",
            })
            .ToArray();

    /// <summary>A session of another implementation; every member but disposal is unsupported.</summary>
    private sealed class FakeSession : IOracleSession
    {
        public Task Commit(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task Rollback(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task Rollback(string savepointName, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task Save(string savepointName, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => default;
    }
}
