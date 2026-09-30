using System.Globalization;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;
using Oracle.ManagedDataAccess.Client;
using Xunit.Abstractions;

namespace Billing.Invoicing.Tests.Oracle;

/// <summary>One bind round-trip per <see cref="PlsqlBlocks"/> anonymous block through the Data gateways, each in a session that is always rolled back. UNVERIFIED against Oracle.</summary>
[Trait("Category", "OracleIntegration")]
[Trait("Verification", "UNVERIFIED")]
public sealed class OracleBindingSmokeTests
{
    private const int ApplicationErrorFirst = -20999;
    private const int ApplicationErrorLast = -20000;
    private const int NoServiceLineError = -20903;

    private const int LineArrayCount = 35;
    private const string LineCountParameterName = "line_count";

    private const string PatientNo = "990000001";
    private const int CashPayType = 1;
    private const int CashPaymentMethod = 1;
    private const int PlaceholderDoctorId = 990001;
    private const int PlaceholderClinicId = 990001;
    private const string PlaceholderServiceId = "SMOKE-SVC-01";
    private const string PlaceholderClientId = "SMOKE-LINE-1";
    private const string PlaceholderPackageServiceId = "SMOKE-PKG-01";
    private const decimal PlaceholderListId = 990001m;
    private const decimal PlaceholderOfferId = 990001m;
    private const decimal BundleQty = 1m;
    private const string VisitUnique = "SMOKE-VISIT-990000001";

    private const string OperatorUserName = "SMOKE_TEST";
    private const string OperatorInfoCenterId = "1";
    private const string OperatorMachineName = "SMOKE-TEST";
    private const int OperatorUserNo = 1;

    private static readonly string[] EntryPackages = ["BIL_INVOICE_ENGINE", "BIL_INVOICE_API"];

    private readonly ITestOutputHelper _output;

    /// <summary>Stores the test output sink; touches no database.</summary>
    /// <param name="output">xUnit output for the application errors each round-trip accepts.</param>
    public OracleBindingSmokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [OracleFact]
    public async Task Preview_BindRoundTrip()
    {
        await RoundTrip(async (options, operatorContext, session) =>
        {
            var gateway = new BilInvoiceApiGateway(options);
            AssertEmptyLinePlaceholder();

            await Attempt(
                "CalculatePreview (no lines)",
                () => gateway.CalculatePreview(session, Header(), Array.Empty<InvoiceLineDraft>(), operatorContext, amount1Auto: true),
                preview => AssertPreview(preview.Lines, preview.Totals, options),
                NoServiceLineError);

            await Attempt(
                "CalculatePreview (one line)",
                () => gateway.CalculatePreview(session, Header(), [Line()], operatorContext, amount1Auto: true),
                preview => AssertPreview(preview.Lines, preview.Totals, options));
        });
    }

    [OracleFact]
    public async Task RequestImport_BindRoundTrip()
    {
        await RoundTrip(async (options, operatorContext, session) =>
        {
            var gateway = new BilImportGateway(options);
            var approvalMode = gateway.ApprovalCheckMode(null);
            Assert.Equal(1, approvalMode);

            await Attempt(
                "ImportRequestLines (no request rows)",
                () => gateway.ImportRequestLines(session, Header(), operatorContext, VisitUnique, Array.Empty<long>(), approvalMode),
                import =>
                {
                    AssertLineCount(import.Lines, options);
                    Assert.Empty(import.Lines);
                    AssertImportResult(import.Result);
                });
        });
    }

    [OracleFact]
    public async Task VisitLine_BindRoundTrip()
    {
        await RoundTrip(async (options, operatorContext, session) =>
        {
            var gateway = new BilImportGateway(options);

            await Attempt(
                "GetVisitLine (consultation)",
                () => gateway.GetVisitLine(session, VisitHeader(), operatorContext, VisitLineChoice.Consultation),
                visit =>
                {
                    _output.WriteLine($"GetVisitLine (consultation): {(visit.Line is null ? 0 : 1)} line.");
                    AssertImportResult(visit.Result);
                });
        });
    }

    [OracleFact]
    public async Task PackageLines_BindRoundTrip()
    {
        await RoundTrip(async (options, operatorContext, session) =>
        {
            var gateway = new BilInvoiceApiGateway(options);

            await Attempt(
                "GetPackageLines",
                () => gateway.GetPackageLines(session, PlaceholderPackageServiceId, PlaceholderListId, parentSourceId: null),
                package =>
                {
                    AssertLineCount(package.Lines, options);
                    AssertImportResult(package.Result);
                });
        });
    }

