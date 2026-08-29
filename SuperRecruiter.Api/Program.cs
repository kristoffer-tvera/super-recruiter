using Scalar.AspNetCore;
using SuperRecruiter.Api;
using SuperRecruiter.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton<PlayerDatabaseService>();
builder.Services.AddHttpClient<GeminiService>();

// Configure CORS for the React frontend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

var configuredApiKey = builder.Configuration["ApiKey"];

// Initialize database
var dbService = app.Services.GetRequiredService<PlayerDatabaseService>();
await dbService.InitializeDatabaseAsync();

app.UseCors();

app.Use(
    async (context, next) =>
    {
        var path = context.Request.Path;

        // Everything is key-protected except the docs, so new endpoints are secured by default.
        var isPublicPath = path == "/" || path.StartsWithSegments("/scalar") || path.StartsWithSegments("/openapi");
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

app.MapOpenApi();
app.MapScalarApiReference();

// Map all API endpoints
app.MapEndpoints();

// SPA fallback — serve index.html for non-API routes
app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();

app.Run();
