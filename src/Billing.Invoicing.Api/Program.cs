using System.Text.Json.Serialization;
using Billing.Invoicing.Api.Contracts;
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
                Field = FieldOf(entry.Key),
                Text = string.IsNullOrEmpty(error.ErrorMessage) ? "The input was not valid." : error.ErrorMessage,
                Severity = ValidationMessage.Blocking,
            }))
            .ToArray(),
        openItems = Array.Empty<string>(),
    })
    {
        ContentTypes = { "application/problem+json" },
    });

var webOrigin = builder.Configuration["Cors:WebOrigin"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (!string.IsNullOrWhiteSpace(webOrigin))
    {
        p.WithOrigins(webOrigin).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("Location");
    }
}));

var app = builder.Build();

app.UseCors();
app.MapControllers();

app.Run();

static string? FieldOf(string key)
{
    var item = key[(key.LastIndexOf('.') + 1)..];
    return item.Length > 0 && item.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_') ? item : null;
}
