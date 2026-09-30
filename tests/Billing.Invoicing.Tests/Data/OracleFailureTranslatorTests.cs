using System.Globalization;
using System.Net.Sockets;
using System.Reflection;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Data.Oracle;
using Oracle.ManagedDataAccess.Client;

namespace Billing.Invoicing.Tests.Data;

/// <summary>Classification, attribution and payload tests for <see cref="OracleFailureTranslator"/>.</summary>
[Trait("Category", "DataUnit")]
public sealed class OracleFailureTranslatorTests
{
    private const string UnknownPackage = "UNKNOWN";
    private const string PaymentPackage = "BIL_PAYMENT";
    private const string OracleErrorMessage = "The Oracle database returned an error.";
    private const string UnavailableMessage = "Oracle database is unavailable.";
    private const string ConfigurationFaultMessage = "The Oracle connection string is not configured or is not well-formed.";

    private const string RequestUnavailableText =
        "One or more requested services were already invoiced or are no longer available.";

    private const string PatientRequiredText = "Invoice create failed: patient number is required.";
    private const string BundledOfferText = "Bundled Offers are available only for Cash invoices.";
    private const string RequestImportQuantityText = "Request import failed: selected request line 55 has invalid quantity.";

    private const string PackageExpansionQuantityText =
        "Request package expansion failed: multiplied quantity exceeds the supported two-decimal quantity precision for component 9.";
    private const string ListenerHostText = "Cannot connect. No listener at host 10.1.2.3 port 1521. (CONNECTION_ID=AbCdEf123==)";

    private static readonly string EngineFrame = Frame("HIS.BIL_INVOICE_ENGINE", 619);
    private static readonly string ApiFrame = Frame("HIS.BIL_INVOICE_API", 1476);
    private static readonly string NextInvoiceNoFrame = Frame("HIS.GET_NEXT_INVOICE_NO", 12);
    private static readonly string PaymentFrame = Frame("HIS.BIL_PAYMENT", 88);

    private static readonly ConstructorInfo? OracleExceptionConstructor = typeof(OracleException).GetConstructor(
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        new[] { typeof(int), typeof(string), typeof(string), typeof(string), typeof(Exception) },
        modifiers: null);

    private readonly OracleFailureTranslator translator = new();

    public static TheoryData<int> ConnectivityNumbers => new()
    {
        12154, 12170, 12505, 12514, 12528, 12537, 12541, 12543, 12545,
        3113, 3114, 3135,
        1033, 1034, 1089,
    };

    public static TheoryData<int> CatalogueRowIndexes
    {
        get
        {
            var indexes = new TheoryData<int>();
            for (int index = 0; index < OracleErrorCatalog.Rows.Count; index++)
            {
                indexes.Add(index);
            }

            return indexes;
        }
    }

    [Fact]
    public void Translate_ApplicationError_Is422WithNumberPackageAndText()
    {
        DataFailure failure = translator.Translate(Err(20900, PatientRequiredText, "CreateFullInvoice", false, EngineFrame));

        Assert.Equal(422, failure.Status);
        Assert.Equal(DataFailure.OracleBusinessErrorType, failure.Type);
        Assert.Equal(-20900, failure.Number);
        Assert.Equal(OracleErrorCatalog.EnginePackage, failure.Package);
        Assert.Equal(PatientRequiredText, failure.Message);
        Assert.Equal("PATIENTNO", failure.Field);
    }

    [Fact]
    public void Translate_ApplicationErrorWhileOpening_Is422()
    {
        DataFailure failure = translator.Translate(Err(20900, PatientRequiredText, duringOpen: true));

        Assert.Equal(422, failure.Status);
        Assert.Equal(DataFailure.OracleBusinessErrorType, failure.Type);
        Assert.Equal(-20900, failure.Number);
    }

    [Fact]
    public void Translate_UncataloguedApplicationErrorWhileOpening_Is422()
    {
        DataFailure failure = translator.Translate(Err(20001, "logon trigger refused", duringOpen: true));

        Assert.Equal(422, failure.Status);
        Assert.Equal(DataFailure.OracleBusinessErrorType, failure.Type);
        Assert.Equal(-20001, failure.Number);
        Assert.Equal(UnknownPackage, failure.Package);
        Assert.Equal("logon trigger refused", failure.Message);
    }

    [Theory]
    [InlineData(20000, -20000)]
    [InlineData(20500, -20500)]
    [InlineData(20999, -20999)]
    public void Translate_ApplicationRangeBoundaries_Are422(int number, int expected)
    {
        DataFailure failure = translator.Translate(Err(number, "x"));

        Assert.Equal(422, failure.Status);
        Assert.Equal(DataFailure.OracleBusinessErrorType, failure.Type);
        Assert.Equal(expected, failure.Number);
        Assert.Equal(UnknownPackage, failure.Package);
        Assert.Null(failure.Kind);
        Assert.Null(failure.Field);
        Assert.Null(failure.LegacyText);
        Assert.Equal("x", failure.Message);
    }

