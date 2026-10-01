using System.Globalization;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;
using Oracle.ManagedDataAccess.Client;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Billing.Invoicing.Tests.Oracle;

/// <summary>One bind round-trip per <see cref="PlsqlBlocks"/> anonymous block through the Data gateways, each in a session that is always rolled back; the create round-trip first requires the synthetic patient to have no PATIENT.PHONE_H. UNVERIFIED against Oracle.</summary>
[Trait("Category", "OracleIntegration")]
[Trait("Verification", "UNVERIFIED")]
public sealed class OracleBindingSmokeTests
{
    private const int LineArrayCount = 35;
    private const string LineCountParameterName = "line_count";

    private const string PatientNo = "990000001";
    private const string PatientMobileSql = "SELECT COUNT(*), COUNT(PHONE_H) FROM PATIENT WHERE PATIENTNO = :patientNo";
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

    private const string RequestSourceType = "REQUEST";
    private const string VisitSourceType = "NEW_VISIT";
    private const string PackageSourceType = "PACKAGE";
    private const string PackageParentRole = "PARENT";
    private const int BundleOfferType = 0;
    private const string BundleComponentRole = "COMPONENT";
    private const string NoFlag = "N";
    private const string EmptyRequestImportMessage = "Invoice request import completed. Expanded 0 request rows into 0 invoice lines.";

    private static readonly string[] EntryPackages = ["BIL_INVOICE_ENGINE", "BIL_INVOICE_API"];
    private static readonly string[] PaymentStatuses = ["No Amount Due", "Unpaid", "Partial", "Paid", "Overpaid"];

    private static readonly ExpectedError NoServiceLine =
        new("BIL_INVOICE_ENGINE", -20903, "Invoice create failed: at least one service line is required.");

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

            await Raises(
                "CalculatePreview (no lines)",
                () => gateway.CalculatePreview(session, Header(), Array.Empty<InvoiceLineDraft>(), operatorContext, amount1Auto: true),
                NoServiceLine,
                EntryPackages);

