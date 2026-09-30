// Serves the React build in wwwroot; unmatched non-file routes get index.html, missing files stay 404.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");
app.Run();