    [OracleFact]
    public async Task BundledOffer_BindRoundTrip()
    {
        await RoundTrip(async (options, operatorContext, session) =>
        {
            var gateway = new BilInvoiceApiGateway(options);

            await Attempt(
                "GetBundledOfferLines",
                () => gateway.GetBundledOfferLines(session, Header(), operatorContext, PlaceholderOfferId, BundleQty),
                offerLines => AssertLineCount(offerLines, options));
        });
    }

    [OracleFact(SideEffects = "Create")]
    [Trait("SideEffects", "Create")]
    public async Task Create_BindRoundTrip()
    {
        await RoundTrip(async (options, operatorContext, session) =>
        {
            var gateway = new BilInvoiceApiGateway(options);
            AssertEmptyLinePlaceholder();

            await Attempt(
                "CreateFullInvoice (no lines)",
                () => gateway.CreateFullInvoice(session, Header(), Array.Empty<InvoiceLineDraft>(), operatorContext, NewHexId()),
                result => AssertCreateResult(result, options),
                NoServiceLineError);

            await Attempt(
                "CreateFullInvoice (one line)",
                () => gateway.CreateFullInvoice(session, Header(), [Line()], operatorContext, NewHexId()),
                result => AssertCreateResult(result, options));
        });
    }

    /// <summary>Opens a session on the Oracle test connection with a synthetic operator, runs the gateway calls in it, and always rolls it back.</summary>
    /// <param name="calls">Gateway calls given the data-layer settings, the operator and the open session.</param>
    private static async Task RoundTrip(Func<InvoicingDataOptions, OperatorContext, IOracleSession, Task> calls)
    {
        var options = new InvoicingDataOptions { ConnectionString = OracleFactAttribute.ConnectionString! };
        var operatorContext = new OperatorContext
        {
            UserNo = OperatorUserNo,
            UserName = OperatorUserName,
            InfoCenterId = OperatorInfoCenterId,
            MachineName = OperatorMachineName,
            SessionId = NewHexId(),
        };

        await using var session = await new OracleSessionFactory(options).Open();
        try
        {
            await calls(options, operatorContext, session);
        }
        finally
        {
            await session.Rollback();
        }
    }

