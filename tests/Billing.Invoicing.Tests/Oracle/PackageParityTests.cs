using System.Data;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Tests.Parity;
using Dapper;
using Oracle.ManagedDataAccess.Client;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Billing.Invoicing.Tests.Oracle;

/// <summary>Oracle integration parity tests for the package-resident rules PR-01 … PR-25, driven by the PR fixtures in rolled-back sessions. UNVERIFIED against Oracle.</summary>
[Trait("Category", "OracleParity")]
[Trait("Verification", "UNVERIFIED")]
public sealed class PackageParityTests
{
    private const string OperationKey = "operation";
    private const string HeaderKey = "header";
    private const string LinesKey = "lines";
    private const string OperatorKey = "operator";
    private const string OperatorContextKey = "operatorContext";
    private const string Amount1AutoKey = "amount1Auto";
    private const string RequestIdKey = "requestId";
    private const string SendSmsKey = "p_send_sms";
    private const string BuildPrintUrlKey = "p_build_print_url";
    private const string PackageArgumentsKey = "packageArguments";
    private const string ParametersKey = "parameters";
    private const string ApprovalModeKey = "approvalMode";
    private const string VisitUniqueKey = "visitUnique";
    private const string PatServReqRowIdsKey = "patServReqRowIds";
    private const string OfferIdKey = "offerId";
    private const string BundleQtyKey = "bundleQty";
    private const string PackageServiceIdKey = "packageServiceId";
    private const string ListIdKey = "listId";
    private const string ParentSourceIdKey = "parentSourceId";
    private const string VisitKindKey = "visitKind";

    private const string GatewayFlagValue = "N";
    private const string PassOutcome = "Pass";
    private const string ErrorOutcome = "Error";
    private const string UnknownPackage = "UNKNOWN";
    private const string PreconditionPrefix = "Precondition:";
    private const string CurrencyField = "curr_code";
    private const string RaisesField = "raises";

    private const string PatientPhoneSql = "SELECT PHONE_H FROM PATIENT WHERE PATIENTNO = :k0";

    private const string ReadBackSql =
        "SELECT OLD_OR_NEW, PFLAG, TO_NUMBER(THE_MONTH) AS THE_MONTH, TO_NUMBER(THE_YEAR) AS THE_YEAR FROM T_INV WHERE INV_NO = :invNo";

    private static readonly string[] ReadBackColumns = ["old_or_new", "pflag", "the_month", "the_year"];

    private static readonly string[] IngestedPackages =
    [
        OracleErrorCatalog.EnginePackage,
        OracleErrorCatalog.ApiPackage,
        OracleErrorCatalog.ImportPackage,
    ];

    private static readonly string[] RowEntryKeys = ["table", "key", "columns", "count", "rowCount", "exists", "absent"];

    private static readonly string[] PackageArgumentNames = [SendSmsKey, BuildPrintUrlKey];

    private static readonly string[] ContextMetadataKeys = ["name", "output", "key", "source", "package", "operation", "locator", "openItem"];

    private static readonly string[] ContextNameKeys = ["name", "output", "key"];

    private static readonly Regex IdentifierPattern = new(@"^[A-Za-z][A-Za-z0-9_$#]*\z", RegexOptions.CultureInvariant);

