using System.Globalization;
using Billing.Invoicing.Api.Errors;
using Billing.Invoicing.Domain.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Billing.Invoicing.Api.Context;

/// <summary>Builds the <see cref="OperatorContext"/> of each <c>/api</c> request from its <c>X-His-*</c> headers, answering 422 when any is missing.</summary>
public sealed class OperatorContextMiddleware
{
    /// <summary><see cref="HttpContext.Items"/> key holding the request's <see cref="OperatorContext"/>.</summary>
    public const string ItemKey = "Billing.Invoicing.Api.OperatorContext";

    private const string ApiPathPrefix = "/api";

    private const string UserNoHeader = "X-His-User-No";
    private const string UserNameHeader = "X-His-User-Name";
    private const string InfoCenterIdHeader = "X-His-Info-Center-Id";
    private const string MachineHeader = "X-His-Machine";
    private const string SessionIdHeader = "X-His-Session-Id";

    private const int HeaderCount = 5;

    private readonly RequestDelegate _next;

    /// <summary>Creates the middleware ahead of the given pipeline step.</summary>
    /// <param name="next">The next step of the request pipeline.</param>
    public OperatorContextMiddleware(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);
        _next = next;
    }

    /// <summary>Stores the operator context of an <c>/api</c> request and continues, or writes the 422 naming the missing headers; other paths pass through unchecked.</summary>
    /// <param name="context">The current request.</param>
    /// <returns>A task that completes when the request has been handled.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.Path.StartsWithSegments(ApiPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var missing = new List<string>(HeaderCount);

        string userNoText = ReadHeader(context.Request, UserNoHeader, missing);
        var userNo = 0;
        if (userNoText.Length > 0
            && !int.TryParse(userNoText, NumberStyles.Integer, CultureInfo.InvariantCulture, out userNo))
        {
            missing.Add(UserNoHeader);
        }

        string userName = ReadHeader(context.Request, UserNameHeader, missing);
        string infoCenterId = ReadHeader(context.Request, InfoCenterIdHeader, missing);
        string machineName = ReadHeader(context.Request, MachineHeader, missing);
        string sessionId = ReadHeader(context.Request, SessionIdHeader, missing);

        if (missing.Count > 0)
        {
            ProblemDetailsWriter writer = context.RequestServices.GetRequiredService<ProblemDetailsWriter>();
            await writer.WriteAsync(context, missing);
            return;
        }

        context.Items[ItemKey] = new OperatorContext
        {
            UserNo = userNo,
            UserName = userName,
            InfoCenterId = infoCenterId,
            MachineName = machineName,
            SessionId = sessionId,
        };

        await _next(context);
    }

    /// <summary>Returns the trimmed header value, adding the header name to <paramref name="missing"/> when the value is blank.</summary>
    /// <param name="request">The current request.</param>
    /// <param name="name">Header name.</param>
    /// <param name="missing">Names of the headers found missing so far.</param>
    /// <returns>The trimmed value; empty when the header is absent or blank.</returns>
    private static string ReadHeader(HttpRequest request, string name, List<string> missing)
    {
        string value = request.Headers[name].ToString().Trim();
        if (value.Length == 0)
        {
            missing.Add(name);
        }

        return value;
    }
}
