using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Tests.Data;

/// <summary>Entry rejections of <see cref="BilImportGateway"/> for destination widths, and the session type check that follows them.</summary>
[Trait("Category", "DataUnit")]
public sealed class BilImportGatewayTests
{
    private const int ApprovalCheckEnforced = 1;

    private static readonly DateTime DraftDate = new(2026, 9, 28, 10, 30, 0);

    [Theory]
    [InlineData("visit-unique", "visitUnique", "visit_unique has 40 characters; at most 39 can be bound.")]
    [InlineData("session-id", "operatorContext", "app_session_id has 4001 characters; at most 4000 can be bound.")]
    [InlineData("user-name", "operatorContext", "app_user has 4001 characters; at most 4000 can be bound.")]
    [InlineData("import-patient-no", "header", "patientno has 13 characters; at most 12 can be bound.")]
    [InlineData("visit-patient-no", "header", "patientno has 13 characters; at most 12 can be bound.")]
    [InlineData("visit-info-center-id", "operatorContext", "info_center_id has 11 characters; at most 10 can be bound.")]
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
    [InlineData("import-at-width")]
    [InlineData("visit-review")]
    [InlineData("visit-consultation-at-width")]
    public async Task BoundaryValue_PassesValidationAndReachesTheSessionTypeCheck(string boundaryCase)
    {
        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => Invoke(boundaryCase));

        Assert.Equal("session", error.ParamName);
        Assert.False(error.Data.Contains(OracleFailureTranslator.BindingRejectionKey));
    }

    [Theory]
    [InlineData(nameof(PlsqlBlocks.RequestImport))]
    [InlineData(nameof(PlsqlBlocks.VisitLine))]
    public void EngineLineBlock_BindsTheOutputCapacityAndReportsTheFullCount(string blockName)
    {
        var block = blockName == nameof(PlsqlBlocks.RequestImport) ? PlsqlBlocks.RequestImport : PlsqlBlocks.VisitLine;

        Assert.Contains("max_output_lines in pls_integer", block, StringComparison.Ordinal);
        Assert.Contains("max_output_lines => :max_output_lines", block, StringComparison.Ordinal);
        Assert.Contains("if v_engine.count > max_output_lines then", block, StringComparison.Ordinal);
        Assert.Contains("el_count := v_engine.count;", block, StringComparison.Ordinal);
        Assert.DoesNotContain("el_count := v_out;", block, StringComparison.Ordinal);
    }

    private static Task Invoke(string caseName)
    {
        var gateway = new BilImportGateway(new InvoicingDataOptions());
        var session = new FakeSession();
        IReadOnlyList<long> rowIds = [55L];

        return caseName switch
        {
            "visit-unique" => gateway.ImportRequestLines(session, Header(), Operator(), new string('9', 40), rowIds, ApprovalCheckEnforced),
            "session-id" => gateway.ImportRequestLines(session, Header(), Operator() with { SessionId = new string('a', 4001) }, "123", rowIds, ApprovalCheckEnforced),
            "user-name" => gateway.ImportRequestLines(session, Header(), Operator() with { UserName = new string('u', 4001) }, "123", rowIds, ApprovalCheckEnforced),
            "import-patient-no" => gateway.ImportRequestLines(session, Header() with { PatientNo = new string('9', 13) }, Operator(), "123", rowIds, ApprovalCheckEnforced),
            "import-at-width" => gateway.ImportRequestLines(
                session,
                Header() with { PatientNo = new string('9', 12) },
                Operator() with { SessionId = new string('a', 4000), UserName = new string('u', 4000) },
                new string('9', 39),
                rowIds,
                ApprovalCheckEnforced),
            "visit-patient-no" => gateway.GetVisitLine(session, Header() with { PatientNo = new string('9', 13) }, Operator(), VisitLineChoice.Review),
            "visit-info-center-id" => gateway.GetVisitLine(session, Header(), Operator() with { InfoCenterId = new string('7', 11) }, VisitLineChoice.Review),
            "visit-review" => gateway.GetVisitLine(session, Header(), Operator(), VisitLineChoice.Review),
            "visit-consultation-at-width" => gateway.GetVisitLine(
                session,
                Header() with { PatientNo = new string('9', 12) },
                Operator() with { InfoCenterId = new string('7', 10) },
                VisitLineChoice.Consultation),
            _ => throw new InvalidOperationException($"Unknown case '{caseName}'."),
        };
    }

    private static InvoiceHeaderDraft Header() => new()
    {
        PatientNo = "1001",
        PayType = 1,
        ClinicId = 14,
        DocId = 12,
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

    /// <summary>A session of another implementation; every member but disposal is unsupported.</summary>
    private sealed class FakeSession : IOracleSession
    {
        public Task Commit(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task Rollback(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void Rollback(string savepointName) => throw new NotSupportedException();

        public void Save(string savepointName) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => default;
    }
}