    private static readonly Regex TemplatePattern = new(@"\{(?<name>[A-Za-z][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant);

    private static readonly Regex ContextNamePattern = new(@"^(?<path>[^\[]*)(?:\[(?<service>[^\]]*)\])?\z", RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, Operation> OperationNames = new Dictionary<string, Operation>(StringComparer.Ordinal)
    {
        ["BilInvoiceApiGateway.CalculatePreview"] = Operation.Preview,
        ["BilInvoiceApiGateway.CreateFullInvoice"] = Operation.Create,
        ["BilInvoiceApiGateway.GetBundledOfferLines"] = Operation.BundledOffer,
        ["BilInvoiceApiGateway.GetPackageLines"] = Operation.PackageLines,
        ["BilImportGateway.ImportRequestLines"] = Operation.RequestImport,
        ["BilImportGateway.GetVisitLine"] = Operation.VisitLine,
    };

    private static readonly IReadOnlyDictionary<Operation, string[]> OperationInputs = new Dictionary<Operation, string[]>
    {
        [Operation.Preview] = [OperationKey, HeaderKey, LinesKey, OperatorKey, OperatorContextKey, Amount1AutoKey],
        [Operation.Create] =
            [OperationKey, HeaderKey, LinesKey, OperatorKey, OperatorContextKey, RequestIdKey, SendSmsKey, BuildPrintUrlKey, PackageArgumentsKey],
        [Operation.BundledOffer] = [OperationKey, HeaderKey, OperatorKey, OperatorContextKey, OfferIdKey, BundleQtyKey],
        [Operation.PackageLines] = [OperationKey, PackageServiceIdKey, ListIdKey, ParentSourceIdKey],
        [Operation.RequestImport] =
            [OperationKey, HeaderKey, OperatorKey, OperatorContextKey, VisitUniqueKey, PatServReqRowIdsKey, ApprovalModeKey, ParametersKey],
        [Operation.VisitLine] = [OperationKey, HeaderKey, OperatorKey, OperatorContextKey, VisitKindKey],
    };

    // ENGINE = 05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql, IMPORT = BIL_IMPORT.sql beside it.
    private static readonly IReadOnlyDictionary<string, ContextRelation> ContextRelations =
        new Dictionary<string, ContextRelation>(StringComparer.OrdinalIgnoreCase)
        {
            // ENGINE:1760 line list_id = service context list_id.
            ["bil_service_context.list_id"] = new("list_id", DeclaredValue),
            // ENGINE:281-289, 1761 line curr_code = upper_trim_to_null(service context curr_code).
            ["bil_service_context.curr_code"] = new(CurrencyField, DeclaredValue),
            // ENGINE:1781 line vat_rate = nvl(vat_rate, 0).
            ["bil_service_context.vat_rate"] = new("vat_rate", ZeroWhenNull),
            // ENGINE:1953, 1764 plan_discount_pct = nvl(plan_price_disc, 0) on a line with no standard offer, package or offer role.
            ["bil_service_context.plan_price_disc"] = new("plan_discount_pct", ZeroWhenNull, ObservedCondition: PlainLine),
            // ENGINE:177, 1807, 2436, 2520, 2535, 2643-2661; IMPORT:657-670 is_package 1 yields a PARENT line, any other value no package role.
            ["bil_service_context.is_package"] = new("package_line_role", PackageRole),
            // ENGINE:1039-1051, 1061-1063, 1937-1943, 1763 price = round(nvl(billing_price, 0), 3) on a line with no price override, standard offer, package or offer role.
            ["bil_price_rule.billing_price"] = new("price", RoundedPrice, NoPriceOverride, PlainLine),
            // ENGINE:1870-1872, 2079-2080, 1827-1828 allow_manual_discount = 'Y' only for allow_discount 'Y', on a line with no package role and no offer role but SERVICE.
            ["bil_price_rule.allow_discount"] = new("allow_manual_discount", YesOrNo, ObservedCondition: OwnPermissions),
            // ENGINE:1873-1875, 2079-2080, 1829-1830 allow_price_override = 'Y' only for allow_manual_price 'Y', on the same lines.
            ["bil_price_rule.allow_manual_price"] = new("allow_price_override", YesOrNo, ObservedCondition: OwnPermissions),
            // ENGINE:1777 line the_pay = patient share.
            ["bil_class_rule.the_pay"] = new("the_pay", DeclaredValue),
            ["bil_class_rule.patient_share"] = new("the_pay", DeclaredValue),
            // ENGINE:1778 line the_comp = company share.
            ["bil_class_rule.the_comp"] = new("the_comp", DeclaredValue),
            ["bil_class_rule.company_share"] = new("the_comp", DeclaredValue),
            // ENGINE:1877-1882, 1912 matched 'Y' on an offer-eligible line sets offer_type 1; ENGINE:1895-1900 an unmatched line keeps its offer_type whether or not bil_offer_rule was called.
            ["bil_offer_rule.matched"] = new("offer_type", OfferTypeOf, OfferEligible),
            // ENGINE:1917 offer_price_applied = offer_price on a standard-offer line.
            ["bil_offer_rule.offer_price"] = new("offer_price_applied", DeclaredValue, ObservedCondition: StandardOfferLine),
            // ENGINE:1918 offer_dis_applied = offer_dis on a standard-offer line.
            ["bil_offer_rule.offer_dis"] = new("offer_dis_applied", DeclaredValue, ObservedCondition: StandardOfferLine),
            // CreateFullInvoice returns each posting stage's flag.
            ["bil_payment.payment_posted"] = new("payment_posted", DeclaredValue),
            ["bil_queue_posting.queue_posted"] = new("queue_posted", DeclaredValue),
            ["bil_stock_posting.stock_posted"] = new("stock_posted", DeclaredValue),
            // ENGINE:3203-3207 create_invoice returns only past an open-shift assertion.
            ["bil_cashier_shift.shift_open"] = new("shift_open", DeclaredTrue, CreateOnly, Completion: true),
        };

    // A shared-package call declared not to raise is observed by the operation returning.
    private static readonly ContextRelation RaisesRelation = new(RaisesField, DeclaredFalse, Completion: true);

    private readonly ITestOutputHelper output;

    /// <summary>Stores the test output sink; touches no database.</summary>
    /// <param name="output">xUnit output helper receiving per-case notes.</param>
    public PackageParityTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    private enum Operation
    {
        Preview,
        Create,
        BundledOffer,
        PackageLines,
        RequestImport,
        VisitLine,
    }

    [OracleFact]
    [Trait("Rule", "PR-01")]
    public Task PR01_GrossAndPriceOverride() => RunAsync("PR-01", Operation.Preview);

    [OracleFact]
    [Trait("Rule", "PR-02")]
    public Task PR02_LineDiscounts() => RunAsync("PR-02", Operation.Preview);

    [OracleFact]
    [Trait("Rule", "PR-03")]
    public Task PR03_PlanDiscountAndDiscountability() => RunAsync("PR-03", Operation.Preview);

    [OracleFact(PendingOn = "OI-06")]
    [Trait("Rule", "PR-04")]
    public Task PR04_PayerShare() => RunAsync("PR-04", Operation.Preview);

    [OracleFact]
    [Trait("Rule", "PR-05")]
    public Task PR05_Vat() => RunAsync("PR-05", Operation.Preview);

    [OracleFact]
    [Trait("Rule", "PR-06")]
    public Task PR06_FinalDiscount() => RunAsync("PR-06", Operation.Preview);

    [OracleFact]
    [Trait("Rule", "PR-07")]
    public Task PR07_AmountDue() => RunAsync("PR-07", Operation.Preview);

    [OracleFact]
    [Trait("Rule", "PR-08")]
    public Task PR08_PaymentStatus() => RunAsync("PR-08", Operation.Preview);

    [OracleFact(PendingOn = "OI-19", SideEffects = OracleFactAttribute.CreateSideEffects)]
    [Trait("Rule", "PR-09")]
    [Trait("SideEffects", OracleFactAttribute.CreateSideEffects)]
    public Task PR09_InvoiceNumbering() => RunAsync("PR-09", Operation.Create);

    [OracleFact(PendingOn = "OI-01", SideEffects = OracleFactAttribute.CreateSideEffects)]
    [Trait("Rule", "PR-10")]
    [Trait("SideEffects", OracleFactAttribute.CreateSideEffects)]
    public Task PR10_OpenShiftGate() => RunAsync("PR-10", Operation.Create);

    [OracleFact(SideEffects = OracleFactAttribute.CreateSideEffects)]
    [Trait("Rule", "PR-11")]
    [Trait("SideEffects", OracleFactAttribute.CreateSideEffects)]
    public Task PR11_OldOrNew() => RunAsync("PR-11", Operation.Create);

    [OracleFact(SideEffects = OracleFactAttribute.CreateSideEffects)]
    [Trait("Rule", "PR-12")]
    [Trait("SideEffects", OracleFactAttribute.CreateSideEffects)]
    public Task PR12_PflagMonthYear() => RunAsync("PR-12", Operation.Create);

    [OracleFact]
    [Trait("Rule", "PR-13")]
    public Task PR13_HeaderValidation() => RunAsync("PR-13", Operation.Preview);

    [OracleFact]
    [Trait("Rule", "PR-14")]
    public Task PR14_LineValidation() => RunAsync("PR-14", Operation.Preview);

    [OracleFact]
    [Trait("Rule", "PR-15")]
    public Task PR15_Currency() => RunAsync("PR-15", Operation.Preview);

    [OracleFact]
    [Trait("Rule", "PR-16")]
    public Task PR16_StandardOffers() => RunAsync("PR-16", Operation.Preview);

    [OracleFact]
    [Trait("Rule", "PR-17")]
    public Task PR17_BundledOffer() => RunAsync("PR-17", Operation.BundledOffer);

    [OracleFact]
    [Trait("Rule", "PR-18")]
    public Task PR18_PackageExpansion() => RunAsync("PR-18", Operation.PackageLines);

    [OracleFact]
    [Trait("Rule", "PR-19")]
    public Task PR19_RequestImport() => RunAsync("PR-19", Operation.RequestImport);

    [OracleFact(SideEffects = OracleFactAttribute.CreateSideEffects)]
    [Trait("Rule", "PR-20")]
    [Trait("SideEffects", OracleFactAttribute.CreateSideEffects)]
    public Task PR20_RequestLineAvailability() => RunAsync("PR-20", Operation.Create);

    [OracleFact]
    [Trait("Rule", "PR-21")]
    public Task PR21_VisitLine() => RunAsync("PR-21", Operation.VisitLine);

    [OracleFact(SideEffects = OracleFactAttribute.CreateSideEffects)]
    [Trait("Rule", "PR-22")]
    [Trait("SideEffects", OracleFactAttribute.CreateSideEffects)]
    public Task PR22_IdempotentCreate() => RunAsync("PR-22", Operation.Create);

    [OracleFact(PendingOn = "OI-08 … OI-10", SideEffects = OracleFactAttribute.CreateSideEffects)]
    [Trait("Rule", "PR-23")]
    [Trait("SideEffects", OracleFactAttribute.CreateSideEffects)]
    public Task PR23_PostingStages() => RunAsync("PR-23", Operation.Create);

    [OracleFact(PendingOn = "OI-03")]
    [Trait("Rule", "PR-24")]
    public Task PR24_PayerContext() => RunAsync("PR-24", Operation.Preview);

    [OracleFact(SideEffects = OracleFactAttribute.CreateSideEffects)]
    [Trait("Rule", "PR-25")]
    [Trait("SideEffects", OracleFactAttribute.CreateSideEffects)]
    public Task PR25_OfferStale() => RunAsync("PR-25", Operation.Create);

    /// <summary>Runs every derivable case of a PR fixture in its own session, explicitly rolled back after the case, and logs every pending-evidence case without asserting it.</summary>
    /// <param name="id">Package rule id, such as PR-01.</param>
    /// <param name="defaultOperation">Gateway operation of a case whose input names none.</param>
    private async Task RunAsync(string id, Operation defaultOperation)
    {
        var doc = ParityFixture.Load(id);
        Assert.Equal(id, doc.Id);
        Assert.Equal(ParityFixture.PackageClass, doc.Class);

        var connectionString = OracleFactAttribute.ConnectionString
            ?? throw FailException.ForFailure($"{PreconditionPrefix} {id}: {OracleFactAttribute.ConnectionVariable} is not set.");
        var options = new InvoicingDataOptions { ConnectionString = connectionString };
        var apiGateway = new BilInvoiceApiGateway(options);
        var importGateway = new BilImportGateway(options);

        var preconditionFailures = new List<string>();
        var parityFailures = new List<string>();
        var unexpectedErrors = new List<string>();
        var rollbackFailures = new List<string>();
        var failedCases = 0;

        foreach (var fixtureCase in doc.Cases)
        {
            if (!ParityFixture.IsDerivable(fixtureCase))
            {
                output.WriteLine($"{id} {fixtureCase.Name}: pending-evidence ({fixtureCase.PendingOn ?? "-"}); not asserted (UNVERIFIED).");
                continue;
            }

            var label = new CaseLabel(id, fixtureCase.Name);
            var outcome = await RunInRolledBackSessionAsync(
                () => new OracleSessionFactory(options).Open(),
                session => RunCaseAsync(label, doc.Compare, fixtureCase, defaultOperation, session, options, apiGateway, importGateway),
                session => ReleaseRolledBackSessionAsync(session, TimeSpan.FromSeconds(options.CommandTimeoutSeconds)));

            if (RecordOutcome(label, outcome, preconditionFailures, parityFailures, unexpectedErrors, rollbackFailures))
            {
                failedCases++;
            }
        }

        if (failedCases > 0)
        {
            Assert.Fail(FailureReport(id, failedCases, preconditionFailures, parityFailures, unexpectedErrors, rollbackFailures));
        }
    }

    /// <summary>Logs one case outcome and adds its case failure and its rollback failure each to its own section, an unexpected or rollback failure by <see cref="FailureCategory"/> only.</summary>
    /// <returns>True when the case failed or its rollback was not confirmed.</returns>
    private bool RecordOutcome(
        CaseLabel label,
        CaseOutcome outcome,
        List<string> preconditionFailures,
        List<string> parityFailures,
        List<string> unexpectedErrors,
        List<string> rollbackFailures)
    {
        switch (outcome)
        {
            case { CaseFailure: null, Operation: { } operation }:
                output.WriteLine(outcome.RollbackFailure is null
                    ? $"{label}: {GatewayName(operation)} parity asserted."
                    : $"{label}: {GatewayName(operation)} parity not accepted: the session's rollback or release failed.");
                break;
            case { CaseFailure: XunitException failure } when failure.Message.StartsWith(PreconditionPrefix, StringComparison.Ordinal):
                preconditionFailures.Add(failure.Message);
                output.WriteLine(failure.Message);
                break;
            case { CaseFailure: XunitException failure }:
                parityFailures.Add($"{label}: {failure.Message}");
                output.WriteLine($"{label}: parity failure: {failure.Message}");
                break;
            case { CaseFailure: { } failure }:
                var category = FailureCategory(failure);
                unexpectedErrors.Add($"{label}: {category}");
                output.WriteLine($"{label}: unexpected {category}.");
                break;
        }

        if (outcome.RollbackFailure is { } rollbackFailure)
        {
            var category = FailureCategory(rollbackFailure);
            rollbackFailures.Add($"{label}: {category}");
            output.WriteLine($"{label}: rollback failure {category}.");
        }

        return outcome.CaseFailure is not null || outcome.RollbackFailure is not null;
    }

    /// <summary>Names a failure by its exception type, the ORA number of an Oracle error and the categories of its inner exceptions, never its message or stack.</summary>
    private static string FailureCategory(Exception failure) => failure switch
    {
        OracleException oracle => "OracleException ORA-" + oracle.Number.ToString("D5", CultureInfo.InvariantCulture),
        AggregateException aggregate =>
            $"{aggregate.GetType().Name} [{string.Join(", ", aggregate.InnerExceptions.Select(FailureCategory))}]",
        { InnerException: { } inner } => $"{failure.GetType().Name} (inner {FailureCategory(inner)})",
        _ => failure.GetType().Name,
    };

    /// <summary>Opens a session, runs one case in it, then always awaits an explicit rollback, the release of a rolled-back session and the disposal of the session.</summary>
    /// <param name="open">Opens the case's session.</param>
    /// <param name="run">Runs the case in the open session and returns the gateway operation it ran.</param>
    /// <param name="release">Releases the session's transaction and connection after a completed rollback and returns the release failure, or null.</param>
    /// <returns>The operation the case ran, its failure, and the failure of its rollback, release or disposal; a session that fails to open is a case failure with no rollback.</returns>
    private static async Task<CaseOutcome> RunInRolledBackSessionAsync(
        Func<Task<IOracleSession>> open,
        Func<IOracleSession, Task<Operation>> run,
        Func<IOracleSession, Task<Exception?>> release)
    {
        IOracleSession session;
        try
        {
            session = await open() ?? throw new InvalidOperationException("The session factory returned no session.");
        }
        catch (Exception ex)
        {
            return new CaseOutcome(null, ex, null);
        }

        Operation? operation = null;
        Exception? caseFailure = null;
        Exception? rollbackFailure = null;
        try
        {
            operation = await run(session);
        }
        catch (Exception ex)
        {
            caseFailure = ex;
        }
        finally
        {
            var cleanupFailures = new List<Exception>();
            try
            {
                await session.Rollback();
            }
            catch (Exception ex)
            {
                cleanupFailures.Add(ex);
            }

            if (cleanupFailures.Count == 0)
            {
                try
                {
                    if (await release(session) is { } releaseFailure)
                    {
                        cleanupFailures.Add(releaseFailure);
                    }
                }
                catch (Exception ex)
                {
                    cleanupFailures.Add(ex);
                }
            }

            try
            {
                await session.DisposeAsync();
            }
            catch (Exception ex)
            {
                cleanupFailures.Add(ex);
            }

            rollbackFailure = cleanupFailures.Count switch
            {
                0 => null,
                1 => cleanupFailures[0],
                _ => new AggregateException(cleanupFailures),
            };
        }

        return new CaseOutcome(operation, caseFailure, rollbackFailure);
    }

    /// <summary>Releases the transaction and the connection of a rolled-back <see cref="OracleSession"/> through <see cref="OracleSession.ReleaseWithinDeadline"/> and returns the failures it reports.</summary>
    /// <param name="session">The case's session, already rolled back.</param>
    /// <param name="deadline">Time each release step may take.</param>
    /// <returns>The release failure, or null when both were released or the session is not an <see cref="OracleSession"/>.</returns>
    private static async Task<Exception?> ReleaseRolledBackSessionAsync(IOracleSession session, TimeSpan deadline)
    {
        if (session is not OracleSession oracleSession)
        {
            return null;
        }

        var failures = await OracleSession.ReleaseWithinDeadline(deadline, null, null, oracleSession.Transaction, oracleSession.Connection);
        return failures switch
        {
            null or [] => null,
            [var only] => only,
            _ => new AggregateException(failures),
        };
    }

    /// <summary>Plans one derivable case, checks its declared rows, runs its gateway operation in the given uncommitted session, and asserts the fixture's expected result.</summary>
    /// <returns>The gateway operation the case ran.</returns>
    private async Task<Operation> RunCaseAsync(
        CaseLabel label,
        FixtureCompare compare,
        FixtureCase fixtureCase,
        Operation defaultOperation,
        IOracleSession session,
        InvoicingDataOptions options,
        BilInvoiceApiGateway apiGateway,
        BilImportGateway importGateway)
    {
        var plan = PlanCase(label, fixtureCase.Input, defaultOperation);
        var requires = fixtureCase.Requires ?? throw label.Precondition("requires is missing.");
        var contextChecks = ReadContext(label, requires.Context, plan);
        var expected = fixtureCase.Expected;
        JsonElement? expectedValues = expected.Values is { } values ? RenderTemplates(label, values) : null;

        var handles = SessionHandles(session);

        foreach (var row in requires.Rows)
        {
            await CheckRowAsync(label, handles, row);
        }

        if (plan.Operation == Operation.Create)
        {
            await CheckNoMobileAsync(label, handles, Required(label, plan.Header, HeaderKey));
        }

        var observations = new List<(string Source, IReadOnlyDictionary<string, object?> Values)>();
        if (contextChecks.Count > 0 && plan.Operation == Operation.Create && plan.Header is not null && plan.Lines.Count > 0)
        {
            var preview = await ObservePreviewAsync(label, session, apiGateway, plan);
            if (preview is not null)
            {
                observations.Add((GatewayName(Operation.Preview), preview));
            }
        }

        Dictionary<string, object?>? actual = null;
        OracleErrorInfo? error = null;
        try
        {
            actual = await ExecuteAsync(label, plan, session, options, apiGateway, importGateway);
        }
        catch (OracleException ex) when (expected.Error is not null)
        {
            error = OracleErrorParser.FromException(ex);
        }

        if (actual is not null)
        {
            observations.Add((GatewayName(plan.Operation), actual));
        }

        CheckContext(label, contextChecks, observations);

        var outcome = error is null ? PassOutcome : ErrorOutcome;
        if (expected.Error is { } expectedError)
        {
            if (error is null)
            {
                throw FailException.ForFailure(
                    $"error: expected {expectedError.Package} {expectedError.Number} '{expectedError.MessagePrefix}', but {GatewayName(plan.Operation)} completed without an Oracle error.");
            }

            ParityFixture.AssertError(expectedError, RaisingPackage(error, expectedError.Package), error.Number, error.Text);
        }

        if (expected.Outcome is { } expectedOutcome)
        {
            At("outcome", () => ParityFixture.AssertExact(expectedOutcome, outcome));
        }

        if (expected.Messages is { } expectedMessages)
        {
            At("messages", () => ParityFixture.AssertMessages(expectedMessages, []));
        }

        if (expectedValues is { } assertedValues)
        {
            var actualValues = actual ?? NewValues();
            actualValues["outcome"] = outcome;
            if (actual is not null && plan.Operation == Operation.Create && NamesAny(assertedValues, ReadBackColumns))
            {
                await ReadBackAsync(handles, actualValues);
            }

            ParityFixture.AssertValues(assertedValues, actualValues, compare);
        }

        return plan.Operation;
    }

    /// <summary>Runs the case's gateway operation and projects its outputs onto the package field names the fixtures use.</summary>
    private static async Task<Dictionary<string, object?>> ExecuteAsync(
        CaseLabel label,
        CasePlan plan,
        IOracleSession session,
        InvoicingDataOptions options,
        BilInvoiceApiGateway apiGateway,
        BilImportGateway importGateway) => plan.Operation switch
        {
            Operation.Preview => await PreviewAsync(label, plan, session, apiGateway),
            Operation.Create => await CreateAsync(label, plan, session, options, apiGateway),
            Operation.BundledOffer => await BundledOfferAsync(label, plan, session, apiGateway),
            Operation.PackageLines => await PackageLinesAsync(label, plan, session, apiGateway),
            Operation.RequestImport => await RequestImportAsync(label, plan, session, importGateway),
            Operation.VisitLine => await VisitLineAsync(label, plan, session, importGateway),
            _ => throw label.Precondition($"operation {plan.Operation} has no gateway call."),
        };

    private static async Task<Dictionary<string, object?>> PreviewAsync(
        CaseLabel label,
        CasePlan plan,
        IOracleSession session,
        BilInvoiceApiGateway apiGateway)
    {
        var (lines, totals) = await apiGateway.CalculatePreview(
            session, Required(label, plan.Header, HeaderKey), plan.Lines, plan.Operator, plan.Amount1Auto);
        return ProjectPreview(lines, totals);
    }

    private static async Task<Dictionary<string, object?>> CreateAsync(
        CaseLabel label,
        CasePlan plan,
        IOracleSession session,
        InvoicingDataOptions options,
        BilInvoiceApiGateway apiGateway)
    {
        await OracleFactAttribute.AssertCreateTargetCleared(session, options, label.ToString());

        // Every create case, a replay included, calls CreateFullInvoice once; a replay case's requires.rows declare the completed request row it returns.
        var result = await apiGateway.CreateFullInvoice(
            session, Required(label, plan.Header, HeaderKey), plan.Lines, plan.Operator, plan.RequestId);
        return ProjectCreate(result);
    }

    private static async Task<Dictionary<string, object?>> BundledOfferAsync(
        CaseLabel label,
        CasePlan plan,
        IOracleSession session,
        BilInvoiceApiGateway apiGateway)
    {
        var lines = await apiGateway.GetBundledOfferLines(
            session,
            Required(label, plan.Header, HeaderKey),
            plan.Operator,
            RequiredValue(label, plan.OfferId, OfferIdKey),
            RequiredValue(label, plan.BundleQty, BundleQtyKey));
        return Values((LinesKey, lines.Select(ProjectPreviewLine).ToList()));
    }

    private static async Task<Dictionary<string, object?>> PackageLinesAsync(
        CaseLabel label,
        CasePlan plan,
        IOracleSession session,
        BilInvoiceApiGateway apiGateway)
    {
        var (lines, result) = await apiGateway.GetPackageLines(
            session,
            Required(label, plan.PackageServiceId, PackageServiceIdKey),
            RequiredValue(label, plan.ListId, ListIdKey),
            plan.ParentSourceId);
        return ProjectImport(lines, result);
    }

    private static async Task<Dictionary<string, object?>> RequestImportAsync(
        CaseLabel label,
        CasePlan plan,
        IOracleSession session,
        BilImportGateway importGateway)
    {
        var approvalMode = plan.ApprovalMode ?? importGateway.ApprovalCheckMode(plan.Parameters?.X422ApprovCheck);
        var (lines, result) = await importGateway.ImportRequestLines(
            session,
            Required(label, plan.Header, HeaderKey),
            plan.Operator,
            Required(label, plan.VisitUnique, VisitUniqueKey),
            plan.PatServReqRowIds,
            approvalMode);
        return ProjectImport(lines, result);
    }

    private static async Task<Dictionary<string, object?>> VisitLineAsync(
        CaseLabel label,
        CasePlan plan,
        IOracleSession session,
        BilImportGateway importGateway)
    {
        var (line, result) = await importGateway.GetVisitLine(
            session, Required(label, plan.Header, HeaderKey), plan.Operator, Required(label, plan.VisitChoice, VisitKindKey));
        var values = ProjectImport(line is null ? [] : [line], result);
        values.Add("line", line is null ? null : ProjectEngineLine(line));
        return values;
    }

    /// <summary>Previews the create-path draft in the same session; when the preview raises an Oracle error, logs its number and returns null.</summary>
    /// <returns>The projected preview, or null when the preview raised an Oracle error.</returns>
    private async Task<Dictionary<string, object?>?> ObservePreviewAsync(
        CaseLabel label,
        IOracleSession session,
        BilInvoiceApiGateway apiGateway,
        CasePlan plan)
    {
        try
        {
            var (lines, totals) = await apiGateway.CalculatePreview(
                session, Required(label, plan.Header, HeaderKey), plan.Lines, plan.Operator, plan.Amount1Auto);
            return ProjectPreview(lines, totals);
        }
        catch (OracleException ex)
        {
            var info = OracleErrorParser.FromException(ex);
            output.WriteLine(
                $"{label}: context preview raised {info.Number}; preview-derived context is not observable (UNVERIFIED).");
            return null;
        }
    }

    /// <summary>Returns the raising package: the translator's package when it is an ingested package, else a matching ORA-06512 frame, else UNKNOWN.</summary>
    private static string RaisingPackage(OracleErrorInfo error, string expectedPackage)
    {
        var translated = new OracleFailureTranslator().Translate(error).Package;
        if (translated is not null && IngestedPackages.Contains(translated, StringComparer.Ordinal))
        {
            return translated;
        }

        foreach (var frame in error.Frames)
        {
            if (string.Equals(frame.Package, expectedPackage, StringComparison.Ordinal))
            {
                return frame.Package;
            }
        }

        return UnknownPackage;
    }

    /// <summary>Formats the failures of one fixture, preconditions, parity failures, unexpected errors and rollback failures each in its own section.</summary>
    /// <param name="id">Package rule id, such as PR-01.</param>
    /// <param name="failedCases">Number of distinct cases that failed; one case may appear in two sections.</param>
    /// <param name="preconditionFailures">Precondition failure entries.</param>
    /// <param name="parityFailures">Parity failure entries.</param>
    /// <param name="unexpectedErrors">Unexpected error entries.</param>
    /// <param name="rollbackFailures">Rollback failure entries.</param>
    private static string FailureReport(
        string id,
        int failedCases,
        IReadOnlyCollection<string> preconditionFailures,
        IReadOnlyCollection<string> parityFailures,
        IReadOnlyCollection<string> unexpectedErrors,
        IReadOnlyCollection<string> rollbackFailures)
    {
        var sections = new List<string>
        {
            $"{id}: {failedCases} derivable case(s) failed (UNVERIFIED).",
        };

        AddSection(sections, "Precondition failures", preconditionFailures);
        AddSection(sections, "Parity failures", parityFailures);
        AddSection(sections, "Unexpected errors", unexpectedErrors);
        AddSection(sections, "Rollback failures", rollbackFailures);
        return string.Join(Environment.NewLine, sections);

        static void AddSection(List<string> target, string title, IReadOnlyCollection<string> items)
        {
            if (items.Count == 0)
            {
                return;
            }

            target.Add(title + ":");
            target.AddRange(items.Select(item => "  " + item));
        }
    }

    /// <summary>Runs an assertion and prefixes its failure message with the compared path.</summary>
    private static void At(string path, Action assertion)
    {
        try
        {
            assertion();
        }
        catch (XunitException ex) when (!ex.Message.StartsWith(PreconditionPrefix, StringComparison.Ordinal))
        {
            throw FailException.ForFailure($"{path}: {ex.Message}");
        }
    }

    /// <summary>Returns the fixture name of a gateway operation, such as BilInvoiceApiGateway.CalculatePreview.</summary>
    private static string GatewayName(Operation operation) =>
        OperationNames.First(pair => pair.Value == operation).Key;

    /// <summary>Maps a case input onto the gateway call it names; every input key must be one the operation consumes.</summary>
    private static CasePlan PlanCase(CaseLabel label, JsonElement input, Operation defaultOperation)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            throw label.Precondition($"input is {input.ValueKind}, not a JSON object.");
        }

        var properties = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in input.EnumerateObject())
        {
            if (!properties.TryAdd(property.Name, property.Value))
            {
                throw label.Precondition($"input key '{property.Name}' is repeated.");
            }
        }

