using SuperRecruiter.Api.Services;
using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Api;

public static class EndpointMapper
{
    public static RouteGroupBuilder MapEndpoints(this RouteGroupBuilder api)
    {
        MapPlayerEndpoints(api);
        MapWowProgressEndpoints(api);
        MapSeenPlayerEndpoints(api);
        MapAdminConfigEndpoints(api);
        MapChatEndpoints(api);

        return api;
    }

    private static void MapPlayerEndpoints(IEndpointRouteBuilder api)
    {
        api.MapGet(
                "/players/{id:int}/enrichment",
                async (PlayerDatabaseService db, WowProgressJobQueue jobs, int id, bool? includeData, CancellationToken cancellationToken) =>
                {
                    if (await db.GetPlayerByIdAsync(id, cancellationToken) is null)
                        return Results.NotFound();
                    return Results.Ok(jobs.WithPending(await db.GetEnrichmentAsync(id, includeData ?? true, cancellationToken), id));
                }
            )
            .WithName("GetPlayerEnrichment")
            .WithSummary("Read stored source snapshots without contacting upstream services")
            .Produces<PlayerEnrichmentResponse>()
            .Produces(StatusCodes.Status404NotFound);

        api.MapPost(
                "/players/{id:int}/enrichment",
                async (PlayerDatabaseService db, PlayerEnrichmentService enrichment, HttpContext context, int id, CancellationToken cancellationToken) =>
                {
                    var player = await db.GetPlayerByIdAsync(id, cancellationToken);
                    if (player is null)
                        return Results.NotFound();
                    try
                    {
                        return Results.Ok(await enrichment.RefreshAsync(player, cancellationToken));
                    }
                    catch (EnrichmentBusyException exception)
                    {
                        if (exception.Cooldown)
                            context.Response.Headers.RetryAfter = "60";
                        return Results.Problem(
                            exception.Cooldown ? "Refresh cooldown or server capacity reached. Retry in 60 seconds." : "This player is already being refreshed.",
                            statusCode: exception.Cooldown ? StatusCodes.Status429TooManyRequests : StatusCodes.Status409Conflict
                        );
                    }
                }
            )
            .WithName("RefreshPlayerEnrichment")
            .WithSummary("Refresh Raider.IO and WarcraftLogs and queue local WoWProgress enrichment")
            .Produces<PlayerEnrichmentRefreshResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

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
                return Results.Created($"/api/players/{player.Id}", player);
            }
        );

        api.MapPut(
            "/players/{id:int}/status",
            async (PlayerDatabaseService db, int id, UpdatePlayerStatusRequest request) =>
            {
                var player = await db.UpdatePlayerStatusAsync(id, request.Status);
                return player is not null ? Results.Ok(await db.GetPlayerByIdAsync(id)) : Results.NotFound();
            }
        );

        api.MapPut(
            "/players/{realmSlug}/{characterName}/status",
            async (PlayerDatabaseService db, string realmSlug, string characterName, UpdatePlayerStatusRequest request) =>
            {
                var player = await db.UpdatePlayerStatusByCharacterAndRealmAsync(characterName, realmSlug, request.Status);
                return player is not null ? Results.Ok(await db.GetPlayerByIdAsync(player.Id)) : Results.NotFound();
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
                return updated is not null ? Results.Ok(await db.GetPlayerByIdAsync(id)) : Results.NotFound();
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

    private static void MapWowProgressEndpoints(IEndpointRouteBuilder api)
    {
        api.MapGet("/wowprogress/jobs/pending/{playerId:int}", (int playerId, WowProgressJobQueue jobs) => Results.Ok(new WowProgressPendingResponse(jobs.IsPending(playerId))))
            .WithName("GetWowProgressPendingStatus")
            .WithSummary("Read queued enrichment status from memory without querying PostgreSQL")
            .Produces<WowProgressPendingResponse>();

        api.MapPost("/wowprogress/jobs/claim", (WowProgressJobQueue jobs) => jobs.Claim() is { } job ? Results.Ok(job) : Results.NoContent())
            .WithName("ClaimWowProgressJob")
            .WithSummary("Lease one pending WoWProgress job from memory without querying PostgreSQL")
            .Produces<WowProgressJobResponse>()
            .Produces(StatusCodes.Status204NoContent);

        api.MapPost(
                "/wowprogress/jobs/{id:guid}/complete",
                async (Guid id, CompleteWowProgressJobRequest request, WowProgressJobQueue jobs, PlayerDatabaseService db, CancellationToken cancellationToken) =>
                {
                    string[] outcomes =
                    [
                        "success",
                        "timeout",
                        "unavailable",
                        "upstream_error",
                        "not_found",
                        "not_found_or_private",
                        "rate_limited",
                        "not_configured",
                        "invalid_response",
                        "response_too_large",
                    ];
                    if (
                        !outcomes.Contains(request.Outcome)
                        || request.LeaseId == Guid.Empty
                        || (request.Outcome == "success" ? request.Details?.GuildHistory is null : request.Details is not null)
                    )
                        return Results.ValidationProblem(
                            new Dictionary<string, string[]> { ["result"] = ["Supply a valid lease, outcome, and details only for successful results."] }
                        );
                    return await jobs.CompleteAsync(id, request.LeaseId, job => db.SaveWowProgressAsync(job.PlayerId, request, cancellationToken))
                        ? Results.NoContent()
                        : Results.Problem("The job lease has expired or is no longer available.", statusCode: StatusCodes.Status409Conflict);
                }
            )
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(1024 * 1024))
            .WithName("CompleteWowProgressJob")
            .WithSummary("Store a local collector result without modifying other source snapshots")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapPut(
                "/wowprogress/discoveries",
                (List<Player> players, WowProgressJobQueue jobs) =>
                {
                    if (players.Count > 500 || players.Any(player => player is null || string.IsNullOrWhiteSpace(player.CharacterName) || string.IsNullOrWhiteSpace(player.Realm)))
                        return Results.ValidationProblem(new Dictionary<string, string[]> { ["players"] = ["Supply at most 500 players with character names and realms."] });
                    foreach (var player in players)
                        player.Source = LfgSource.WoWProgress;
                    jobs.PublishDiscoveries(players);
                    return Results.NoContent();
                }
            )
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(1024 * 1024))
            .WithName("PublishWowProgressDiscoveries")
            .WithSummary("Cache the local WoWProgress listing for the server worker; no database access")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        api.MapGet("/wowprogress/discoveries", (WowProgressJobQueue jobs) => Results.Ok(jobs.GetDiscoveries()))
            .WithName("GetWowProgressDiscoveries")
            .WithSummary("Read the cached WoWProgress listing without database access")
            .Produces<Player[]>();
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

    private static void MapChatEndpoints(IEndpointRouteBuilder api)
    {
        const int maxMessageLength = 1000;

        api.MapPost(
            "/chat",
            async (GeminiService gemini, ChatMessageRequest request) =>
            {
                if (string.IsNullOrWhiteSpace(request.Message))
                    return Results.BadRequest(new { error = "Message is required" });

                var message = request.Message.Length > maxMessageLength ? request.Message[..maxMessageLength] : request.Message;

                var reply = await gemini.GetChatReplyAsync(message, request.UserName);

                return string.IsNullOrWhiteSpace(reply)
                    ? Results.Problem("Failed to generate a chat reply", statusCode: StatusCodes.Status503ServiceUnavailable)
                    : Results.Ok(new ChatMessageResponse { Reply = reply });
            }
        );
    }
}
