using Scalar.AspNetCore;
using SuperRecruiter.Api.Services;
using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Shared.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton<PlayerDatabaseService>();

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

// Serve React static files in production
app.UseDefaultFiles();
app.UseStaticFiles();

// --- Player endpoints ---

app.MapGet(
    "/api/players",
    async (PlayerDatabaseService db, PlayerStatus? status, int? limit, int? offset) =>
    {
        var players = await db.GetPlayersAsync(status, limit ?? 50, offset ?? 0);
        return Results.Ok(players);
    }
);

app.MapGet(
    "/api/players/{id:int}",
    async (PlayerDatabaseService db, int id) =>
    {
        var player = await db.GetPlayerByIdAsync(id);
        return player is not null ? Results.Ok(player) : Results.NotFound();
    }
);

app.MapPost(
    "/api/players",
    async (PlayerDatabaseService db, CreatePlayerRequest request) =>
    {
        var player = await db.UpsertPlayerAsync(request);
        return Results.Created($"/api/players/{player.Id}", player);
    }
);

app.MapPut(
    "/api/players/{id:int}/status",
    async (PlayerDatabaseService db, int id, UpdatePlayerStatusRequest request) =>
    {
        var player = await db.UpdatePlayerStatusAsync(id, request.Status);
        return player is not null ? Results.Ok(player) : Results.NotFound();
    }
);

// --- Seen players endpoints (used by worker) ---

app.MapGet(
    "/api/players/seen",
    async (PlayerDatabaseService db, string name, string realm) =>
    {
        var lastSeenAt = await db.GetLastSeenAtAsync(name, realm);
        return Results.Ok(new { lastSeenAt });
    }
);

app.MapPost(
    "/api/players/seen",
    async (PlayerDatabaseService db, SeenPlayerRequest request) =>
    {
        await db.AddSeenPlayerAsync(request.CharacterName, request.Realm, request.LastUpdated);
        return Results.Ok();
    }
);

// --- Blacklist endpoints ---

app.MapGet(
    "/api/blacklist",
    async (PlayerDatabaseService db) =>
    {
        var blacklisted = await db.GetBlacklistedPlayersAsync();
        return Results.Ok(blacklisted);
    }
);

app.MapGet(
    "/api/blacklist/check",
    async (PlayerDatabaseService db, string name, string realm) =>
    {
        var isBlacklisted = await db.IsPlayerBlacklistedAsync(name, realm);
        return Results.Ok(new { isBlacklisted });
    }
);

app.MapPost(
    "/api/blacklist",
    async (PlayerDatabaseService db, BlacklistRequest request) =>
    {
        await db.AddBlacklistedPlayerAsync(request.CharacterName, request.Realm, request.Reason);
        return Results.Created();
    }
);

app.MapDelete(
    "/api/blacklist/{id:int}",
    async (PlayerDatabaseService db, int id) =>
    {
        await db.RemoveBlacklistedPlayerAsync(id);
        return Results.NoContent();
    }
);

// SPA fallback — serve index.html for non-API routes
app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();

app.Run();