        var operation = defaultOperation;
        if (properties.TryGetValue(OperationKey, out var operationElement))
        {
            var operationName = operationElement.ValueKind == JsonValueKind.String ? operationElement.GetString() : null;
            if (operationName is null || !OperationNames.TryGetValue(operationName, out operation))
            {
                throw label.Precondition($"input.{OperationKey} {operationElement.GetRawText()} names no gateway operation.");
            }
        }

        var consumed = OperationInputs[operation];
        foreach (var key in properties.Keys)
        {
            if (!consumed.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                throw label.Precondition($"input key '{key}' is not an input of {GatewayName(operation)}.");
            }
        }

        var header = properties.TryGetValue(HeaderKey, out var headerElement) ? ReadHeader(label, headerElement) : null;
        if (header is null && operation != Operation.PackageLines)
        {
            throw label.Precondition($"{GatewayName(operation)} needs input.{HeaderKey}.");
        }

        var lines = properties.TryGetValue(LinesKey, out var linesElement) ? ReadLines(label, linesElement) : [];

        var amount1Auto = true;
        if (properties.TryGetValue(Amount1AutoKey, out var amount1AutoElement))
        {
            amount1Auto = ReadBoolean(label, Amount1AutoKey, amount1AutoElement);
        }

        var requestId = properties.TryGetValue(RequestIdKey, out var requestIdElement)
            ? ReadText(label, RequestIdKey, requestIdElement)
            : Guid.NewGuid().ToString("N").ToUpperInvariant();

