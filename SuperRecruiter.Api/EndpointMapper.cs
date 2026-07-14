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
        MapAdminConfigEndpoints(app);

        return app;
    }

    private static void MapPlayerEndpoints(IEndpointRouteBuilder api)
    {
        api.MapGet(
            "/players",
            async (PlayerDatabaseService db, int? limit, int? offset, int[]? status, string[]? playerClass, double? minItemLevel, int? minMythicKills) =>
            {
                var players = await db.GetPlayersAsync(
                    statuses: status?.Select(s => (PlayerStatus)s).ToList(),
                    playerClasses: playerClass?.ToList(),
                    minItemLevel: minItemLevel,
                    minMythicKills: minMythicKills,
                    limit: limit ?? 50,
                    offset: offset ?? 0
                );
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

        api.MapGet(
            "/players/lookup/{realmSlug}/{characterName}",
            async (PlayerDatabaseService db, string realmSlug, string characterName) =>
            {
                var player = await db.GetPlayerByCharacterAndRealmAsync(characterName, realmSlug);
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

        api.MapPut(
            "/players/{realmSlug}/{characterName}/status",
            async (PlayerDatabaseService db, string realmSlug, string characterName, UpdatePlayerStatusRequest request) =>
            {
                var player = await db.UpdatePlayerStatusByCharacterAndRealmAsync(characterName, realmSlug, request.Status);
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

        api.MapGet(
            "/players/cache",
            async (PlayerDatabaseService db) =>
            {
                var players = await db.GetPlayersCacheAsync();
                return Results.Ok(players);
            }
        );
    }

    private static void MapSeenPlayerEndpoints(IEndpointRouteBuilder api)
    {
        api.MapGet(
            "/players/seen/all",
            async (PlayerDatabaseService db) =>
            {
                var seenPlayers = await db.GetAllSeenPlayersAsync();
                return Results.Ok(seenPlayers);
            }
        );

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
                await db.AddSeenPlayerAsync(request.CharacterName, request.Realm, request.LastUpdated);
                return Results.Ok();
            }
        );

        api.MapPost(
            "/players/seen/bulk",
            async (PlayerDatabaseService db, List<SeenPlayerRequest> requests) =>
            {
                var batch = requests.Select(r => (r.CharacterName, r.Realm, r.LastUpdated)).ToList();
                await db.BulkAddSeenPlayersAsync(batch);
                return Results.Ok();
            }
        );

        api.MapPost(
            "/players/seen/cleanup",
            async (PlayerDatabaseService db, int? daysToKeep) =>
            {
                await db.CleanupOldSeenPlayersAsync(daysToKeep ?? 30);
                return Results.Ok();
            }
        );
    }

    private static void MapAdminConfigEndpoints(IEndpointRouteBuilder api)
    {
        api.MapGet(
            "/config",
            async (PlayerDatabaseService db) =>
            {
                var config = await db.GetAdminConfigAsync();
                return Results.Ok(config);
            }
        );

        api.MapPut(
            "/config",
            async (PlayerDatabaseService db, UpdateAdminConfigRequest request) =>
            {
                var config = await db.UpdateAdminConfigAsync(request);
                return Results.Ok(config);
            }
        );
    }
}