    [Theory]
    [InlineData(20000, -20000)]
    [InlineData(20500, -20500)]
    [InlineData(20999, -20999)]
    public void Translate_UncataloguedApplicationErrorWithFrame_AttributesTheFramePackage(int number, int expected)
    {
        DataFailure failure = translator.Translate(Err(number, "x", "CreateFullInvoice", false, EngineFrame));

        Assert.Equal(422, failure.Status);
        Assert.Equal(DataFailure.OracleBusinessErrorType, failure.Type);
        Assert.Equal(expected, failure.Number);
        Assert.Equal(OracleErrorCatalog.EnginePackage, failure.Package);
        Assert.Null(failure.Kind);
        Assert.Null(failure.Field);
        Assert.Equal("x", failure.Message);
    }

    [Theory]
    [MemberData(nameof(ConnectivityNumbers))]
    public void Translate_ConnectivityNumber_Is503WithFixedMessage(int number)
    {
        foreach (bool duringOpen in new[] { false, true })
        {
            DataFailure failure = translator.Translate(Err(number, "connection detail", duringOpen: duringOpen));

            Assert.Equal(503, failure.Status);
            Assert.Equal(DataFailure.OracleUnavailableType, failure.Type);
            Assert.Equal(number, failure.Number);
            Assert.Equal(UnavailableMessage, failure.Message);
            Assert.Null(failure.Package);
        }
    }

    [Theory]
    [InlineData(1017)]
    [InlineData(1045)]
    [InlineData(28000)]
    [InlineData(19999)]
    [InlineData(21000)]
    public void Translate_ConfigurationFaultOrOtherCode_Is500EvenWhileOpening(int number)
    {
        foreach (bool duringOpen in new[] { false, true })
        {
            DataFailure failure = translator.Translate(Err(number, "logon detail", duringOpen: duringOpen));

            Assert.Equal(500, failure.Status);
            Assert.Equal(DataFailure.OracleErrorType, failure.Type);
            Assert.Equal(number, failure.Number);
            Assert.Equal(OracleErrorMessage, failure.Message);
            Assert.Null(failure.Kind);
        }
    }

    [Theory]
    [MemberData(nameof(CatalogueRowIndexes))]
    public void Translate_CatalogueRow_CarriesPackageKindFieldAndLegacyText(int index)
    {
        var row = OracleErrorCatalog.Rows[index];
        string text = row.MessagePrefix + "tail";

        DataFailure failure = translator.Translate(Err(-row.Number, text));

        bool operatorContext = row.Kind == OracleErrorCatalog.OperatorContextMissingKind;
        Assert.Equal(422, failure.Status);
        Assert.Equal(operatorContext ? DataFailure.OperatorContextMissingType : DataFailure.OracleBusinessErrorType, failure.Type);
        Assert.Equal(row.Number, failure.Number);
        Assert.Equal(row.Package, failure.Package);
        Assert.Equal(row.Kind, failure.Kind);
        Assert.Equal(row.Field, failure.Field);
        Assert.Equal(row.LegacyText, failure.LegacyText);
        Assert.Equal(text, failure.Message);
    }

    [Theory]
    [InlineData(20900, "Invoice create failed: patient number is required.", "CreateFullInvoice", OracleErrorCatalog.EnginePackage, "PATIENTNO", null)]
    [InlineData(20923, "Invoice create failed: clinic is required when doctor is supplied.", "CreateFullInvoice", OracleErrorCatalog.EnginePackage, "CLINICID", null)]
    [InlineData(20924, "Invoice create failed: selected doctor does not belong to the selected clinic.", "CalculatePreview", OracleErrorCatalog.EnginePackage, "DOCIDX", null)]
    [InlineData(20903, "Invoice create failed: at least one service line is required.", "CreateFullInvoice", OracleErrorCatalog.EnginePackage, null, "Invoice without Details")]
    [InlineData(20905, "Invoice create failed: quantity must be a positive whole number on line 2.", "CalculatePreview", OracleErrorCatalog.EnginePackage, "QTY", null)]
    [InlineData(20914, "Invoice create failed: final discount cannot exceed patient share.", "CreateFullInvoice", OracleErrorCatalog.EnginePackage, "FINALDISC", "discount is greater than cash payed amount")]
    [InlineData(20780, "Invoice line 1 uses price override but PRICE_OVERRIDE is null.", "CalculatePreview", OracleErrorCatalog.EnginePackage, "PRICE", null)]
    [InlineData(20781, "Invoice line 1 price override cannot be negative.", "CreateFullInvoice", OracleErrorCatalog.EnginePackage, "PRICE", null)]
    [InlineData(20871, "Bundled Offers are available only for Cash invoices.", "GetBundledOfferLines", OracleErrorCatalog.ApiPackage, "OFERID", null)]
    public void Translate_PackageErrorText_MapsToItsField(int number, string text, string operation, string package, string? field, string? legacyText)
    {
        DataFailure failure = translator.Translate(Err(number, text, operation));

        Assert.Equal(422, failure.Status);
        Assert.Equal(DataFailure.OracleBusinessErrorType, failure.Type);
        Assert.Equal(-number, failure.Number);
        Assert.Equal(package, failure.Package);
        Assert.Equal(field, failure.Field);
        Assert.Equal(legacyText, failure.LegacyText);
        Assert.Null(failure.Kind);
        Assert.Equal(text, failure.Message);
    }