        foreach (var flag in PackageArgumentNames)
        {
            if (properties.TryGetValue(flag, out var flagElement))
            {
                CheckGatewayFlag(label, flag, flagElement);
            }
        }

        if (properties.TryGetValue(PackageArgumentsKey, out var packageArguments))
        {
            CheckPackageArguments(label, packageArguments);
        }

        var parameters = properties.TryGetValue(ParametersKey, out var parametersElement)
            ? ReadObject<InvoiceEntryParameters>(label, ParametersKey, parametersElement)
            : null;

        int? approvalMode = properties.TryGetValue(ApprovalModeKey, out var approvalModeElement)
            ? ReadInt32(label, ApprovalModeKey, approvalModeElement)
            : null;

        var visitUnique = properties.TryGetValue(VisitUniqueKey, out var visitUniqueElement)
            ? ReadText(label, VisitUniqueKey, visitUniqueElement)
            : parameters?.VisitUnique;

        IReadOnlyList<long> patServReqRowIds = properties.TryGetValue(PatServReqRowIdsKey, out var rowIdsElement)
            ? ReadIds(label, PatServReqRowIdsKey, rowIdsElement)
            : [];

        decimal? offerId = properties.TryGetValue(OfferIdKey, out var offerIdElement)
            ? ReadDecimal(label, OfferIdKey, offerIdElement)
            : null;

        decimal? bundleQty = properties.TryGetValue(BundleQtyKey, out var bundleQtyElement)
            ? ReadDecimal(label, BundleQtyKey, bundleQtyElement)
            : null;

        var packageServiceId = properties.TryGetValue(PackageServiceIdKey, out var packageServiceIdElement)
            ? ReadText(label, PackageServiceIdKey, packageServiceIdElement)
            : null;

        decimal? listId = properties.TryGetValue(ListIdKey, out var listIdElement)
            ? ReadDecimal(label, ListIdKey, listIdElement)
            : null;

        var parentSourceId = properties.TryGetValue(ParentSourceIdKey, out var parentSourceIdElement)
            ? ReadOptionalText(label, ParentSourceIdKey, parentSourceIdElement)
            : null;

        var visitChoice = properties.TryGetValue(VisitKindKey, out var visitKindElement)
            ? ReadVisitChoice(label, visitKindElement)
            : null;

        var missing = operation switch
        {
            Operation.BundledOffer when offerId is null => OfferIdKey,
            Operation.BundledOffer when bundleQty is null => BundleQtyKey,
            Operation.PackageLines when packageServiceId is null => PackageServiceIdKey,
            Operation.PackageLines when listId is null => ListIdKey,
            Operation.RequestImport when visitUnique is null => VisitUniqueKey,
            Operation.RequestImport when !properties.ContainsKey(PatServReqRowIdsKey) => PatServReqRowIdsKey,
            Operation.VisitLine when visitChoice is null => VisitKindKey,
            _ => null,
        };

        if (missing is not null)
        {
            throw label.Precondition($"{GatewayName(operation)} needs input.{missing}.");
        }

