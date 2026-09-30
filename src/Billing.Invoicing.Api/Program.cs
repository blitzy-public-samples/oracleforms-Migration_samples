using System.Text.Json.Serialization;
using Billing.Invoicing.Api.Composition;
using Billing.Invoicing.Api.Context;
using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Api.Errors;
using Billing.Invoicing.Api.Services;
using Billing.Invoicing.Domain.Model;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.AllowInputFormatterExceptionMessages = false;
    })
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = context => new UnprocessableEntityObjectResult(new
    {
        type = "field-validation",
        title = "Validation failed",
        status = StatusCodes.Status422UnprocessableEntity,
        messages = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .SelectMany(entry => entry.Value!.Errors.Select(error => new MessageDto
            {
                Field = ModelStateFieldMap.FieldOf(entry.Key),
                Text = string.IsNullOrEmpty(error.ErrorMessage) ? "The input was not valid." : error.ErrorMessage,
                Severity = ValidationMessage.Blocking,
            }))
            .ToArray(),
        openItems = Array.Empty<string>(),
    })
    {
        ContentTypes = { "application/problem+json" },
    });

builder.Services.AddInvoicingData(builder.Configuration);
builder.Services.AddSingleton<ProblemDetailsWriter>();
builder.Services.AddScoped<InvoiceWorkflowService>();

var webOrigin = RequireWebOrigin(builder.Configuration["Cors:WebOrigin"]);
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(webOrigin).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("Location")));

var app = builder.Build();

app.UseExceptionHandler(handler => handler.Run(context =>
    context.RequestServices.GetRequiredService<ProblemDetailsWriter>().WriteAsync(context)));
app.UseCors();
app.UseMiddleware<OperatorContextMiddleware>();
app.MapControllers();

app.Run();

// Returns the Cors:WebOrigin value as a scheme://host:port origin; throws InvalidOperationException naming the key when it is
// missing, blank, or not an absolute http or https URI without user info, path, query or fragment.
static string RequireWebOrigin(string? configured)
{
    var value = configured?.Trim();
    if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.UserInfo.Length == 0
        && uri.AbsolutePath == "/"
        && uri.Query.Length == 0
        && uri.Fragment.Length == 0)
    {
        return uri.GetLeftPart(UriPartial.Authority);
    }

    throw new InvalidOperationException(
        "Cors:WebOrigin must be the absolute http or https origin of the Web host, such as http://localhost:5090.");
}

// Declares the top-level entry-point class internal (D-81).
internal partial class Program;