    /// <summary>Runs one gateway call and verifies its returned value, or accepts an application error the package raised and writes it to the test output.</summary>
    /// <param name="call">Name of the call used in output and assertion text.</param>
    /// <param name="invoke">The gateway call.</param>
    /// <param name="verifyReturn">Assertions on the value the call returned.</param>
    /// <param name="requiredEngineError">Application error the call must raise unless an ORA-06512 frame names a package other than BIL_INVOICE_ENGINE and BIL_INVOICE_API; null accepts any application error.</param>
    private async Task Attempt<T>(string call, Func<Task<T>> invoke, Action<T> verifyReturn, int? requiredEngineError = null)
    {
        T value;
        try
        {
            value = await invoke();
        }
        catch (OracleException exception)
        {
            var error = OracleErrorParser.FromException(exception);
            Assert.True(
                error.Number is >= ApplicationErrorFirst and <= ApplicationErrorLast,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{call}: Oracle error {error.Number} is not an application error; the bind round-trip failed. {error.Message}"));

            var sharedPackage = RaisedInSharedPackage(error);
            if (requiredEngineError is { } required && !sharedPackage)
            {
                Assert.True(
                    error.Number == required,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{call}: expected application error {required}, got {error.Number}. {error.Message}"));
            }

            _output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{call}: application error {error.Number} raised in {Origin(error)}{(sharedPackage ? " (shared package)" : string.Empty)}: {error.Text}"));
            return;
        }

        verifyReturn(value);
        _output.WriteLine($"{call}: returned.");
    }

    /// <summary>Asserts that zero draft lines bind as one-element PL/SQL associative arrays with a scalar line count of 0.</summary>
    private static void AssertEmptyLinePlaceholder()
    {
        var lines = Array.Empty<InvoiceLineDraft>();
        var lineParameters = LineInputBinder.Bind(lines);
        OracleParameter? clientIds = null;
        try
        {
            clientIds = ClientIdBinder.Bind(lines);
            var arrays = lineParameters
                .Where(parameter => parameter.ParameterName != LineCountParameterName)
                .ToList();
            Assert.Equal(LineArrayCount, arrays.Count);
            arrays.Add(clientIds);

            Assert.All(arrays, parameter =>
            {
                Assert.Equal(OracleCollectionType.PLSQLAssociativeArray, parameter.CollectionType);
                Assert.Equal(1, parameter.Size);
                Assert.Single(Assert.IsAssignableFrom<Array>(parameter.Value));
                Assert.Equal(OracleParameterStatus.NullInsert, Assert.Single(parameter.ArrayBindStatus));
            });

            var lineCount = Assert.Single(lineParameters, parameter => parameter.ParameterName == LineCountParameterName);
            Assert.Equal(OracleCollectionType.None, lineCount.CollectionType);
            Assert.Equal(0, Convert.ToInt32(lineCount.Value, CultureInfo.InvariantCulture));
        }
        finally
        {
            foreach (var parameter in lineParameters)
            {
                parameter.Dispose();
            }

            clientIds?.Dispose();
        }
    }

    /// <summary>Asserts the preview line count against the output capacity and a non-negative totals line count.</summary>
    private static void AssertPreview(IReadOnlyList<EditablePreviewLine> lines, PreviewTotalsRow totals, InvoicingDataOptions options)
    {
        AssertLineCount(lines, options);
        Assert.NotNull(totals);
        if (totals.LineCount is { } lineCount)
        {
            Assert.InRange(lineCount, 0, int.MaxValue);
        }
    }

    /// <summary>Asserts a create result was read with a line count between 0 and the output capacity.</summary>
    private static void AssertCreateResult(FullInvoiceResultRow result, InvoicingDataOptions options)
    {
        Assert.NotNull(result);
        if (result.LineCount is { } lineCount)
        {
            Assert.InRange(lineCount, 0, options.MaxOutputLines);
        }
    }

    /// <summary>Asserts that an OUT line collection holds between 0 and <see cref="InvoicingDataOptions.MaxOutputLines"/> lines.</summary>
    private static void AssertLineCount<TLine>(IReadOnlyList<TLine> lines, InvoicingDataOptions options)
    {
        Assert.NotNull(lines);
        Assert.InRange(lines.Count, 0, options.MaxOutputLines);
    }

    /// <summary>Asserts that every count the package set in an import result is non-negative.</summary>
    private static void AssertImportResult(ImportResultRow result)
    {
        Assert.NotNull(result);
        int?[] counts =
        [
            result.SourceCount,
            result.ImportedCount,
            result.SkippedRejectedCount,
            result.SkippedNeedApprovalCount,
            result.SkippedInvalidCount,
        ];

        foreach (var count in counts)
        {
            if (count is { } value)
            {
                Assert.InRange(value, 0, int.MaxValue);
            }
        }
    }

    /// <summary>True when an ORA-06512 frame of the error names a package other than BIL_INVOICE_ENGINE and BIL_INVOICE_API.</summary>
    private static bool RaisedInSharedPackage(OracleErrorInfo error) =>
        error.Frames.Any(frame => !EntryPackages.Contains(frame.Package, StringComparer.OrdinalIgnoreCase));

    /// <summary>Innermost package frame of the error as SCHEMA.PACKAGE:LINE, or "the anonymous block" when no frame names a package.</summary>
    private static string Origin(OracleErrorInfo error)
    {
        if (error.Frames.Count == 0)
        {
            return "the anonymous block";
        }

        var (schema, package, line) = error.Frames[0];
        var name = schema.Length == 0 ? package : $"{schema}.{package}";
        return string.Create(CultureInfo.InvariantCulture, $"{name}:{line}");
    }

    /// <summary>Synthetic cash header for patient 990000001 dated today, with no doctor or clinic.</summary>
    private static InvoiceHeaderDraft Header() => new()
    {
        PatientNo = PatientNo,
        InvDate = DateTime.Today,
        DraftDate = DateTime.Today,
        PayType = CashPayType,
        SubPayType = CashPaymentMethod,
    };

    /// <summary>Synthetic header carrying the placeholder doctor and clinic that the visit line reads.</summary>
    private static InvoiceHeaderDraft VisitHeader() => Header() with
    {
        DocId = PlaceholderDoctorId,
        ClinicId = PlaceholderClinicId,
    };

    /// <summary>Synthetic line of quantity 1 for the placeholder service.</summary>
    private static InvoiceLineDraft Line() => new()
    {
        ServiceId = PlaceholderServiceId,
        Qty = 1m,
        ClientId = PlaceholderClientId,
    };

    /// <summary>New 32-character upper-case hexadecimal id.</summary>
    private static string NewHexId() => Guid.NewGuid().ToString("N").ToUpperInvariant();
}