        return new CasePlan(
            operation,
            header,
            lines,
            ReadOperator(label, properties),
            amount1Auto,
            requestId,
            parameters,
            approvalMode,
            visitUnique,
            patServReqRowIds,
            offerId,
            bundleQty,
            packageServiceId,
            listId,
            parentSourceId,
            visitChoice);
    }

    /// <summary>Reads input.header; the draft date is the header's draftDate, else its invDate.</summary>
    private static InvoiceHeaderDraft ReadHeader(CaseLabel label, JsonElement element)
    {
        var header = ReadObject<InvoiceHeaderDraft>(label, HeaderKey, element);
        if (HasProperty(element, nameof(InvoiceHeaderDraft.DraftDate)))
        {
            return header;
        }

        return header.InvDate is { } invDate
            ? header with { DraftDate = invDate }
            : throw label.Precondition($"input.{HeaderKey} has neither draftDate nor invDate.");
    }

    /// <summary>Reads input.lines as draft lines in bind order.</summary>
    private static IReadOnlyList<InvoiceLineDraft> ReadLines(CaseLabel label, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw label.Precondition($"input.{LinesKey} is {element.ValueKind}, not an array.");
        }

        var lines = new List<InvoiceLineDraft>();
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            lines.Add(ReadObject<InvoiceLineDraft>(label, $"{LinesKey}[{index}]", item));
            index++;
        }

        return lines;
    }

    /// <summary>Returns the synthetic parity operator, overridden by each value input.operator or input.operatorContext declares.</summary>
    private static OperatorContext ReadOperator(CaseLabel label, IReadOnlyDictionary<string, JsonElement> properties)
    {
        var synthetic = new OperatorContext
        {
            UserNo = 1,
            UserName = "PARITY_TEST",
            InfoCenterId = "1",
            MachineName = "PARITY-TEST",
            SessionId = Guid.NewGuid().ToString("N").ToUpperInvariant(),
        };

        var hasOperator = properties.TryGetValue(OperatorKey, out var operatorElement);
        var hasOperatorContext = properties.TryGetValue(OperatorContextKey, out var operatorContextElement);
        if (hasOperator && hasOperatorContext)
        {
            throw label.Precondition($"input declares both {OperatorKey} and {OperatorContextKey}.");
        }

        if (!hasOperator && !hasOperatorContext)
        {
            return synthetic;
        }

        var element = hasOperator ? operatorElement : operatorContextElement;
        var path = hasOperator ? OperatorKey : OperatorContextKey;
        var declared = ReadObject<OperatorContext>(label, path, element);

        return synthetic with
        {
            UserNo = HasProperty(element, nameof(OperatorContext.UserNo)) ? declared.UserNo : synthetic.UserNo,
            UserName = HasProperty(element, nameof(OperatorContext.UserName)) ? declared.UserName : synthetic.UserName,
            InfoCenterId = HasProperty(element, nameof(OperatorContext.InfoCenterId)) ? declared.InfoCenterId : synthetic.InfoCenterId,
            MachineName = HasProperty(element, nameof(OperatorContext.MachineName)) ? declared.MachineName : synthetic.MachineName,
            SessionId = HasProperty(element, nameof(OperatorContext.SessionId)) ? declared.SessionId : synthetic.SessionId,
        };
    }

    /// <summary>Deserializes a JSON object into <typeparamref name="T"/>, refusing any property <typeparamref name="T"/> does not declare.</summary>
    private static T ReadObject<T>(CaseLabel label, string path, JsonElement element)
        where T : class
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw label.Precondition($"input.{path} is {element.ValueKind}, not a JSON object.");
        }

        var names = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is { IsPublic: true })
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var property in element.EnumerateObject())
        {
            if (!names.Contains(property.Name))
            {
                throw label.Precondition($"input.{path}.{property.Name} is not a {typeof(T).Name} property.");
            }
        }

        try
        {
            return element.Deserialize<T>(ParityFixture.JsonOptions)
                ?? throw label.Precondition($"input.{path} deserialized to null.");
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            throw label.Precondition($"input.{path} cannot be read as {typeof(T).Name}: {ex.Message}");
        }
    }

    /// <summary>Checks that a declared p_send_sms or p_build_print_url value is the 'N' CreateFullInvoice binds.</summary>
    private static void CheckGatewayFlag(CaseLabel label, string path, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String || !string.Equals(element.GetString(), GatewayFlagValue, StringComparison.Ordinal))
        {
            throw label.Precondition(
                $"input.{path} is {element.GetRawText()}; BilInvoiceApiGateway.CreateFullInvoice binds '{GatewayFlagValue}'.");
        }
    }

    /// <summary>Checks input.packageArguments: only p_send_sms and p_build_print_url, each 'N'.</summary>
    private static void CheckPackageArguments(CaseLabel label, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw label.Precondition($"input.{PackageArgumentsKey} is {element.ValueKind}, not a JSON object.");
        }

        foreach (var argument in element.EnumerateObject())
        {
            if (!PackageArgumentNames.Contains(argument.Name, StringComparer.OrdinalIgnoreCase))
            {
                throw label.Precondition(
                    $"input.{PackageArgumentsKey}.{argument.Name} is not an argument BilInvoiceApiGateway.CreateFullInvoice lets a caller set.");
            }

            CheckGatewayFlag(label, $"{PackageArgumentsKey}.{argument.Name}", argument.Value);
        }
    }

    private static bool ReadBoolean(CaseLabel label, string path, JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw label.Precondition($"input.{path} is {element.GetRawText()}, not a boolean."),
    };

    private static string ReadText(CaseLabel label, string path, JsonElement element) =>
        element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()!
            : throw label.Precondition($"input.{path} is {element.GetRawText()}, not a non-blank string.");

    private static string? ReadOptionalText(CaseLabel label, string path, JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => element.GetString(),
        _ => throw label.Precondition($"input.{path} is {element.GetRawText()}, not a string or null."),
    };

    private static decimal ReadDecimal(CaseLabel label, string path, JsonElement element) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var value)
            ? value
            : throw label.Precondition($"input.{path} is {element.GetRawText()}, not a decimal number.");

    private static int ReadInt32(CaseLabel label, string path, JsonElement element) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value)
            ? value
            : throw label.Precondition($"input.{path} is {element.GetRawText()}, not a whole number.");

    private static IReadOnlyList<long> ReadIds(CaseLabel label, string path, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw label.Precondition($"input.{path} is {element.ValueKind}, not an array.");
        }

        var ids = new List<long>();
        foreach (var item in element.EnumerateArray())
        {
            ids.Add(item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var id)
                ? id
                : throw label.Precondition($"input.{path} holds {item.GetRawText()}, not a whole number."));
        }

        return ids;
    }

    /// <summary>Maps input.visitKind onto the two visit lines BIL_IMPORT.GET_VISIT_LINE supplies.</summary>
    private static VisitLineChoice ReadVisitChoice(CaseLabel label, JsonElement element)
    {
        var kind = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
        if (string.Equals(kind, VisitLineChoice.Consultation.Kind, StringComparison.Ordinal))
        {
            return VisitLineChoice.Consultation;
        }

        if (string.Equals(kind, VisitLineChoice.Review.Kind, StringComparison.Ordinal))
        {
            return VisitLineChoice.Review;
        }

        throw label.Precondition(
            $"input.{VisitKindKey} is {element.GetRawText()}; only '{VisitLineChoice.Consultation.Kind}' and '{VisitLineChoice.Review.Kind}' reach BIL_IMPORT.GET_VISIT_LINE.");
    }

    private static bool HasProperty(JsonElement element, string name) =>
        element.EnumerateObject().Any(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase));

    private static T Required<T>(CaseLabel label, T? value, string path)
        where T : class =>
        value ?? throw label.Precondition($"input.{path} is missing.");

    private static T RequiredValue<T>(CaseLabel label, T? value, string path)
        where T : struct =>
        value ?? throw label.Precondition($"input.{path} is missing.");

    /// <summary>Reads the connection and transaction of an <see cref="OracleSession"/> for in-session SELECT statements.</summary>
    private static (OracleConnection Connection, OracleTransaction Transaction) SessionHandles(IOracleSession session)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = session.GetType();
        var connection = type.GetProperty("Connection", flags)?.GetValue(session) as OracleConnection;
        var transaction = type.GetProperty("Transaction", flags)?.GetValue(session) as OracleTransaction;
        if (connection is null || transaction is null)
        {
            throw FailException.ForFailure($"{PreconditionPrefix} OracleSession handles not found");
        }

        return (connection, transaction);
    }

    /// <summary>Checks one requires.rows entry by keyed SELECT: the declared row count, else at least one row, and every declared column value on every matching row.</summary>
    private static async Task CheckRowAsync(
        CaseLabel label,
        (OracleConnection Connection, OracleTransaction Transaction) handles,
        JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object)
        {
            throw label.Precondition($"requires.rows entry {row.GetRawText()} is not a JSON object.");
        }

        foreach (var property in row.EnumerateObject())
        {
            if (!RowEntryKeys.Contains(property.Name, StringComparer.Ordinal))
            {
                throw label.Precondition($"requires.rows key '{property.Name}' is not one of {string.Join(", ", RowEntryKeys)}.");
            }
        }

        var table = Identifier(
            label,
            row.TryGetProperty("table", out var tableElement) && tableElement.ValueKind == JsonValueKind.String ? tableElement.GetString() : null);

        if (!row.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.Object || !key.EnumerateObject().Any())
        {
            throw label.Precondition($"requires.rows entry for {table} needs a non-empty key object.");
        }

        var description = $"{table} {key.GetRawText()}";
        var (predicate, parameters) = KeyPredicate(label, key);
        var expectedCount = ExpectedRowCount(label, row, description);

        var countSql = $"SELECT COUNT(*) AS ROW_COUNT FROM {table} WHERE {predicate}";
        var found = await handles.Connection.QuerySingleOrDefaultAsync<decimal>(
            new CommandDefinition(countSql, parameters(), handles.Transaction, commandType: CommandType.Text));

        if (expectedCount is { } exact && found != exact)
        {
            throw label.Precondition($"{description}: {found.ToString(CultureInfo.InvariantCulture)} row(s) found, {exact} declared.");
        }

        if (expectedCount is null && found == 0)
        {
            throw label.Precondition($"{description}: no row found.");
        }

        if (!row.TryGetProperty("columns", out var columns))
        {
            return;
        }

        if (columns.ValueKind != JsonValueKind.Object)
        {
            throw label.Precondition($"{description}: columns is {columns.ValueKind}, not a JSON object.");
        }

        var declared = columns.EnumerateObject().ToList();
        if (declared.Count == 0 || found == 0)
        {
            return;
        }

        var selectList = string.Join(", ", declared.Select(column => Identifier(label, column.Name)));
        var columnSql = $"SELECT {selectList} FROM {table} WHERE {predicate}";
        var rows = await QueryRowsAsync(handles, columnSql, parameters());
        foreach (var actualRow in rows)
        {
            foreach (var column in declared)
            {
                var value = ColumnValue(label, actualRow, column.Name);
                if (!Matches(column.Value, value))
                {
                    throw label.Precondition(
                        $"{description}: {column.Name} differs from the declared {column.Value.GetRawText()} (actual value withheld: {Withheld(value)}).");
                }
            }
        }
    }

    /// <summary>Builds the WHERE predicate of a keyed SELECT; every non-null key value is a bound parameter :k0, :k1 and so on.</summary>
    private static (string Predicate, Func<DynamicParameters> Parameters) KeyPredicate(CaseLabel label, JsonElement key)
    {
        var terms = new List<string>();
        var values = new List<(string Name, object Value)>();
        foreach (var property in key.EnumerateObject())
        {
            var column = Identifier(label, property.Name);
            if (property.Value.ValueKind == JsonValueKind.Null)
            {
                terms.Add($"{column} IS NULL");
                continue;
            }

            var name = "k" + values.Count.ToString(CultureInfo.InvariantCulture);
            values.Add((name, BindValue(label, property.Name, property.Value)));
            terms.Add($"{column} = :{name}");
        }

        return (string.Join(" AND ", terms), Bind);

        DynamicParameters Bind()
        {
            var parameters = new DynamicParameters();
            foreach (var (name, value) in values)
            {
                parameters.Add(name, value);
            }

            return parameters;
        }
    }

    /// <summary>Returns the exact row count a requires.rows entry declares through count, rowCount, exists or absent, or null for "at least one row".</summary>
    private static int? ExpectedRowCount(CaseLabel label, JsonElement row, string description)
    {
        int? exact = null;
        bool? present = null;

        foreach (var name in new[] { "count", "rowCount" })
        {
            if (!row.TryGetProperty(name, out var element))
            {
                continue;
            }

            if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var count) || count < 0)
            {
                throw label.Precondition($"{description}: {name} {element.GetRawText()} is not a non-negative whole number.");
            }

            if (exact is not null && exact != count)
            {
                throw label.Precondition($"{description}: count and rowCount disagree.");
            }

            exact = count;
        }

        foreach (var (name, meansPresent) in new[] { ("exists", true), ("absent", false) })
        {
            if (!row.TryGetProperty(name, out var element))
            {
                continue;
            }

            if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw label.Precondition($"{description}: {name} {element.GetRawText()} is not a boolean.");
            }

            var value = element.GetBoolean() == meansPresent;
            if (present is not null && present != value)
            {
                throw label.Precondition($"{description}: exists and absent disagree.");
            }

            present = value;
        }

        if (present == false)
        {
            return exact is null or 0 ? 0 : throw label.Precondition($"{description}: a row count of {exact} contradicts an absent row.");
        }

        if (present == true && exact == 0)
        {
            throw label.Precondition($"{description}: a row count of 0 contradicts a present row.");
        }

        return exact;
    }

    /// <summary>Checks that the create-path patient exists and has no mobile number in PATIENT.PHONE_H.</summary>
    private static async Task CheckNoMobileAsync(
        CaseLabel label,
        (OracleConnection Connection, OracleTransaction Transaction) handles,
        InvoiceHeaderDraft header)
    {
        if (string.IsNullOrWhiteSpace(header.PatientNo))
        {
            throw label.Precondition("a create-path case needs input.header.patientNo naming a synthetic patient.");
        }

        var parameters = new DynamicParameters();
        parameters.Add("k0", header.PatientNo);
        var rows = await QueryRowsAsync(handles, PatientPhoneSql, parameters);
        if (rows.Count == 0)
        {
            throw label.Precondition($"PATIENT {header.PatientNo} does not exist.");
        }

        foreach (var patient in rows)
        {
            var phone = ColumnValue(label, patient, "PHONE_H");
            if (phone is not null)
            {
                throw label.Precondition($"PATIENT {header.PatientNo} has a PHONE_H value (withheld); a create-path case needs a patient with no mobile number.");
            }
        }
    }

    /// <summary>Reads OLD_OR_NEW, PFLAG and, as numbers, THE_MONTH and THE_YEAR of the invoice just created, visible only inside the session's transaction.</summary>
    private static async Task ReadBackAsync(
        (OracleConnection Connection, OracleTransaction Transaction) handles,
        Dictionary<string, object?> values)
    {
        if (!values.TryGetValue("inv_no", out var invNo) || invNo is not long number)
        {
            throw FailException.ForFailure(
                $"inv_no: BilInvoiceApiGateway.CreateFullInvoice returned {Describe(invNo)}, so T_INV cannot be read back.");
        }

        var parameters = new DynamicParameters();
        parameters.Add("invNo", number);
        var rows = await QueryRowsAsync(handles, ReadBackSql, parameters);
        if (rows.Count != 1)
        {
            throw FailException.ForFailure($"T_INV {number.ToString(CultureInfo.InvariantCulture)}: {rows.Count} row(s) visible in the session, 1 expected.");
        }

        foreach (var column in ReadBackColumns)
        {
            var match = rows[0].Where(pair => FixtureKeyComparer.Instance.Equals(pair.Key, column)).ToList();
            if (match.Count != 1)
            {
                throw FailException.ForFailure($"T_INV {number.ToString(CultureInfo.InvariantCulture)}: column {column} was not returned.");
            }

            values[column] = match[0].Value is DBNull ? null : match[0].Value;
        }
    }

    /// <summary>Runs a SELECT on the session's own connection and transaction and returns each row keyed by column name.</summary>
    private static async Task<IReadOnlyList<IDictionary<string, object>>> QueryRowsAsync(
        (OracleConnection Connection, OracleTransaction Transaction) handles,
        string sql,
        DynamicParameters parameters)
    {
        var rows = await handles.Connection.QueryAsync(
            new CommandDefinition(sql, parameters, handles.Transaction, commandType: CommandType.Text));
        return rows.Cast<IDictionary<string, object>>().ToList();
    }

    private static object? ColumnValue(CaseLabel label, IDictionary<string, object> row, string column)
    {
        var match = row.Where(pair => string.Equals(pair.Key, column, StringComparison.OrdinalIgnoreCase)).ToList();
        if (match.Count != 1)
        {
            throw label.Precondition($"column {column} was not returned by the keyed SELECT.");
        }

        return match[0].Value is DBNull ? null : match[0].Value;
    }

    private static string Identifier(CaseLabel label, string? name) =>
        name is not null && IdentifierPattern.IsMatch(name)
            ? name
            : throw label.Precondition($"'{name}' is not a plain Oracle identifier.");

    private static object BindValue(CaseLabel label, string column, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
        _ => throw label.Precondition($"key {column} value {value.GetRawText()} is neither a string nor a decimal number."),
    };

    /// <summary>Reads requires.context into value checks: an explicit output field, a values object, a dotted name with an optional [serviceid], or flat field keys.</summary>
    /// <param name="label">Case under test.</param>
    /// <param name="context">The case's requires.context entries.</param>
    /// <param name="plan">The case's gateway call.</param>
    /// <returns>One check per declared value, each bound to its output relation or to none.</returns>
    private static List<ContextCheck> ReadContext(CaseLabel label, IReadOnlyList<JsonElement> context, CasePlan plan)
    {
        var checks = new List<ContextCheck>();
        foreach (var entry in context)
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw label.Precondition($"requires.context entry {entry.GetRawText()} is not a JSON object.");
            }

            var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in entry.EnumerateObject())
            {
                properties[property.Name] = property.Value;
            }

            var service = properties.TryGetValue("serviceid", out var serviceElement) ? ServiceText(label, serviceElement) : null;
            var source = ContextSource(properties) ?? entry.GetRawText();
            var package = ContextPackage(properties);
            var before = checks.Count;

            if (properties.TryGetValue("field", out var field))
            {
                if (field.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(field.GetString()) || !properties.TryGetValue("value", out var fieldValue))
                {
                    throw label.Precondition($"requires.context entry {entry.GetRawText()} needs a field name and a value.");
                }

                checks.Add(NewContextCheck(source, null, field.GetString()!, service, fieldValue, plan));
            }
            else if (properties.TryGetValue("values", out var values))
            {
                if (values.ValueKind != JsonValueKind.Object)
                {
                    throw label.Precondition($"requires.context entry {entry.GetRawText()} has a values member that is not a JSON object.");
                }

                var valuesService = values.TryGetProperty("serviceid", out var valuesServiceElement) ? ServiceText(label, valuesServiceElement) : service;
                foreach (var item in values.EnumerateObject())
                {
                    if (!string.Equals(item.Name, "serviceid", StringComparison.Ordinal))
                    {
                        checks.Add(NewContextCheck($"{source}.{item.Name}", package, item.Name, valuesService, item.Value, plan));
                    }
                }
            }
            else if (ContextNameKeys.FirstOrDefault(properties.ContainsKey) is { } nameKey && properties.TryGetValue("value", out var namedValue))
            {
                var name = properties[nameKey];
                if (name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()))
                {
                    throw label.Precondition($"requires.context entry {entry.GetRawText()} has a {nameKey} that is not a non-blank string.");
                }

                var (fieldName, nameService) = SplitContextName(label, name.GetString()!);
                checks.Add(NewContextCheck(name.GetString()!, package, fieldName, nameService ?? service, namedValue, plan));
            }
            else
            {
                foreach (var (key, value) in properties)
                {
                    if (string.Equals(key, "serviceid", StringComparison.Ordinal) || ContextMetadataKeys.Contains(key, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    var (fieldName, nameService) = SplitContextName(label, key);
                    checks.Add(NewContextCheck(key, DottedPackage(key) ?? PackageKey(properties), fieldName, nameService ?? service, value, plan));
                }
            }

            if (checks.Count == before)
            {
                throw label.Precondition($"requires.context entry {entry.GetRawText()} declares no value.");
            }
        }

        return checks;
    }

    /// <summary>Compares each context check with the output that echoes it; a check no observed output carries fails as a precondition, whether or not the case's operation raised.</summary>
    /// <param name="label">Case under test.</param>
    /// <param name="checks">The case's context checks.</param>
    /// <param name="observations">Outputs observed in the case's session, each named by its gateway call.</param>
    private static void CheckContext(
        CaseLabel label,
        IReadOnlyList<ContextCheck> checks,
        IReadOnlyList<(string Source, IReadOnlyDictionary<string, object?> Values)> observations)
    {
        if (checks.Count == 0)
        {
            return;
        }

        var operation = GatewayName(checks[0].Plan.Operation);
        var completed = observations.Any(observation => string.Equals(observation.Source, operation, StringComparison.Ordinal));
        foreach (var check in checks)
        {
            if (!TryObserve(check, observations, completed, out var expected, out var observed, out var where, out var reason))
            {
                var cause = completed || check.Relation is { Completion: true } ? reason : $"{reason} ({operation} raised before returning an output)";
                throw label.Precondition($"context {check.Source} = {check.Value.GetRawText()} cannot be checked: {cause}.");
            }

            if (!Matches(expected, observed))
            {
                var implied = expected.GetRawText() == check.Value.GetRawText() ? string.Empty : $" (output {expected.GetRawText()})";
                throw label.Precondition(
                    $"context {check.Source} declares {check.Value.GetRawText()}{implied}, but {where} {check.Relation!.Output} differs (actual value withheld: {Withheld(observed)}).");
            }
        }
    }

    /// <summary>Finds the output value that echoes a context check through its relation.</summary>
    /// <param name="check">The declared value and its relation.</param>
    /// <param name="observations">Outputs observed in the case's session.</param>
    /// <param name="completed">True when the case's own operation returned an output.</param>
    /// <param name="expected">Output value the declared value implies.</param>
    /// <param name="observed">Observed output value.</param>
    /// <param name="where">Output that carries the observed value.</param>
    /// <param name="reason">Why the check is unobserved, when the result is false.</param>
    /// <returns>True when an output carries a value the relation applies to.</returns>
    private static bool TryObserve(
        ContextCheck check,
        IReadOnlyList<(string Source, IReadOnlyDictionary<string, object?> Values)> observations,
        bool completed,
        out JsonElement expected,
        out object? observed,
        out string where,
        out string reason)
    {
        expected = default;
        observed = null;
        where = string.Empty;
        var operation = GatewayName(check.Plan.Operation);

        if (check.Relation is not { } relation)
        {
            reason = $"no output of {operation} echoes {check.Package}.{check.Field}";
            return false;
        }

        if (relation.InputCondition?.Invoke(check) is { } inputReason)
        {
            reason = inputReason;
            return false;
        }

        if (relation.Expect(check.Value, check) is not { } implied)
        {
            reason = $"no output of {operation} echoes {check.Package}.{check.Field} = {check.Value.GetRawText()}";
            return false;
        }

        expected = FixtureKeyComparer.Instance.Equals(relation.Output, CurrencyField) ? NormalisedCurrency(implied) : implied;
        if (relation.Completion)
        {
            reason = $"{operation} returned no output";
            if (!completed)
            {
                return false;
            }

            observed = implied.ValueKind == JsonValueKind.True;
            where = operation;
            return true;
        }

        reason = check.ServiceId is null
            ? $"no observed output carries {relation.Output}"
            : $"no observed line for service {check.ServiceId} carries {relation.Output}";
        foreach (var (source, values) in observations)
        {
            var line = SelectLine(values, check.ServiceId);
            IReadOnlyDictionary<string, object?> record;
            string place;
            if (line is not null && line.ContainsKey(relation.Output))
            {
                (record, place) = (line, $"{source} line");
            }
            else if (check.ServiceId is null && values.TryGetValue(relation.Output, out var scalar) && IsScalar(scalar))
            {
                (record, place) = (values, source);
            }
            else
            {
                continue;
            }

            if (relation.ObservedCondition?.Invoke(record) is { } observedReason)
            {
                reason = $"{place} {observedReason}";
                continue;
            }

            observed = record[relation.Output];
            where = place;
            return true;
        }

        return false;
    }

    private static IReadOnlyDictionary<string, object?>? SelectLine(IReadOnlyDictionary<string, object?> values, string? serviceId)
    {
        if (!values.TryGetValue(LinesKey, out var lines) || lines is not IEnumerable<IReadOnlyDictionary<string, object?>> list)
        {
            return null;
        }

        return serviceId is null
            ? list.FirstOrDefault()
            : list.FirstOrDefault(line =>
                line.TryGetValue("serviceid", out var id)
                && string.Equals(Convert.ToString(id, CultureInfo.InvariantCulture), serviceId, StringComparison.Ordinal));
    }

    private static bool IsScalar(object? value) => value is null or string || value is not System.Collections.IEnumerable;

    private static string? ContextSource(IReadOnlyDictionary<string, JsonElement> properties)
    {
        foreach (var key in new[] { "name", "output", "key", "source" })
        {
            if (properties.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString();
            }
        }

        var package = properties.TryGetValue("package", out var packageElement) && packageElement.ValueKind == JsonValueKind.String
            ? packageElement.GetString()
            : null;
        var operation = properties.TryGetValue("operation", out var operationElement) && operationElement.ValueKind == JsonValueKind.String
            ? operationElement.GetString()
            : null;

        return (package, operation) switch
        {
            (not null, not null) => $"{package}.{operation}",
            (not null, null) => package,
            _ => null,
        };
    }

    /// <summary>Splits a context name such as bil_service_context.curr_code[1001] into its last segment and its bracketed service id.</summary>
    private static (string Field, string? ServiceId) SplitContextName(CaseLabel label, string name)
    {
        var match = ContextNamePattern.Match(name);
        var path = match.Success ? match.Groups["path"].Value : string.Empty;
        var field = path[(path.LastIndexOf('.') + 1)..].Trim();
        if (field.Length == 0)
        {
            throw label.Precondition($"requires.context name '{name}' names no field.");
        }

        var service = match.Groups["service"].Success ? match.Groups["service"].Value : null;
        return (field, service);
    }

    private static string ServiceText(CaseLabel label, JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString()!,
        JsonValueKind.Number => element.GetRawText(),
        _ => throw label.Precondition($"requires.context serviceid {element.GetRawText()} is neither a string nor a number."),
    };

    /// <summary>Returns the lower-cased package a context entry names: the first segment of its dotted name, output, key or source, else its package member, else null.</summary>
    private static string? ContextPackage(IReadOnlyDictionary<string, JsonElement> properties)
    {
        foreach (var key in new[] { "name", "output", "key", "source" })
        {
            if (properties.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String)
            {
                return DottedPackage(element.GetString()!) ?? PackageKey(properties);
            }
        }

        return PackageKey(properties);
    }

    /// <summary>Returns the lower-cased first segment of a dotted context name, or null when the name has no package segment.</summary>
    private static string? DottedPackage(string name)
    {
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        var package = dot > 0 ? name[..dot].Trim() : string.Empty;
        return package.Length == 0 ? null : package.ToLowerInvariant();
    }

    /// <summary>Returns the lower-cased package member of a context entry, or null.</summary>
    private static string? PackageKey(IReadOnlyDictionary<string, JsonElement> properties) =>
        properties.TryGetValue("package", out var element) && element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()!.Trim().ToLowerInvariant()
            : null;

    /// <summary>Builds a context check: a value with no package is compared with the output field it names, any other with its package field's output relation.</summary>
    /// <returns>The check, with a null relation when no output echoes the package field.</returns>
    private static ContextCheck NewContextCheck(string source, string? package, string field, string? serviceId, JsonElement value, CasePlan plan)
    {
        var relation = package is null
            ? new ContextRelation(field, DeclaredValue)
            : string.Equals(field, RaisesField, StringComparison.OrdinalIgnoreCase)
                ? RaisesRelation
                : ContextRelations.GetValueOrDefault($"{package}.{field}");
        return new ContextCheck(source, package, field, serviceId, value, relation, plan);
    }

    /// <summary>Returns the declared value as the output value it implies.</summary>
    private static JsonElement? DeclaredValue(JsonElement value, ContextCheck check) => value;

    /// <summary>Returns nvl(value, 0) for a number or null, else null.</summary>
    private static JsonElement? ZeroWhenNull(JsonElement value, ContextCheck check) => value.ValueKind switch
    {
        JsonValueKind.Null => JsonSerializer.SerializeToElement(0m),
        JsonValueKind.Number => value,
        _ => null,
    };

    /// <summary>Returns round(nvl(value, 0), 3), half away from zero, for a number or null, else null.</summary>
    private static JsonElement? RoundedPrice(JsonElement value, ContextCheck check) => value.ValueKind switch
    {
        JsonValueKind.Null => JsonSerializer.SerializeToElement(0m),
        JsonValueKind.Number when value.TryGetDecimal(out var price) =>
            JsonSerializer.SerializeToElement(Math.Round(price, 3, MidpointRounding.AwayFromZero)),
        _ => null,
    };

    /// <summary>Returns 'Y' when upper_trim_to_null(value) is 'Y' and 'N' for any other string or null, else null.</summary>
    private static JsonElement? YesOrNo(JsonElement value, ContextCheck check) => value.ValueKind switch
    {
        JsonValueKind.String => JsonSerializer.SerializeToElement(UpperTrimToNull(value.GetString()) == "Y" ? "Y" : "N"),
        JsonValueKind.Null => JsonSerializer.SerializeToElement("N"),
        _ => null,
    };

    /// <summary>Returns PARENT for is_package 1 and null for any other value.</summary>
    private static JsonElement? PackageRole(JsonElement value, ContextCheck check) =>
        JsonSerializer.SerializeToElement(
            value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var flag) && flag == 1 ? "PARENT" : null);

    /// <summary>Returns offer_type 1 for matched 'Y', else null.</summary>
    private static JsonElement? OfferTypeOf(JsonElement value, ContextCheck check) =>
        value.ValueKind == JsonValueKind.String && value.GetString() == "Y" ? JsonSerializer.SerializeToElement(1) : null;

    /// <summary>Returns a declared true, else null.</summary>
    private static JsonElement? DeclaredTrue(JsonElement value, ContextCheck check) => value.ValueKind == JsonValueKind.True ? value : null;

    /// <summary>Returns a declared false, else null.</summary>
    private static JsonElement? DeclaredFalse(JsonElement value, ContextCheck check) => value.ValueKind == JsonValueKind.False ? value : null;

    /// <summary>Returns a curr_code string as upper_trim_to_null leaves it, and any other value unchanged.</summary>
    private static JsonElement NormalisedCurrency(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? JsonSerializer.SerializeToElement(UpperTrimToNull(value.GetString())) : value;

    /// <summary>Returns the reason the input line is priced by an override instead of the billing price, or null.</summary>
    private static string? NoPriceOverride(ContextCheck check) => InputLine(check) switch
    {
        null => MissingInputLine(check),
        { } line when UsesPriceOverride(line) => $"input line {line.ServiceId} uses a price override, so its price is not the billing price",
        _ => null,
    };

    /// <summary>Returns the reason a declared match cannot reach the line's offer_type, or null.</summary>
    private static string? OfferEligible(ContextCheck check)
    {
        if (InputLine(check) is not { } line)
        {
            return MissingInputLine(check);
        }

        if (check.Value.ValueKind != JsonValueKind.String || check.Value.GetString() != "Y")
        {
            return "only a matched 'Y' reaches offer_type: an unmatched line and a line bil_offer_rule never resolves show the same offer_type";
        }

        if (check.Plan.Header?.PayType != 1)
        {
            return "a standard offer is resolved only for a cash invoice (payType 1)";
        }

        if (HasPackageMetadata(line))
        {
            return $"input line {line.ServiceId} carries package metadata, so no standard offer is resolved for it";
        }

        return line.PatServReqRowId is not null && UsesPriceOverride(line)
            ? $"input line {line.ServiceId} is a request line with a price override, so no standard offer is resolved for it"
            : null;
    }

    /// <summary>Returns the reason an operation other than CreateFullInvoice cannot observe the cashier shift, or null.</summary>
    private static string? CreateOnly(ContextCheck check) =>
        check.Plan.Operation == Operation.Create ? null : $"{GatewayName(check.Plan.Operation)} does not assert the cashier shift";

    /// <summary>Returns the reason an observed line is a standard-offer, package or offer line, or null.</summary>
    private static string? PlainLine(IReadOnlyDictionary<string, object?> record)
    {
        if (!record.TryGetValue("offer_type", out var offerType)
            || !record.TryGetValue("package_line_role", out var packageRole)
            || !record.TryGetValue("offer_line_role", out var offerRole))
        {
            return "shows no offer type and line roles";
        }

        if (AsDecimal(offerType) == 1)
        {
            return "is a standard-offer line";
        }

        return packageRole is null && offerRole is null ? null : "is a package or offer line";
    }

    /// <summary>Returns the reason an observed line takes its discount and override permissions from a package or bundled offer, or null.</summary>
    private static string? OwnPermissions(IReadOnlyDictionary<string, object?> record)
    {
        if (!record.TryGetValue("package_line_role", out var packageRole) || !record.TryGetValue("offer_line_role", out var offerRole))
        {
            return "shows no line roles";
        }

        if (packageRole is not null)
        {
            return "is a package line";
        }

        return offerRole is null || string.Equals(Convert.ToString(offerRole, CultureInfo.InvariantCulture), "SERVICE", StringComparison.Ordinal)
            ? null
            : "is a bundled-offer line";
    }

    /// <summary>Returns the reason an observed line is not a standard-offer line, or null.</summary>
    private static string? StandardOfferLine(IReadOnlyDictionary<string, object?> record) =>
        record.TryGetValue("offer_type", out var offerType) && AsDecimal(offerType) == 1 ? null : "is not a standard-offer line";

    /// <summary>Returns the input line a check names by service id, else the first input line, or null.</summary>
    private static InvoiceLineDraft? InputLine(ContextCheck check) => check.ServiceId is null
        ? check.Plan.Lines.FirstOrDefault()
        : check.Plan.Lines.FirstOrDefault(line => string.Equals(line.ServiceId, check.ServiceId, StringComparison.Ordinal));

    /// <summary>Describes the input line a check names but the case input lacks.</summary>
    private static string MissingInputLine(ContextCheck check) => check.ServiceId is null
        ? $"{GatewayName(check.Plan.Operation)} input has no line"
        : $"{GatewayName(check.Plan.Operation)} input has no line for service {check.ServiceId}";

    /// <summary>Mirrors ENGINE line_uses_price_override: yn_flag(use_price_override) 'Y' or a non-null price_override.</summary>
    private static bool UsesPriceOverride(InvoiceLineDraft line) =>
        UpperTrimToNull(line.UsePriceOverride) is "Y" or "YES" or "1" or "TRUE" || line.PriceOverride is not null;

    /// <summary>Returns true when an input line carries any package field.</summary>
    private static bool HasPackageMetadata(InvoiceLineDraft line) =>
        line.PackageServiceId is not null
        || line.PackageInstanceId is not null
        || line.PackageLineRole is not null
        || line.PackageComponentOrder is not null
        || line.PackageParentLineId is not null
        || line.PackagePricingMethod is not null
        || line.PackageDefinitionToken is not null;

    /// <summary>Mirrors ENGINE upper_trim_to_null: trims spaces, maps blank to null and upper-cases the rest.</summary>
    private static string? UpperTrimToNull(string? value)
    {
        var trimmed = value?.Trim(' ');
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToUpperInvariant();
    }

    /// <summary>Describes an actual database or package value without its content: null, or the non-null value's type.</summary>
    private static string Withheld(object? value) => value is null or DBNull ? "null" : $"a non-null {value.GetType().Name}";

    /// <summary>Compares a declared JSON value with a database or package value: numbers as decimal, strings ordinally, dates as DateTime, null equal to DBNull.</summary>
    private static bool Matches(JsonElement expected, object? actual)
    {
        if (actual is DBNull)
        {
            actual = null;
        }

        switch (expected.ValueKind)
        {
            case JsonValueKind.Null:
                return actual is null;

            case JsonValueKind.Number:
                return expected.TryGetDecimal(out var number) && AsDecimal(actual) is { } actualNumber && actualNumber == number;

            case JsonValueKind.String:
                var text = expected.GetString()!;
                return actual switch
                {
                    null => false,
                    string actualText => string.Equals(text, actualText, StringComparison.Ordinal),
                    char character => string.Equals(text, character.ToString(), StringComparison.Ordinal),
                    DateTime moment => TryParseMoment(text, out var declared) && declared == moment,
                    DateTimeOffset offset => TryParseMoment(text, out var declaredOffset) && declaredOffset == offset.DateTime,
                    _ => AsDecimal(actual) is { } numeric
                        && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                        && parsed == numeric,
                };

            case JsonValueKind.True or JsonValueKind.False:
                return actual is bool flag && flag == expected.GetBoolean();

            default:
                return false;
        }
    }

    private static decimal? AsDecimal(object? value) => value switch
    {
        sbyte or byte or short or ushort or int or uint or long or ulong or decimal or float or double =>
            Convert.ToDecimal(value, CultureInfo.InvariantCulture),
        string text when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };

    private static bool TryParseMoment(string text, out DateTime moment) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out moment);

    private static string Describe(object? value) => value switch
    {
        null or DBNull => "null",
        DateTime moment => $"DateTime '{moment.ToString("s", CultureInfo.InvariantCulture)}'",
        _ => $"{value.GetType().Name} '{Convert.ToString(value, CultureInfo.InvariantCulture)}'",
    };

    /// <summary>Replaces each {name} token in the expected string values with the case's own expected scalar value of that name.</summary>
    private static JsonElement RenderTemplates(CaseLabel label, JsonElement values)
    {
        if (values.ValueKind != JsonValueKind.Object || !TemplatePattern.IsMatch(values.GetRawText()))
        {
            return values;
        }

        var root = JsonNode.Parse(values.GetRawText())?.AsObject()
            ?? throw label.Precondition("expected.values cannot be parsed.");

        var scalars = new Dictionary<string, JsonNode>(FixtureKeyComparer.Instance);
        foreach (var (key, node) in root)
        {
            if (node is JsonValue scalar && !scalars.TryAdd(key, scalar))
            {
                throw label.Precondition($"expected.values keys '{key}' and '{scalars.Keys.First(existing => FixtureKeyComparer.Instance.Equals(existing, key))}' name the same field.");
            }
        }

        string Render(string text) => TemplatePattern.Replace(text, match =>
        {
            var name = match.Groups["name"].Value;
            if (!scalars.TryGetValue(name, out var node) || node.GetValueKind() == JsonValueKind.String && TemplatePattern.IsMatch(node.GetValue<string>()))
            {
                throw label.Precondition($"expected.values template {{{name}}} names no expected scalar value.");
            }

            return node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : node.ToJsonString();
        });

        var rendered = RenderNode(root, Render);
        using var document = JsonDocument.Parse(rendered?.ToJsonString() ?? "null");
        return document.RootElement.Clone();
    }

    private static JsonNode? RenderNode(JsonNode? node, Func<string, string> render) => node switch
    {
        JsonObject obj => new JsonObject(obj.Select(pair => KeyValuePair.Create(pair.Key, RenderNode(pair.Value, render)))),
        JsonArray array => new JsonArray(array.Select(item => RenderNode(item, render)).ToArray()),
        JsonValue value when value.GetValueKind() == JsonValueKind.String => JsonValue.Create(render(value.GetValue<string>())),
        _ => node?.DeepClone(),
    };

    private static bool NamesAny(JsonElement values, IReadOnlyCollection<string> keys) =>
        values.ValueKind == JsonValueKind.Object
        && values.EnumerateObject().Any(property => keys.Any(key => FixtureKeyComparer.Instance.Equals(property.Name, key)));

    /// <summary>Projects preview totals and lines: the totals fields at top level and under "totals", the lines under "lines".</summary>
    private static Dictionary<string, object?> ProjectPreview(IReadOnlyList<EditablePreviewLine> lines, PreviewTotalsRow totals)
    {
        var totalsValues = ProjectTotals(totals);
        var values = NewValues();
        foreach (var (key, value) in totalsValues)
        {
            values.Add(key, value);
        }

        values.Add("totals", totalsValues);
        values.Add(LinesKey, lines.Select(ProjectPreviewLine).ToList());
        return values;
    }

    private static Dictionary<string, object?> ProjectTotals(PreviewTotalsRow totals) => Values(
        ("line_count", totals.LineCount),
        ("total_gross", totals.TotalGross),
        ("total_discount", totals.TotalDiscount),
        ("total_net", totals.TotalNet),
        ("pat_pay", totals.PatPay),
        ("comp_pay", totals.CompPay),
        ("vat_total_pat", totals.VatTotalPat),
        ("vat_total_co", totals.VatTotalCo),
        ("cash_collected", totals.CashCollected),
        ("amount_1", totals.Amount1),
        ("amount_2", totals.Amount2),
        ("remaining_amount", totals.RemainingAmount),
        ("payment_status", totals.PaymentStatus));

    private static Dictionary<string, object?> ProjectPreviewLine(EditablePreviewLine line) => Values(
        ("client_id", line.ClientId),
        ("line_no", line.LineNo),
        ("serviceid", line.ServiceId),
        ("servicedesc", line.ServiceDesc),
        ("catid", line.CatId),
        ("list_id", line.ListId),
        ("curr_code", line.CurrCode),
        ("qty", line.Qty),
        ("price", line.Price),
        ("plan_discount_pct", line.PlanDiscountPct),
        ("plan_discount_amount", line.PlanDiscountAmount),
        ("manual_discount_type", line.ManualDiscountType),
        ("manual_discount_pct", line.ManualDiscountPct),
        ("manual_discount_amount", line.ManualDiscountAmount),
        ("discount_source", line.DiscountSource),
        ("disc", line.Disc),
        ("my_disc", line.MyDisc),
        ("my_price", line.MyPrice),
        ("my_net", line.MyNet),
        ("the_pay", line.ThePay),
        ("the_comp", line.TheComp),
        ("vat_rate", line.VatRate),
        ("vat_val_pat", line.VatValPat),
        ("vat_val_co", line.VatValCo),
        ("vat_val_pat_ex", line.VatValPatEx),
        ("req_need_a", line.ReqNeedA),
        ("req_a_status", line.ReqAStatus),
        ("allow_manual_discount", line.AllowManualDiscount),
        ("allow_price_override", line.AllowPriceOverride),
        ("package_service_id", line.PackageServiceId),
        ("package_instance_id", line.PackageInstanceId),
        ("package_line_role", line.PackageLineRole),
        ("package_component_order", line.PackageComponentOrder),
        ("package_parent_line_id", line.PackageParentLineId),
        ("package_pricing_method", line.PackagePricingMethod),
        ("package_definition_token", line.PackageDefinitionToken),
        ("offer_id", line.OfferId),
        ("offer_dtl_id", line.OfferDtlId),
        ("offer_type", line.OfferType),
        ("offer_instance_id", line.OfferInstanceId),
        ("offer_line_role", line.OfferLineRole),
        ("offer_parent_line_id", line.OfferParentLineId),
        ("offer_price_applied", line.OfferPriceApplied),
        ("offer_dis_applied", line.OfferDisApplied),
        ("offer_name_snapshot", line.OfferNameSnapshot),
        ("offer_object_version_number", line.OfferObjectVersionNumber),
        ("offer_dtl_object_version_number", line.OfferDtlObjectVersionNumber));

    private static Dictionary<string, object?> ProjectEngineLine(EngineLineInput line) => Values(
        ("serviceid", line.ServiceId),
        ("qty", line.Qty),
        ("price_override", line.PriceOverride),
        ("use_price_override", line.UsePriceOverride),
        ("discount_type", line.DiscountType),
        ("disc", line.Disc),
        ("my_disc", line.MyDisc),
        ("teeth_no", line.TeethNo),
        ("tooth_surface", line.ToothSurface),
        ("teeth_no2", line.TeethNo2),
        ("pat_serv_req_row_id", line.PatServReqRowId),
        ("approv_date", line.ApprovDate),
        ("approv_validity", line.ApprovValidity),
        ("approv_ref_no", line.ApprovRefNo),
        ("claim_no", line.ClaimNo),
        ("req_need_a", line.ReqNeedA),
        ("req_a_status", line.ReqAStatus),
        ("package_service_id", line.PackageServiceId),
        ("package_instance_id", line.PackageInstanceId),
        ("package_line_role", line.PackageLineRole),
        ("package_component_order", line.PackageComponentOrder),
        ("package_parent_line_id", line.PackageParentLineId),
        ("package_pricing_method", line.PackagePricingMethod),
        ("package_definition_token", line.PackageDefinitionToken),
        ("offer_id", line.OfferId),
        ("offer_dtl_id", line.OfferDtlId),
        ("offer_type", line.OfferType),
        ("offer_instance_id", line.OfferInstanceId),
        ("offer_line_role", line.OfferLineRole),
        ("offer_parent_line_id", line.OfferParentLineId),
        ("offer_price_applied", line.OfferPriceApplied),
        ("offer_dis_applied", line.OfferDisApplied),
        ("offer_name_snapshot", line.OfferNameSnapshot),
        ("offer_object_version_number", line.OfferObjectVersionNumber),
        ("offer_dtl_object_version_number", line.OfferDtlObjectVersionNumber));

    private static Dictionary<string, object?> ProjectImportResult(ImportResultRow result) => Values(
        ("source_type", result.SourceType),
        ("source_count", result.SourceCount),
        ("imported_count", result.ImportedCount),
        ("skipped_rejected_count", result.SkippedRejectedCount),
        ("skipped_need_approval_count", result.SkippedNeedApprovalCount),
        ("skipped_invalid_count", result.SkippedInvalidCount),
        ("has_price_overrides", result.HasPriceOverrides),
        ("message", result.Message));

    /// <summary>Projects an import result and its engine lines: the result fields at top level and under "result", the lines under "lines".</summary>
    private static Dictionary<string, object?> ProjectImport(IReadOnlyList<EngineLineInput> lines, ImportResultRow result)
    {
        var resultValues = ProjectImportResult(result);
        var values = NewValues();
        foreach (var (key, value) in resultValues)
        {
            values.Add(key, value);
        }

        values.Add("result", resultValues);
        values.Add(LinesKey, lines.Select(ProjectEngineLine).ToList());
        return values;
    }

    /// <summary>Projects the full-invoice result: the invoice_result fields, posting flags and message at top level, and the invoice_result and message_result records nested.</summary>
    private static Dictionary<string, object?> ProjectCreate(FullInvoiceResultRow result)
    {
        var invoice = Values(
            ("inv_no", result.InvNo),
            ("invdate", result.InvDate),
            ("patientno", result.PatientNo),
            ("curr_code", result.CurrCode),
            ("line_count", result.LineCount),
            ("total_gross", result.TotalGross),
            ("total_discount", result.TotalDiscount),
            ("total_net", result.TotalNet),
            ("pat_pay", result.PatPay),
            ("comp_pay", result.CompPay),
            ("vat_total_pat", result.VatTotalPat),
            ("vat_total_co", result.VatTotalCo),
            ("vat_total", result.VatTotal),
            ("finaldisc", result.FinalDisc),
            ("cash_collected", result.CashCollected),
            ("shift_system_unique", result.ShiftSystemUnique));

        var values = NewValues();
        foreach (var (key, value) in invoice)
        {
            values.Add(key, value);
        }

        values.Add("payment_posted", result.PaymentPosted);
        values.Add("queue_posted", result.QueuePosted);
        values.Add("stock_posted", result.StockPosted);
        values.Add("print_url_built", result.PrintUrlBuilt);
        values.Add("sms_sent", result.SmsSent);
        values.Add("message", result.Message);
        values.Add("invoice_result", invoice);
        values.Add("message_result", Values(("send_status", result.MessageSendStatus), ("message", result.MessageText)));
        return values;
    }

    private static Dictionary<string, object?> NewValues() => new(FixtureKeyComparer.Instance);

    private static Dictionary<string, object?> Values(params (string Key, object? Value)[] pairs)
    {
        var values = NewValues();
        foreach (var (key, value) in pairs)
        {
            values.Add(key, value);
        }

        return values;
    }

    /// <summary>Fixture id and case name of the case under test.</summary>
    private readonly record struct CaseLabel(string Id, string Name)
    {
        /// <summary>Builds the failure reported when the environment or the fixture input does not meet the case's declared preconditions.</summary>
        public FailException Precondition(string detail) => FailException.ForFailure($"{PreconditionPrefix} {Id}/{Name}: {detail}");

        public override string ToString() => $"{Id}/{Name}";
    }

    /// <summary>Result of one case run: the gateway operation it ran, or null when it failed, its failure, and the failure of its rollback, release or disposal.</summary>
    private sealed record CaseOutcome(Operation? Operation, Exception? CaseFailure, Exception? RollbackFailure);

    /// <summary>Gateway call of one case, with every argument read from its input.</summary>
    private sealed record CasePlan(
        Operation Operation,
        InvoiceHeaderDraft? Header,
        IReadOnlyList<InvoiceLineDraft> Lines,
        OperatorContext Operator,
        bool Amount1Auto,
        string RequestId,
        InvoiceEntryParameters? Parameters,
        int? ApprovalMode,
        string? VisitUnique,
        IReadOnlyList<long> PatServReqRowIds,
        decimal? OfferId,
        decimal? BundleQty,
        string? PackageServiceId,
        decimal? ListId,
        string? ParentSourceId,
        VisitLineChoice? VisitChoice);

    /// <summary>One declared shared-package value: its source name, package and field, an optional line service id, the declared value, the output relation that echoes it and the case's gateway call.</summary>
    private sealed record ContextCheck(
        string Source,
        string? Package,
        string Field,
        string? ServiceId,
        JsonElement Value,
        ContextRelation? Relation,
        CasePlan Plan);

    /// <summary>Output field echoing a declared value, the output value it implies, the input and observed-line conditions under which the echo holds, and whether completion alone observes it.</summary>
    /// <param name="Output">Output field carrying the echo.</param>
    /// <param name="Expect">Output value a declared value implies, or null when the declared value has no relation.</param>
    /// <param name="InputCondition">Reason the case input prevents the echo, or null.</param>
    /// <param name="ObservedCondition">Reason an observed line or output does not carry the echo, or null.</param>
    /// <param name="Completion">True when the operation returning its output observes the declared value.</param>
    private sealed record ContextRelation(
        string Output,
        Func<JsonElement, ContextCheck, JsonElement?> Expect,
        Func<ContextCheck, string?>? InputCondition = null,
        Func<IReadOnlyDictionary<string, object?>, string?>? ObservedCondition = null,
        bool Completion = false);

    /// <summary>Key comparer that ignores case and underscores, so inv_no, INV_NO and invNo name the same field.</summary>
    private sealed class FixtureKeyComparer : IEqualityComparer<string>
    {
        public static FixtureKeyComparer Instance { get; } = new();

        public bool Equals(string? x, string? y) => string.Equals(Fold(x), Fold(y), StringComparison.Ordinal);

        public int GetHashCode(string obj) => StringComparer.Ordinal.GetHashCode(Fold(obj));

        private static string Fold(string? key) =>
            key is null ? string.Empty : key.Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
    }
}
