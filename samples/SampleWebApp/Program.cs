using Codekali.Net.Config.UI.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ── Register Codekali Config UI services ──────────────────────────────────
// Option A: default (Development only, no token, /config-ui)
builder.Services.AddConfigUI();

// Option B: custom settings — uncomment to use instead
// builder.Services.AddConfigUI(options =>
// {
//     options.PathPrefix          = "/config-ui";
//     options.AccessToken         = "super-secret-dev-token";
//     options.AllowedEnvironments = ["Development", "Staging"];
//     options.MaskSensitiveValues = true;
//     options.ReadOnly            = false;
// });

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

app.MapGet("/", () => Results.Ok(new
{
    message = "SampleWebApp is running",
    configUI = "/config-ui"
}));

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
