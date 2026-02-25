using SuperRecruiter.Api.Services;
using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Api;

public static class EndpointMapper
{
    public static WebApplication MapEndpoints(this WebApplication app)
    {
        MapPlayerEndpoints(app);
        MapSeenPlayerEndpoints(app);
        MapBlacklistEndpoints(app);

        return app;
    }

    private static void MapPlayerEndpoints(IEndpointRouteBuilder api)
    {
        api.MapGet(
            "/players",
            async (PlayerDatabaseService db, PlayerStatus? status, int? limit, int? offset) =>
            {
                var players = await db.GetPlayersAsync(status, limit ?? 50, offset ?? 0);
                return Results.Ok(players);
            }
        );

        api.MapGet(
            "/players/{id:int}",
            async (PlayerDatabaseService db, int id) =>
            {
                var player = await db.GetPlayerByIdAsync(id);
                return player is not null ? Results.Ok(player) : Results.NotFound();
            }
        );

        api.MapPost(
            "/players",
            async (PlayerDatabaseService db, CreatePlayerRequest request) =>
            {
                var player = await db.UpsertPlayerAsync(request);
                return Results.Created($"/players/{player.Id}", player);
            }
        );

        api.MapPut(
            "/players/{id:int}/status",
            async (PlayerDatabaseService db, int id, UpdatePlayerStatusRequest request) =>
            {
                var player = await db.UpdatePlayerStatusAsync(id, request.Status);
                return player is not null ? Results.Ok(player) : Results.NotFound();
            }
        );

        api.MapPost(
            "/players/{id:int}/ai-summary",
            async (PlayerDatabaseService db, GeminiService gemini, int id) =>
            {
                var player = await db.GetPlayerByIdAsync(id);
                if (player is null)
                    return Results.NotFound();

                var take = await gemini.GetGeminiTakeForPlayer(player);
                if (string.IsNullOrEmpty(take))
                    return Results.Problem("Failed to generate AI summary");

                var updated = await db.UpdateGeminiTakeAsync(id, take);
                return updated is not null ? Results.Ok(updated) : Results.NotFound();
            }
        );
    }

    private static void MapSeenPlayerEndpoints(IEndpointRouteBuilder api)
    {
        api.MapGet(
            "/players/seen",
            async (PlayerDatabaseService db, string name, string realm) =>
            {
                var lastSeenAt = await db.GetLastSeenAtAsync(name, realm);
                return Results.Ok(new { lastSeenAt });
            }
        );

        api.MapPost(
            "/players/seen",
            async (PlayerDatabaseService db, SeenPlayerRequest request) =>
            {
                await db.AddSeenPlayerAsync(
                    request.CharacterName,
                    request.Realm,
                    request.LastUpdated
                );
                return Results.Ok();
            }
        );
    }

    private static void MapBlacklistEndpoints(IEndpointRouteBuilder api)
    {
        api.MapGet(
            "/blacklist",
            async (PlayerDatabaseService db) =>
            {
                var blacklisted = await db.GetBlacklistedPlayersAsync();
                return Results.Ok(blacklisted);
            }
        );

        api.MapGet(
            "/blacklist/check",
            async (PlayerDatabaseService db, string name, string realm) =>
            {
                var isBlacklisted = await db.IsPlayerBlacklistedAsync(name, realm);
                return Results.Ok(new { isBlacklisted });
            }
        );

        api.MapPost(
            "/blacklist",
            async (PlayerDatabaseService db, BlacklistRequest request) =>
            {
                await db.AddBlacklistedPlayerAsync(
                    request.CharacterName,
                    request.Realm,
                    request.Reason
                );
                return Results.Created();
            }
        );

        api.MapDelete(
            "/blacklist/{id:int}",
            async (PlayerDatabaseService db, int id) =>
            {
                await db.RemoveBlacklistedPlayerAsync(id);
                return Results.NoContent();
            }
        );
    }
}
