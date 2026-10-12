using Codekali.Net.Config.UI.Extensions;
using SampleWebApp;

var builder = WebApplication.CreateBuilder(args);

// ── Register Codekali Config UI services ──────────────────────────────────
// Option A: default (Development only, no token, /config-ui)
builder.Services.AddConfigUI(options =>
{
    options.PathPrefix = "/configuration";
    options.BackupVersionPrefix = "v.1";
    options.MaskSensitiveValues = true;
    options.EnableAuditLogging = true;
    options.ConfigurationTestTypes = [typeof(CustomConfigurationTests)];
});

// Option B: custom settings — uncomment to use instead
/*builder.Services.AddConfigUI(options =>
{
    options.PathPrefix = "/config-ui"; // Configurable to any path prefix you like (e.g. "/admin/config", "/settings", etc.)
    options.AccessToken = "super-secret-dev-token";
    options.AllowedEnvironments = ["Development", "Staging"];
    options.MaskSensitiveValues = true;
    options.ReadOnly = false;
});*/

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ── Activate the middleware ───────────────────────────────────────────────
// Must be called AFTER builder.Services.AddConfigUI() above.
app.UseConfigUI();

app.MapGet("/", () => Results.Redirect("/configuration"));

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/test/{key}", (string key) => 
    Results.Ok(new { status = "Test Results", key, value = builder.Configuration.GetValue<string>(key) }
    ));

app.Run();
