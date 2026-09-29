using Billing.Invoicing.Data.Errors;

namespace Billing.Invoicing.Tests.Data;

/// <summary>Number normalisation, text and frame parsing tests for <see cref="OracleErrorParser.FromParts"/>.</summary>
[Trait("Category", "DataUnit")]
public sealed class OracleErrorParserTests
{
    private const string RequestUnavailableText =
        "One or more requested services were already invoiced or are no longer available.";

    private const string EngineFrameLine = "ORA-06512: at \"HIS.BIL_INVOICE_ENGINE\", line 619";
    private const string ApiFrameLine = "ORA-06512: at \"HIS.BIL_INVOICE_API\", line 1476";

    private const string RequestUnavailableMessage =
        "ORA-20931: " + RequestUnavailableText + "\n" + EngineFrameLine;

    private const string RequestUnavailableMessageCrLf =
        "ORA-20931: " + RequestUnavailableText + "\r\n" + EngineFrameLine;

    private const string PatientRequiredMessage =
        "ORA-20900: Invoice create failed: patient number is required.";

    [Fact]
    public void FromParts_ApplicationError_NormalisesNumberAndParsesTextAndFrame()
    {
        OracleErrorInfo info = OracleErrorParser.FromParts(20931, RequestUnavailableMessage);

        Assert.Equal(-20931, info.Number);
        Assert.Equal(RequestUnavailableText, info.Text);
        Assert.Equal(RequestUnavailableMessage, info.Message);
        Assert.Contains(("HIS", "BIL_INVOICE_ENGINE", 619), info.Frames);
    }

    [Fact]
    public void FromParts_MultipleFrames_ReturnsInnermostFirst()
    {
        string message = "ORA-20931: " + RequestUnavailableText + "\n" + EngineFrameLine + "\n" + ApiFrameLine;

        OracleErrorInfo info = OracleErrorParser.FromParts(20931, message);

        Assert.Equal(2, info.Frames.Count);
        Assert.Equal(("HIS", "BIL_INVOICE_ENGINE", 619), info.Frames[0]);
        Assert.Equal(("HIS", "BIL_INVOICE_API", 1476), info.Frames[1]);
    }

    [Fact]
    public void FromParts_NumberDisagreesWithMessagePrefix_Throws()
    {
        Assert.Throws<ArgumentException>(() => OracleErrorParser.FromParts(20930, RequestUnavailableMessage));
    }

    [Theory]
    [InlineData(12541, "ORA-12541: TNS:no listener", "TNS:no listener")]
    [InlineData(1017, "ORA-01017: invalid username/password; logon denied", "invalid username/password; logon denied")]
    [InlineData(3113, "ORA-03113: end-of-file on communication channel", "end-of-file on communication channel")]
    public void FromParts_NonApplicationCode_StaysPositive(int number, string message, string expectedText)
    {
        OracleErrorInfo info = OracleErrorParser.FromParts(number, message);

        Assert.Equal(number, info.Number);
        Assert.Equal(expectedText, info.Text);
        Assert.Empty(info.Frames);
    }

    [Theory]
    [InlineData(20000, "ORA-20000: x", -20000)]
    [InlineData(20999, "ORA-20999: x", -20999)]
    public void FromParts_ApplicationRangeBoundaries_AreSigned(int number, string message, int expected)
    {
        OracleErrorInfo info = OracleErrorParser.FromParts(number, message);

        Assert.Equal(expected, info.Number);
        Assert.Equal("x", info.Text);
    }

    [Fact]
    public void FromParts_DuringOpenAndOperation_ArePassedThrough()
    {
        OracleErrorInfo info = OracleErrorParser.FromParts(
            20900,
            PatientRequiredMessage,
            duringOpen: true,
            operation: "CreateFullInvoice");

        Assert.True(info.DuringOpen);
        Assert.Equal("CreateFullInvoice", info.Operation);
    }

    [Fact]
    public void FromParts_DuringOpenAndOperation_DefaultToFalseAndNull()
    {
        OracleErrorInfo info = OracleErrorParser.FromParts(20900, PatientRequiredMessage);

        Assert.False(info.DuringOpen);
        Assert.Null(info.Operation);
        Assert.Equal(-20900, info.Number);
        Assert.Equal("Invoice create failed: patient number is required.", info.Text);
    }

    [Fact]
    public void FromParts_WindowsLineEndings_ParseLikeUnixLineEndings()
    {
        OracleErrorInfo unix = OracleErrorParser.FromParts(20931, RequestUnavailableMessage);
        OracleErrorInfo windows = OracleErrorParser.FromParts(20931, RequestUnavailableMessageCrLf);

        Assert.Equal(unix.Number, windows.Number);
        Assert.Equal(unix.Text, windows.Text);
        Assert.Equal(RequestUnavailableText, windows.Text);
        Assert.Equal(unix.Frames, windows.Frames);
        Assert.Equal(RequestUnavailableMessageCrLf, windows.Message);
    }
}
