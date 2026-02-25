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

// Initialize database
var dbService = app.Services.GetRequiredService<PlayerDatabaseService>();
await dbService.InitializeDatabaseAsync();

app.UseCors();

app.MapOpenApi();
app.MapScalarApiReference();

// Map all API endpoints
app.MapEndpoints();

// SPA fallback — serve index.html for non-API routes
app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();

app.Run();
