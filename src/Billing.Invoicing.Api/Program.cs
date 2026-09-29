using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

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
