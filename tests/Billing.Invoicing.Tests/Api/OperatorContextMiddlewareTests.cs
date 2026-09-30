using System.Text.Json;
using Billing.Invoicing.Api.Context;
using Billing.Invoicing.Api.Errors;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Domain.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Billing.Invoicing.Tests.Api;

/// <summary><c>X-His-Info-Center-Id</c> checks of <see cref="OperatorContextMiddleware"/> on an <c>/api</c> request.</summary>
[Trait("Category", "Orchestration")]
public sealed class OperatorContextMiddlewareTests
{
    private const string UserNoHeader = "X-His-User-No";
    private const string UserNameHeader = "X-His-User-Name";
    private const string InfoCenterIdHeader = "X-His-Info-Center-Id";
    private const string MachineHeader = "X-His-Machine";
    private const string SessionIdHeader = "X-His-Session-Id";

    private const string ProblemJsonContentType = "application/problem+json";
    private const string OperatorContextMissingType = "operator-context-missing";

    [Theory]
    [InlineData("AB12345678", "AB12345678")]
    [InlineData("1234567890", "1234567890")]
    [InlineData("  AB12345678  ", "AB12345678")]
    [InlineData("C1", "C1")]
    public async Task InfoCenterId_UpToTenCharacters_IsStoredTrimmedAndContinues(string header, string expected)
    {
        Dictionary<string, string> headers = ValidHeaders();
        headers[InfoCenterIdHeader] = header;

        (DefaultHttpContext context, int nextCalls) = await InvokeAsync(headers);

        Assert.Equal(1, nextCalls);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
        OperatorContext operatorContext = Assert.IsType<OperatorContext>(context.Items[OperatorContextMiddleware.ItemKey]);
        Assert.Equal(expected, operatorContext.InfoCenterId);
    }

    [Theory]
    [InlineData("AB123456789")]
    [InlineData("12345678901")]
    [InlineData("  AB123456789  ")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ")]
    public async Task InfoCenterId_LongerThanTenCharacters_Answers422NamingTheHeader(string header)
    {
        Dictionary<string, string> headers = ValidHeaders();
        headers[InfoCenterIdHeader] = header;

        (DefaultHttpContext context, int nextCalls) = await InvokeAsync(headers);

        Assert.Equal(0, nextCalls);
        Assert.False(context.Items.ContainsKey(OperatorContextMiddleware.ItemKey));
        AssertOperatorContextMissing(context, InfoCenterIdHeader);
    }

    [Fact]
    public async Task InfoCenterId_TooLongWithBlankUserName_NamesBothHeadersInReadOrder()
    {
        Dictionary<string, string> headers = ValidHeaders();
        headers[UserNameHeader] = "   ";
        headers[InfoCenterIdHeader] = "AB123456789";

        (DefaultHttpContext context, int nextCalls) = await InvokeAsync(headers);

        Assert.Equal(0, nextCalls);
        AssertOperatorContextMissing(context, UserNameHeader, InfoCenterIdHeader);
    }

    /// <summary>Returns a complete, valid set of operator-context headers.</summary>
    private static Dictionary<string, string> ValidHeaders() => new(StringComparer.OrdinalIgnoreCase)
    {
        [UserNoHeader] = "1",
        [UserNameHeader] = "dev",
        [InfoCenterIdHeader] = "AB12345678",
        [MachineHeader] = "clone24",
        [SessionIdHeader] = "0123456789abcdef0123456789abcdef",
    };

    /// <summary>Runs the middleware on <c>/api/x</c> with the given headers.</summary>
    /// <param name="headers">Request headers.</param>
    /// <returns>The handled context and the number of times the next step ran.</returns>
    private static async Task<(DefaultHttpContext Context, int NextCalls)> InvokeAsync(IReadOnlyDictionary<string, string> headers)
    {
        await using ServiceProvider services = new ServiceCollection()
            .AddSingleton(new ProblemDetailsWriter(new OracleFailureTranslator()))
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = "/api/x";
        foreach (KeyValuePair<string, string> header in headers)
        {
            context.Request.Headers[header.Key] = header.Value;
        }

        context.Response.Body = new MemoryStream();

        var nextCalls = 0;
        var middleware = new OperatorContextMiddleware(_ =>
        {
            nextCalls++;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
        return (context, nextCalls);
    }

    /// <summary>Asserts a 422 <c>operator-context-missing</c> body whose <c>missing</c> lists exactly the given headers.</summary>
    /// <param name="context">The handled context.</param>
    /// <param name="expectedMissing">Header names expected in <c>missing</c>, in order.</param>
    private static void AssertOperatorContextMissing(DefaultHttpContext context, params string[] expectedMissing)
    {
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, context.Response.StatusCode);
        Assert.Equal(ProblemJsonContentType, context.Response.ContentType);

        context.Response.Body.Position = 0;
        using JsonDocument body = JsonDocument.Parse(context.Response.Body);
        JsonElement root = body.RootElement;

        Assert.Equal(OperatorContextMissingType, root.GetProperty("type").GetString());
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, root.GetProperty("status").GetInt32());
        Assert.Equal(
            expectedMissing,
            root.GetProperty("missing").EnumerateArray().Select(item => item.GetString()).ToArray());
    }
}