            await Returns(
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

            await Returns(
                "ImportRequestLines (no request rows)",
                () => gateway.ImportRequestLines(session, Header(), operatorContext, VisitUnique, Array.Empty<long>(), approvalMode),
                import =>
                {
                    AssertLineCount(import.Lines, options);
                    Assert.Empty(import.Lines);
                    AssertImportResult(import.Result);
                    Assert.Equal(RequestSourceType, import.Result.SourceType);
                    Assert.Equal(0, import.Result.SourceCount);
                    Assert.Equal(0, import.Result.ImportedCount);
                    Assert.Equal(EmptyRequestImportMessage, import.Result.Message);
                });
        });
    }

    [OracleFact]
    public async Task VisitLine_BindRoundTrip()
    {
        await RoundTrip(async (options, operatorContext, session) =>
        {
            var gateway = new BilImportGateway(options);

            await Returns(
                "GetVisitLine (consultation)",
                () => gateway.GetVisitLine(session, VisitHeader(), operatorContext, VisitLineChoice.Consultation),
                visit =>
                {
                    var line = visit.Line;
                    Assert.NotNull(line);
                    Assert.Equal(1m, line.Qty);
                    Assert.Equal(NoFlag, line.DiscountType);
                    AssertImportResult(visit.Result);
                    Assert.Equal(VisitSourceType, visit.Result.SourceType);
                    Assert.Equal(1, visit.Result.SourceCount);
                    Assert.Equal(1, visit.Result.ImportedCount);
                });
        });
    }

    [OracleFact]
    public async Task PackageLines_BindRoundTrip()
    {
        await RoundTrip(async (options, operatorContext, session) =>
        {
            var gateway = new BilInvoiceApiGateway(options);

            await Returns(
                "GetPackageLines",
                () => gateway.GetPackageLines(session, PlaceholderPackageServiceId, PlaceholderListId, parentSourceId: null),
                package =>
                {
                    AssertLineCount(package.Lines, options);
                    AssertImportResult(package.Result);
                    Assert.NotEmpty(package.Lines);
                    var parent = package.Lines[0];
                    Assert.Equal(PlaceholderPackageServiceId, parent.ServiceId);
                    Assert.Equal(PlaceholderPackageServiceId, parent.PackageServiceId);
                    Assert.Equal(PackageParentRole, parent.PackageLineRole);
                    Assert.Equal(1m, parent.Qty);
                    Assert.Equal(PackageSourceType, package.Result.SourceType);
                    Assert.Equal(package.Lines.Count, package.Result.ImportedCount);
                });
        });
    }

    [OracleFact]
    public async Task BundledOffer_BindRoundTrip()
    {
        await RoundTrip(async (options, operatorContext, session) =>
        {
            var gateway = new BilInvoiceApiGateway(options);

            await Returns(
                "GetBundledOfferLines",
                () => gateway.GetBundledOfferLines(session, Header(), operatorContext, PlaceholderOfferId, BundleQty),
                offerLines => AssertBundledOfferLines(offerLines, options));
        });
    }

    [OracleFact(SideEffects = "Create")]
    [Trait("SideEffects", "Create")]
    public async Task Create_BindRoundTrip()
    {
        await RoundTrip(async (options, operatorContext, session) =>
        {
            await AssertSyntheticPatientWithoutMobile(session, options);
            var gateway = new BilInvoiceApiGateway(options);
            AssertEmptyLinePlaceholder();

            await Raises(
                "CreateFullInvoice (no lines)",
                () => gateway.CreateFullInvoice(session, Header(), Array.Empty<InvoiceLineDraft>(), operatorContext, NewHexId()),
                NoServiceLine,
                EntryPackages);

            await Returns(
                "CreateFullInvoice (one line)",
                () => gateway.CreateFullInvoice(session, Header(), [Line()], operatorContext, NewHexId()),
                result => AssertCreateResult(result));
        });
    }

    /// <summary>Opens a session on the Oracle test connection with a synthetic operator, runs the gateway calls in it, and always rolls it back; a rollback failure after a failed call is reported together with that failure.</summary>
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
        catch (Exception callFailure)
        {
            try
            {
                await session.Rollback();
            }
            catch (Exception rollbackFailure)
            {
                throw new AggregateException(callFailure, rollbackFailure);
            }

            throw;
        }

        await session.Rollback();
    }

    /// <summary>Fails as a precondition unless PATIENT holds the synthetic patient and none of its rows has a PHONE_H value; reads only the two counts, inside the session's transaction.</summary>
    /// <param name="session">Open session whose connection and transaction the read runs in.</param>
    /// <param name="options">Data-layer settings supplying the command timeout.</param>
    private static async Task AssertSyntheticPatientWithoutMobile(IOracleSession session, InvoicingDataOptions options)
    {
        var oracleSession = Assert.IsType<OracleSession>(session);
        using var patientNo = new OracleParameter("patientNo", OracleDbType.Varchar2) { Value = PatientNo };
        await using var command = new OracleCommand(PatientMobileSql, oracleSession.Connection)
        {
            Transaction = oracleSession.Transaction,
            BindByName = true,
            CommandTimeout = options.CommandTimeoutSeconds,
        };
        command.Parameters.Add(patientNo);

        decimal patients;
        decimal mobiles;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync(), $"Precondition: the PATIENT count for {PatientNo} returned no row.");
            patients = reader.GetDecimal(0);
            mobiles = reader.GetDecimal(1);
        }

        if (patients == 0m)
        {
            throw FailException.ForFailure(
                $"Precondition: PATIENT {PatientNo} does not exist; {nameof(Create_BindRoundTrip)} needs the synthetic patient with no mobile number.");
        }

        if (mobiles > 0m)
        {
            throw FailException.ForFailure(
                $"Precondition: PATIENT {PatientNo} PHONE_H is not null; {nameof(Create_BindRoundTrip)} needs the synthetic patient with no mobile number.");
        }
    }

    /// <summary>Runs one gateway call that must return, verifies its returned value and writes the outcome to the test output; any Oracle error fails the test.</summary>
    /// <param name="call">Name of the call used in output and assertion text.</param>
    /// <param name="invoke">The gateway call.</param>
    /// <param name="verifyReturn">Assertions on the value the call returned.</param>
    private async Task Returns<T>(string call, Func<Task<T>> invoke, Action<T> verifyReturn)
    {
        T value;
        try
        {
            value = await invoke();
        }
        catch (OracleException exception)
        {
            var error = OracleErrorParser.FromException(exception);
            throw FailException.ForFailure(string.Create(
                CultureInfo.InvariantCulture,
                $"{call}: Oracle error {error.Number} raised in {Origin(error)}; a bind round-trip needs a successful return with its OUT values. {error.Text}"));
        }

        verifyReturn(value);
        _output.WriteLine($"{call}: returned.");
    }

    /// <summary>Runs one gateway call that must raise the expected application error inside the target packages, and writes the accepted error to the test output.</summary>
    /// <param name="call">Name of the call used in output and assertion text.</param>
    /// <param name="invoke">The gateway call.</param>
    /// <param name="expected">Package, number and message prefix the call must raise.</param>
    /// <param name="targetPackages">Packages every ORA-06512 frame of the error must name.</param>
    private async Task Raises<T>(string call, Func<Task<T>> invoke, ExpectedError expected, IReadOnlyCollection<string> targetPackages)
    {
        try
        {
            await invoke();
        }
        catch (OracleException exception)
        {
            var error = OracleErrorParser.FromException(exception);
            AssertAttributed(call, error, expected, targetPackages);
            _output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{call}: application error {error.Number} raised in {Origin(error)}: {error.Text}"));
            return;
        }

        throw FailException.ForFailure(string.Create(
            CultureInfo.InvariantCulture,
            $"{call}: expected {expected.Package} {expected.Number} '{expected.MessagePrefix}', but the call returned."));
    }

    /// <summary>Fails unless the error is the expected number and message prefix, raised after the connection opened, with at least one ORA-06512 frame, every frame in the target packages, and no trigger in its stack.</summary>
    /// <param name="call">Name of the call used in assertion text.</param>
    /// <param name="error">The parsed error the call raised.</param>
    /// <param name="expected">Package, number and message prefix the call must raise.</param>
    /// <param name="targetPackages">Packages every ORA-06512 frame of the error must name.</param>
    private static void AssertAttributed(string call, OracleErrorInfo error, ExpectedError expected, IReadOnlyCollection<string> targetPackages)
    {
        Assert.False(
            error.DuringOpen,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{call}: Oracle error {error.Number} was raised while opening the connection, not by {expected.Package}. {error.Message}"));

        Assert.True(
            error.Number == expected.Number,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{call}: expected {expected.Package} {expected.Number}, got Oracle error {error.Number} raised in {Origin(error)}. {error.Message}"));

        Assert.True(
            error.Text.StartsWith(expected.MessagePrefix, StringComparison.Ordinal),
            string.Create(
                CultureInfo.InvariantCulture,
                $"{call}: expected {expected.Package} {expected.Number} '{expected.MessagePrefix}', got Oracle error {error.Number} raised in {Origin(error)}. {error.Message}"));

        Assert.True(
            error.Frames.Count > 0,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{call}: Oracle error {error.Number} names no package frame, so it was not shown to be raised inside {string.Join(" or ", targetPackages)}. {error.Message}"));

        var foreignPackages = error.Frames
            .Where(frame => !targetPackages.Contains(frame.Package, StringComparer.OrdinalIgnoreCase))
            .Select(frame => frame.Schema.Length == 0 ? frame.Package : $"{frame.Schema}.{frame.Package}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.True(
            foreignPackages.Count == 0,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{call}: Oracle error {error.Number} raised in {Origin(error)} passed through {string.Join(", ", foreignPackages)}, outside {string.Join(" and ", targetPackages)}. {error.Message}"));

        var triggers = OracleErrorParser.ParseTriggers(error.Message)
            .Select(trigger => trigger.Schema.Length == 0 ? trigger.Name : $"{trigger.Schema}.{trigger.Name}")
            .ToList();
        Assert.True(
            triggers.Count == 0,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{call}: Oracle error {error.Number} raised in {Origin(error)} was raised during trigger {string.Join(", ", triggers)}. {error.Message}"));
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

    /// <summary>Asserts a one-line preview: one line within the output capacity carrying the bound client id, a totals line count of 1, and a payment status the package sets.</summary>
    private static void AssertPreview(IReadOnlyList<EditablePreviewLine> lines, PreviewTotalsRow totals, InvoicingDataOptions options)
    {
        AssertLineCount(lines, options);
        var line = Assert.Single(lines);
        Assert.Equal(PlaceholderClientId, line.ClientId);
        Assert.NotNull(totals);
        Assert.Equal(1, totals.LineCount);
        Assert.Contains(totals.PaymentStatus, PaymentStatuses);
    }

    /// <summary>Asserts a returned bundled offer: at least one line within the output capacity, each a bundle component (offer type 0) whose client id is its offer instance id and offer detail id joined by ':'.</summary>
    private static void AssertBundledOfferLines(IReadOnlyList<EditablePreviewLine> lines, InvoicingDataOptions options)
    {
        AssertLineCount(lines, options);
        Assert.NotEmpty(lines);
        Assert.All(lines, line =>
        {
            Assert.Equal(BundleOfferType, line.OfferType);
            Assert.Equal(BundleComponentRole, line.OfferLineRole?.Trim(), StringComparer.OrdinalIgnoreCase);
            Assert.NotNull(line.OfferInstanceId);
            Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"{line.OfferInstanceId}:{line.OfferDtlId}"), line.ClientId);
        });
    }

    /// <summary>Asserts a one-line create result: an invoice number, a line count of 1, the package's success message for that number, and no SMS sent or print URL built.</summary>
    private static void AssertCreateResult(FullInvoiceResultRow result)
    {
        Assert.NotNull(result);
        var invNo = Assert.NotNull(result.InvNo);
        Assert.Equal(1, result.LineCount);
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"Invoice {invNo} created successfully."), result.Message);
        Assert.Equal(NoFlag, result.SmsSent);
        Assert.Equal(NoFlag, result.PrintUrlBuilt);
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

    /// <summary>Application error a call must raise: the raising package, the signed ORA number and the start of the error text.</summary>
    private sealed record ExpectedError(string Package, int Number, string MessagePrefix);
}
