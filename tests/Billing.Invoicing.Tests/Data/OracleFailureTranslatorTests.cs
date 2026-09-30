using System.Globalization;
using System.Net.Sockets;
using System.Reflection;
using Billing.Invoicing.Data.Errors;
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

    private const string RequestUnavailableText =
        "One or more requested services were already invoiced or are no longer available.";

    private const string PatientRequiredText = "Invoice create failed: patient number is required.";
    private const string BundledOfferText = "Bundled Offers are available only for Cash invoices.";
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

    [Theory]
    [InlineData(20000, -20000)]
    [InlineData(20999, -20999)]
    public void Translate_ApplicationRangeBoundaries_Are422(int number, int expected)
    {
        DataFailure failure = translator.Translate(Err(number, "x"));

        Assert.Equal(422, failure.Status);
        Assert.Equal(expected, failure.Number);
        Assert.Equal(UnknownPackage, failure.Package);
        Assert.Null(failure.Kind);
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
    [InlineData(20778, "Request import failed: application ID is required.")]
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
    [InlineData("Request import failed: selected request line 42 is no longer available.")]
    [InlineData("Request package expansion failed: multiplied quantity exceeds the supported two-decimal quantity precision for component 7.")]
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
        Assert.Equal(UnknownPackage, failure.Package);
        Assert.Null(failure.Field);
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

    [Fact]
    public void Translate_NotImplementedWithoutOpenItem_Is501WithoutOpenItemId()
    {
        DataFailure? failure = translator.Translate(new NotImplementedException("blocked"));

        Assert.NotNull(failure);
        Assert.Equal(501, failure.Status);
        Assert.Null(failure.OpenItemId);
    }

    [Fact]
    public void Translate_UnrelatedException_ReturnsNull()
    {
        Assert.Null(translator.Translate(new InvalidOperationException("unrelated")));
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