    [Theory]
    [InlineData(20931, "One or more requested services were already invoiced or are no longer available.", "CreateFullInvoice", OracleErrorCatalog.EnginePackage, OracleErrorCatalog.RequestLinesStaleKind)]
    [InlineData(20930, "Invoice create failed: request line 77 was already invoiced by another session.", "CreateFullInvoice", OracleErrorCatalog.EnginePackage, OracleErrorCatalog.RequestLinesStaleKind)]
    [InlineData(20969, "The package definition changed after the invoice was calculated. Refresh the invoice and review the package lines.", "CreateFullInvoice", OracleErrorCatalog.EnginePackage, OracleErrorCatalog.DefinitionStaleKind)]
    [InlineData(20970, "The offer changed after the invoice was calculated. Refresh the invoice and review the updated pricing.", "CalculatePreview", OracleErrorCatalog.EnginePackage, OracleErrorCatalog.DefinitionStaleKind)]
    [InlineData(20848, "Invoice request 0A1B2C3D4E5F60718293A4B5C6D7E8F9 refers to unavailable invoice 105.", "CreateFullInvoice", OracleErrorCatalog.ApiPackage, OracleErrorCatalog.IdempotencyConflictKind)]
    [InlineData(20848, "Invoice request 0A1B2C3D4E5F60718293A4B5C6D7E8F9 exists but has no completed invoice.", "CreateFullInvoice", OracleErrorCatalog.ApiPackage, OracleErrorCatalog.IdempotencyConflictKind)]
    [InlineData(20848, "Invoice request 0A1B2C3D4E5F60718293A4B5C6D7E8F9 could not be completed for invoice 105.", "CreateFullInvoice", OracleErrorCatalog.ApiPackage, OracleErrorCatalog.IdempotencyConflictKind)]
    [InlineData(20849, "Invoice request 0A1B2C3D4E5F60718293A4B5C6D7E8F9 already belongs to invoice 105 for another patient.", "CreateFullInvoice", OracleErrorCatalog.ApiPackage, OracleErrorCatalog.IdempotencyConflictKind)]
    public void Translate_PackageErrorText_CarriesItsKind(int number, string text, string operation, string package, string kind)
    {
        DataFailure failure = translator.Translate(Err(number, text, operation));

        Assert.Equal(422, failure.Status);
        Assert.Equal(DataFailure.OracleBusinessErrorType, failure.Type);
        Assert.Equal(-number, failure.Number);
        Assert.Equal(package, failure.Package);
        Assert.Equal(kind, failure.Kind);
        Assert.Null(failure.Field);
        Assert.Null(failure.LegacyText);
        Assert.Equal(text, failure.Message);
    }

