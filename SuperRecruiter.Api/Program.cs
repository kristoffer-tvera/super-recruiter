using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;
using SuperRecruiter.Api;
using SuperRecruiter.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("172.30.50.0"), 24));
});

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<WowProgressJobQueue>();
builder.Services.AddSingleton<PlayerDatabaseService>();
builder.Services.AddHttpClient<GeminiService>();
builder.Services.AddHttpClient("enrichment", client => client.Timeout = TimeSpan.FromSeconds(70)).RedactLoggedHeaders(["Authorization"]).RemoveAllLoggers();
builder.Services.AddSingleton<PlayerSourceClient>();
builder.Services.AddSingleton<PlayerEnrichmentService>();

var app = builder.Build();

var configuredApiKey = builder.Configuration["ApiKey"];
if (string.IsNullOrWhiteSpace(configuredApiKey))
    throw new InvalidOperationException("ApiKey is not configured");

// Initialize database
var dbService = app.Services.GetRequiredService<PlayerDatabaseService>();
await dbService.InitializeDatabaseAsync();

app.UseForwardedHeaders();
app.UseExceptionHandler();

app.Use(
    async (context, next) =>
    {
        if (context.GetEndpoint() is null)
        {
            await next();
            return;
        }

        var path = context.Request.Path;

        // Everything except health and API documentation is key-protected by default.
        var isPublicPath = path == "/" || path == "/api/health" || path == "/api/live" || path.StartsWithSegments("/api/scalar") || path.StartsWithSegments("/api/openapi");
        if (isPublicPath)
        {
            await next();
            return;
        }

        if (string.IsNullOrWhiteSpace(configuredApiKey))
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "API key is not configured on server" });
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var providedApiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing X-Api-Key header" });
            return;
        }

        if (!string.Equals(providedApiKey, configuredApiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid API key" });
            return;
        }

        await next();
    }
);

app.MapOpenApi("/api/openapi/{documentName}.json");
app.MapScalarApiReference("/api/scalar", options => options.WithOpenApiRoutePattern("/api/openapi/{documentName}.json"));

app.MapGet("/api/live", () => Results.Ok(new { status = "alive" })).ExcludeFromDescription();

app.MapGet(
        "/api/health",
        async (PlayerDatabaseService db) => await db.IsReadyAsync() ? Results.Ok(new { status = "ready" }) : Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
    )
    .ExcludeFromDescription();

app.MapGroup("/api").MapEndpoints();

// SPA fallback — serve index.html for non-API routes
app.MapGet("/", () => Results.Redirect("/api/scalar")).ExcludeFromDescription();

app.Run();

public partial class Program { }