    [Fact]
    public void Translate_IdempotencyConflict_KeepsTheNamedInvoiceNumber()
    {
        const string text = "Invoice request 0A1B2C3D4E5F60718293A4B5C6D7E8F9 already belongs to invoice 105 for another patient.";

        DataFailure failure = translator.Translate(Err(20849, text, "CreateFullInvoice", false, ApiFrame));

        Assert.Equal(OracleErrorCatalog.IdempotencyConflictKind, failure.Kind);
        Assert.Contains("invoice 105", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ORA-", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(20778, "Request import failed: application ID is required.")]
    [InlineData(20779, "Request import failed: application session is required.")]
    [InlineData(20782, "Request import failed: application user is required.")]
    [InlineData(20779, "Request selection failed: application session is required.")]
    [InlineData(20782, "Request selection clear failed: application user is required.")]
    public void Translate_ImportOperatorContextError_IsOperatorContextMissingWithOracleDetails(int number, string text)
    {
        DataFailure failure = translator.Translate(Err(number, text, "ImportRequestLines"));

        Assert.Equal(422, failure.Status);
        Assert.Equal(DataFailure.OperatorContextMissingType, failure.Type);
        Assert.Equal(OracleErrorCatalog.OperatorContextMissingKind, failure.Kind);
        Assert.Equal(-number, failure.Number);
        Assert.Equal(OracleErrorCatalog.ImportPackage, failure.Package);
        Assert.Equal(text, failure.Message);
    }

    [Theory]
    [InlineData(RequestImportQuantityText)]
    [InlineData(PackageExpansionQuantityText)]
    public void Translate_Import20771_MatchesEachPrefix(string text)
    {
        DataFailure failure = translator.Translate(Err(20771, text, "ImportRequestLines"));

        Assert.Equal(422, failure.Status);
        Assert.Equal(DataFailure.OracleBusinessErrorType, failure.Type);
        Assert.Equal(-20771, failure.Number);
        Assert.Equal(OracleErrorCatalog.ImportPackage, failure.Package);
        Assert.Null(failure.Kind);
        Assert.Equal(text, failure.Message);
    }

    [Fact]
    public void Find_Import20771_ReturnsADistinctRowPerMessagePrefix()
    {
        string[] candidates = { OracleErrorCatalog.ImportPackage };

        var requestImport = OracleErrorCatalog.Find(candidates, -20771, RequestImportQuantityText);
        var packageExpansion = OracleErrorCatalog.Find(candidates, -20771, PackageExpansionQuantityText);

        Assert.NotNull(requestImport);
        Assert.NotNull(packageExpansion);
        Assert.Equal(OracleErrorCatalog.ImportPackage, requestImport.Value.Package);
        Assert.Equal(OracleErrorCatalog.ImportPackage, packageExpansion.Value.Package);
        Assert.StartsWith(requestImport.Value.MessagePrefix, RequestImportQuantityText, StringComparison.Ordinal);
        Assert.StartsWith(packageExpansion.Value.MessagePrefix, PackageExpansionQuantityText, StringComparison.Ordinal);
        Assert.NotEqual(requestImport.Value.MessagePrefix, packageExpansion.Value.MessagePrefix);
    }

    [Theory]
    [InlineData(OracleErrorCatalog.EnginePackage, -20914)]
    [InlineData(OracleErrorCatalog.EnginePackage, -20931)]
    [InlineData(OracleErrorCatalog.ApiPackage, -20871)]
    [InlineData(OracleErrorCatalog.ImportPackage, -20771)]
    [InlineData(OracleErrorCatalog.ImportPackage, -20778)]
    public void Find_CataloguedNumberWithUnrelatedText_ReturnsNoRow(string package, int number)
    {
        Assert.Null(OracleErrorCatalog.Find(new[] { package }, number, "Some other failure."));
    }

    [Fact]
    public void Translate_Import20771WithUnknownPrefix_IsUncatalogued()
    {
        DataFailure failure = translator.Translate(Err(20771, "Some other import failure.", "ImportRequestLines"));

        Assert.Equal(422, failure.Status);
        Assert.Equal(UnknownPackage, failure.Package);
        Assert.Null(failure.Kind);
    }

    [Fact]
    public void Translate_RowOutsideOperationCandidates_IsNotMatched()
    {
        DataFailure failure = translator.Translate(Err(20871, BundledOfferText, "ImportRequestLines"));

        Assert.Equal(422, failure.Status);
        Assert.Equal(DataFailure.OracleBusinessErrorType, failure.Type);
        Assert.Equal(UnknownPackage, failure.Package);
        Assert.Null(failure.Field);
        Assert.Null(failure.Kind);
        Assert.Null(failure.LegacyText);
    }

    [Fact]
    public void Translate_RowOutsideOperationCandidatesWithFrame_AttributesTheFrameWithoutField()
    {
        DataFailure failure = translator.Translate(Err(20871, BundledOfferText, "ImportRequestLines", false, ApiFrame));

        Assert.Equal(422, failure.Status);
        Assert.Equal(OracleErrorCatalog.ApiPackage, failure.Package);
        Assert.Null(failure.Field);
        Assert.Null(failure.Kind);
        Assert.Null(failure.LegacyText);
    }

    [Fact]
    public void Find_RowOutsideCandidates_ReturnsNoRow()
    {
        string[] importCandidates = { OracleErrorCatalog.ImportPackage, OracleErrorCatalog.EnginePackage };

        Assert.Null(OracleErrorCatalog.Find(importCandidates, -20871, BundledOfferText));
        Assert.NotNull(OracleErrorCatalog.Find(new[] { OracleErrorCatalog.ApiPackage }, -20871, BundledOfferText));
    }

    [Theory]
    [InlineData("GetBundledOfferLines")]
    [InlineData("NotAnOperation")]
    [InlineData(null)]
    public void Translate_RowInsideOperationCandidates_IsMatched(string? operation)
    {
        DataFailure failure = translator.Translate(Err(20871, BundledOfferText, operation));

        Assert.Equal(OracleErrorCatalog.ApiPackage, failure.Package);
        Assert.Equal("OFERID", failure.Field);
    }

    [Fact]
    public void Translate_NotImplementedWithOpenItem_Is501WithOpenItemId()
    {
        DataFailure? failure = translator.Translate(new NotImplementedException("OI-11: print URL result type unknown."));

        Assert.NotNull(failure);
        Assert.Equal(501, failure.Status);
        Assert.Equal(DataFailure.OpenItemType, failure.Type);
        Assert.Equal("OI-11", failure.OpenItemId);
        Assert.Equal("OI-11: print URL result type unknown.", failure.Message);
    }

    [Theory]
    [InlineData("blocked")]
    [InlineData("OI-1: x")]
    [InlineData("OI-123: x")]
    [InlineData(" OI-11: x")]
    [InlineData("oi-11: x")]
    [InlineData("The method or operation is not implemented.")]
    public void Translate_NotImplementedWithoutOpenItem_ReturnsNull(string message)
    {
        Assert.Null(translator.Translate(new NotImplementedException(message)));
    }

    [Fact]
    public void Translate_NotImplementedWithBareOpenItemId_Is501()
    {
        DataFailure? failure = translator.Translate(new NotImplementedException("OI-56"));

        Assert.NotNull(failure);
        Assert.Equal(501, failure.Status);
        Assert.Equal("OI-56", failure.OpenItemId);
        Assert.Equal("OI-56", failure.Message);
    }

    [Fact]
    public void Translate_WrappedOpenItem_Is501WithTheInnerIdAndMessage()
    {
        const string message = "OI-31: package consumption registration is not available.";
        var blocked = new NotImplementedException(message);

        foreach (Exception exception in new Exception[]
        {
            new InvalidOperationException("workflow failed", blocked),
            new InvalidOperationException("request failed", new AggregateException("step failed", blocked)),
            new NotImplementedException("blocked", blocked),
        })
        {
            DataFailure? failure = translator.Translate(exception);

            Assert.NotNull(failure);
            Assert.Equal(501, failure.Status);
            Assert.Equal(DataFailure.OpenItemType, failure.Type);
            Assert.Equal("OI-31", failure.OpenItemId);
            Assert.Equal(message, failure.Message);
        }
    }

    [Fact]
    public void Translate_WrappedOpenItems_TakesTheOutermostOpenItem()
    {
        var inner = new NotImplementedException("OI-24: price plan is not available.");
        var outer = new NotImplementedException("OI-23: paid-before check is not available.", inner);

        DataFailure? failure = translator.Translate(new InvalidOperationException("workflow failed", outer));

        Assert.NotNull(failure);
        Assert.Equal("OI-23", failure.OpenItemId);
        Assert.Equal(outer.Message, failure.Message);
    }

    [Fact]
    public void Translate_WrapperWithoutOpenItem_ReturnsNull()
    {
        Assert.Null(translator.Translate(new InvalidOperationException("workflow failed", new ArgumentException("x"))));
        Assert.Null(translator.Translate(new InvalidOperationException("workflow failed", new NotImplementedException("blocked"))));
    }

    [Fact]
    public void Translate_OracleErrorAndOpenItemInChain_IsTheOracleFailure()
    {
        OracleException driver = Driver(20931, "ORA-20931: " + RequestUnavailableText + "\n" + EngineFrame);
        driver.Data[OracleErrorParser.OperationKey] = "CreateFullInvoice";

        foreach (Exception exception in new Exception[]
        {
            new NotImplementedException("OI-31: package consumption registration is not available.", driver),
            new InvalidOperationException("workflow failed", new NotImplementedException("OI-31: blocked", driver)),
        })
        {
            DataFailure? failure = translator.Translate(exception);

            Assert.NotNull(failure);
            Assert.Equal(422, failure.Status);
            Assert.Equal(DataFailure.OracleBusinessErrorType, failure.Type);
            Assert.Equal(-20931, failure.Number);
            Assert.Null(failure.OpenItemId);
        }
    }

    [Fact]
    public void Translate_OpenItemWrappingSocketFailure_Is501()
    {
        DataFailure? failure = translator.Translate(new NotImplementedException("OI-12: SMS gateway is not available.", new SocketException(10061)));

        Assert.NotNull(failure);
        Assert.Equal(501, failure.Status);
        Assert.Equal("OI-12", failure.OpenItemId);
    }

    [Fact]
    public void Translate_NotImplementedWithoutOpenItemWrappingSocketFailure_Is503()
    {
        DataFailure? failure = translator.Translate(new NotImplementedException("blocked", new SocketException(10061)));

        Assert.NotNull(failure);
        Assert.Equal(503, failure.Status);
        Assert.Equal(DataFailure.OracleUnavailableType, failure.Type);
        Assert.Null(failure.Number);
        Assert.Null(failure.OpenItemId);
        Assert.Equal(UnavailableMessage, failure.Message);
    }

    [Fact]
    public void Translate_TransportFailureAndWrappedOpenItemInChain_Is503()
    {
        foreach (Exception exception in new Exception[]
        {
            new TimeoutException("driver timed out", new NotImplementedException("OI-31: blocked")),
            new InvalidOperationException("workflow step", new NotImplementedException("OI-31: blocked", new SocketException(10061))),
            new InvalidOperationException("workflow step", new SocketException(10061)),
        })
        {
            DataFailure? failure = translator.Translate(exception);

            Assert.NotNull(failure);
            Assert.Equal(503, failure.Status);
            Assert.Equal(DataFailure.OracleUnavailableType, failure.Type);
            Assert.Null(failure.Number);
            Assert.Null(failure.OpenItemId);
            Assert.Equal(UnavailableMessage, failure.Message);
        }
    }

    [Fact]
    public void Translate_ConfigurationFault_Is500WithFixedMessage()
    {
        const string connectionString = "Data Source=HISDB;User Id=his;Password=secret;Pooling=maybe";
        var invalid = new InvalidOperationException("ORA-50029: OracleConnection.ConnectionString is invalid");
        invalid.Data[OracleFailureTranslator.ConfigurationFaultKey] = true;
        var malformed = new ArgumentException("ORA-50007: " + connectionString);
        malformed.Data[OracleFailureTranslator.ConfigurationFaultKey] = true;
        var wrappedMalformed = new ArgumentException("ORA-50008: " + connectionString);
        wrappedMalformed.Data[OracleFailureTranslator.ConfigurationFaultKey] = true;

        foreach (Exception exception in new Exception[]
        {
            invalid,
            malformed,
            new InvalidOperationException("open failed", wrappedMalformed),
        })
        {
            AssertConfigurationFault(translator.Translate(exception), connectionString);
        }
    }

    [Fact]
    public void Translate_UnmarkedOrFalseMarkedArgumentException_ReturnsNull()
    {
        var falseMarked = new ArgumentException("x");
        falseMarked.Data[OracleFailureTranslator.ConfigurationFaultKey] = false;
        var stringMarked = new ArgumentException("x");
        stringMarked.Data[OracleFailureTranslator.ConfigurationFaultKey] = "true";

        Assert.Null(translator.Translate(new ArgumentException("x")));
        Assert.Null(translator.Translate(falseMarked));
        Assert.Null(translator.Translate(stringMarked));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData(";")]
    [InlineData(" ; ;")]
    [InlineData(null)]
    public async Task Translate_BlankConnectionStringFromFactory_Is500WithFixedMessage(string? connectionString)
    {
        var factory = new OracleSessionFactory(new InvoicingDataOptions { ConnectionString = connectionString! });

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.Open());

        Assert.True(thrown.Data[OracleFailureTranslator.ConfigurationFaultKey] is true);
        Assert.Null(thrown.InnerException);
        AssertConfigurationFault(translator.Translate(thrown), connectionString: null);
    }

    [Fact]
    public void Translate_UnrelatedException_ReturnsNull()
    {
        Assert.Null(translator.Translate(new InvalidOperationException("unrelated")));
        Assert.Null(translator.Translate(new ArgumentException("x")));
    }

    [Fact]
    public void Translate_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => translator.Translate((OracleErrorInfo)null!));
        Assert.Throws<ArgumentNullException>(() => translator.Translate((Exception)null!));
    }

    [Fact]
    public void Translate_DriverNumberDisagreesWithStack_Is500WithFixedMessage()
    {
        string stack = "ORA-20931: " + RequestUnavailableText + "\n" + EngineFrame + "\n" + ApiFrame;
        OracleException driver = Driver(20930, stack);
        Assert.Contains("ORA-06512", driver.Message, StringComparison.Ordinal);

        DataFailure? failure = translator.Translate(driver);

        Assert.NotNull(failure);
        Assert.Equal(500, failure.Status);
        Assert.Equal(DataFailure.OracleErrorType, failure.Type);
        Assert.Equal(-20930, failure.Number);
        Assert.Null(failure.Package);
        Assert.Equal(OracleErrorMessage, failure.Message);
        Assert.DoesNotContain("ORA-06512", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("HIS.", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Translate_WrappedDriverException_IsParsedFromInnerException()
    {
        OracleException driver = Driver(20931, "ORA-20931: " + RequestUnavailableText + "\n" + EngineFrame);
        driver.Data[OracleErrorParser.OperationKey] = "CreateFullInvoice";
        var wrapped = new InvalidOperationException("gateway failed", driver);

        DataFailure? failure = translator.Translate(wrapped);

        Assert.NotNull(failure);
        Assert.Equal(422, failure.Status);
        Assert.Equal(DataFailure.OracleBusinessErrorType, failure.Type);
        Assert.Equal(OracleErrorCatalog.RequestLinesStaleKind, failure.Kind);
        Assert.Equal(-20931, failure.Number);
        Assert.Equal(OracleErrorCatalog.EnginePackage, failure.Package);
        Assert.Equal(RequestUnavailableText, failure.Message);
    }

    [Fact]
    public void Translate_DriverConnectivityError_Is503WithoutHostDetails()
    {
        DataFailure? failure = translator.Translate(Driver(12541, "ORA-12541: " + ListenerHostText));

        Assert.NotNull(failure);
        Assert.Equal(503, failure.Status);
        Assert.Equal(12541, failure.Number);
        Assert.Equal(UnavailableMessage, failure.Message);
        Assert.DoesNotContain("10.1.2.3", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("host", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Translate_DriverErrorWithSocketFailure_Is503WithNumber()
    {
        OracleException driver = Driver(1017, "ORA-01017: invalid username/password; logon denied", new SocketException(10061));

        DataFailure? failure = translator.Translate(driver);

        Assert.NotNull(failure);
        Assert.Equal(503, failure.Status);
        Assert.Equal(DataFailure.OracleUnavailableType, failure.Type);
        Assert.Equal(1017, failure.Number);
        Assert.Equal(UnavailableMessage, failure.Message);
    }

    [Fact]
    public void Translate_DriverErrorWhileOpening_Is500()
    {
        OracleException driver = Driver(1017, "ORA-01017: invalid username/password; logon denied");
        driver.Data[OracleErrorParser.DuringOpenKey] = true;

        DataFailure? failure = translator.Translate(driver);

        Assert.NotNull(failure);
        Assert.Equal(500, failure.Status);
        Assert.Equal(1017, failure.Number);
        Assert.Equal(OracleErrorMessage, failure.Message);
    }

    [Fact]
    public void Translate_TimeoutWithPath_Is503WithFixedMessage()
    {
        var timeout = new TimeoutException("retry failed at /etc/his/connection.conf");

        foreach (Exception exception in new Exception[] { timeout, new InvalidOperationException("open failed", timeout) })
        {
            DataFailure? failure = translator.Translate(exception);

            Assert.NotNull(failure);
            Assert.Equal(503, failure.Status);
            Assert.Equal(DataFailure.OracleUnavailableType, failure.Type);
            Assert.Null(failure.Number);
            Assert.Equal(UnavailableMessage, failure.Message);
            Assert.DoesNotContain("/etc", failure.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Translate_SocketFailure_Is503WithFixedMessage()
    {
        var socket = new SocketException(10061);

        foreach (Exception exception in new Exception[] { socket, new InvalidOperationException("open failed", socket) })
        {
            DataFailure? failure = translator.Translate(exception);

            Assert.NotNull(failure);
            Assert.Equal(503, failure.Status);
            Assert.Equal(DataFailure.OracleUnavailableType, failure.Type);
            Assert.Null(failure.Number);
            Assert.Equal(UnavailableMessage, failure.Message);
        }
    }

    [Fact]
    public void Translate_ParsedConnectivityErrorWithHost_Is503WithFixedMessage()
    {
        DataFailure failure = translator.Translate(OracleErrorParser.FromParts(12541, "ORA-12541: " + ListenerHostText));

        Assert.Equal(503, failure.Status);
        Assert.Equal(UnavailableMessage, failure.Message);
    }

    [Fact]
    public void Translate_UnlistedListenerErrorWithHost_Is500WithFixedMessage()
    {
        const string message = "ORA-12516: Cannot connect. Listener at host 10.1.2.3 port 1521 has no available handler for this service.";

        DataFailure failure = translator.Translate(OracleErrorParser.FromParts(12516, message));

        Assert.Equal(500, failure.Status);
        Assert.Equal(12516, failure.Number);
        Assert.Equal(OracleErrorMessage, failure.Message);
        Assert.DoesNotContain("10.1.2.3", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Translate_UncataloguedErrorBelowStandaloneFunction_AttributesTheCallingPackage()
    {
        DataFailure failure = translator.Translate(Err(20555, "x", "CreateFullInvoice", false, NextInvoiceNoFrame, EngineFrame));

        Assert.Equal(422, failure.Status);
        Assert.Equal(OracleErrorCatalog.EnginePackage, failure.Package);
    }

    [Fact]
    public void Translate_UncataloguedErrorBelowTrigger_AttributesTheCallingPackage()
    {
        DataFailure failure = translator.Translate(Err(
            20555,
            "x",
            null,
            false,
            Frame("HIS.T_INV_BIU", 5),
            "ORA-04088: error during execution of trigger 'HIS.T_INV_BIU'",
            ApiFrame));

        Assert.Equal(422, failure.Status);
        Assert.Equal(OracleErrorCatalog.ApiPackage, failure.Package);
    }

    [Fact]
    public void Translate_UncataloguedErrorWithOnlyFunctionFrame_IsUnknown()
    {
        DataFailure failure = translator.Translate(Err(20555, "x", null, false, NextInvoiceNoFrame));

        Assert.Equal(UnknownPackage, failure.Package);
    }

    [Fact]
    public void Translate_UncataloguedErrorInUnlistedPackage_AttributesThatPackage()
    {
        DataFailure failure = translator.Translate(Err(20555, "x", null, false, Frame("SYS.UTL_HTTP", 1130), PaymentFrame));

        Assert.Equal(422, failure.Status);
        Assert.Equal("UTL_HTTP", failure.Package);
    }

    [Fact]
    public void Translate_UncataloguedErrorBelowLowerCaseStandaloneRoutine_AttributesTheCallingPackage()
    {
        DataFailure failure = translator.Translate(Err(20555, "x", null, false, Frame("his.get_next_invoice_no", 3), PaymentFrame));

        Assert.Equal(PaymentPackage, failure.Package);
    }

    [Fact]
    public void Translate_FrameSharingTriggerNameInAnotherSchema_IsAPackage()
    {
        DataFailure failure = translator.Translate(Err(
            20555,
            "x",
            null,
            false,
            Frame("OTHER.T_INV_BIU", 7),
            "ORA-04088: error during execution of trigger 'HIS.T_INV_BIU'",
            ApiFrame));

        Assert.Equal("T_INV_BIU", failure.Package);
    }

    [Fact]
    public void Translate_GenericErrorWithOnlyFunctionFrame_HasNoPackage()
    {
        DataFailure failure = translator.Translate(Err(6502, "PL/SQL: numeric or value error", null, false, NextInvoiceNoFrame));

        Assert.Equal(500, failure.Status);
        Assert.Null(failure.Package);
    }

    [Fact]
    public void Translate_GenericErrorWithPackageFrame_AttributesThePackage()
    {
        DataFailure failure = translator.Translate(Err(6502, "PL/SQL: numeric or value error", null, false, NextInvoiceNoFrame, PaymentFrame));

        Assert.Equal(500, failure.Status);
        Assert.Equal(PaymentPackage, failure.Package);
        Assert.Equal(OracleErrorMessage, failure.Message);
    }

    /// <summary>Parses an error from a positive driver number, its text and ORA-06512 lines.</summary>
    /// <param name="number">ORA code as the driver reports it.</param>
    /// <param name="text">Text after the ORA prefix.</param>
    /// <param name="operation">Failing gateway operation, or null.</param>
    /// <param name="duringOpen">Whether the failure occurred while opening.</param>
    /// <param name="stackLines">Lines following the first line, innermost first.</param>
    /// <returns>The parsed error.</returns>
    private static OracleErrorInfo Err(int number, string text, string? operation = null, bool duringOpen = false, params string[] stackLines)
    {
        string first = string.Create(CultureInfo.InvariantCulture, $"ORA-{number:D5}: {text}");
        string message = string.Join("\n", new[] { first }.Concat(stackLines));
        return OracleErrorParser.FromParts(number, message, duringOpen, operation);
    }

    /// <summary>Asserts a 500 oracle-error failure with no number or package and the fixed connection-string message.</summary>
    /// <param name="failure">The translated failure.</param>
    /// <param name="connectionString">Connection string the message must not contain, or null.</param>
    private static void AssertConfigurationFault(DataFailure? failure, string? connectionString)
    {
        Assert.NotNull(failure);
        Assert.Equal(500, failure.Status);
        Assert.Equal(DataFailure.OracleErrorType, failure.Type);
        Assert.Null(failure.Number);
        Assert.Null(failure.Package);
        Assert.Null(failure.Kind);
        Assert.Null(failure.OpenItemId);
        Assert.Equal(ConfigurationFaultMessage, failure.Message);
        Assert.DoesNotContain("ORA-", failure.Message, StringComparison.Ordinal);
        if (connectionString is not null)
        {
            Assert.DoesNotContain(connectionString, failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("secret", failure.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>Returns an ORA-06512 line for a quoted name and line number.</summary>
    /// <param name="name">Quoted object name, optionally schema-qualified.</param>
    /// <param name="line">Line number.</param>
    /// <returns>The frame line.</returns>
    private static string Frame(string name, int line) =>
        string.Create(CultureInfo.InvariantCulture, $"ORA-06512: at \"{name}\", line {line}");

    /// <summary>Builds an ODP.NET exception through its internal constructor.</summary>
    /// <param name="number">ORA code as the driver reports it.</param>
    /// <param name="message">Driver error message.</param>
    /// <param name="inner">Inner exception, or null for a plain driver detail.</param>
    /// <returns>The driver exception.</returns>
    private static OracleException Driver(int number, string message, Exception? inner = null)
    {
        Assert.NotNull(OracleExceptionConstructor);
        return (OracleException)OracleExceptionConstructor.Invoke(new object[]
        {
            number,
            "HISDB",
            "BIL_INVOICE_API.CREATE_FULL_INVOICE",
            message,
            inner ?? new InvalidOperationException("driver detail"),
        });
    }
}
