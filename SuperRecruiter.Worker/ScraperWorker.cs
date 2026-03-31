using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Shared.Helpers;
using SuperRecruiter.Shared.Models;
using SuperRecruiter.Worker.Services;

namespace SuperRecruiter.Worker;

public class ScraperWorker(
    ILogger<ScraperWorker> logger,
    WowProgressService wowProgressService,
    RaiderIOService raiderIOService,
    WarcraftLogsService warcraftLogsService,
    SuperRecruiterApiClient apiClient,
    DiscordBotService discordBotService,
    IConfiguration configuration
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollingIntervalMinutes = configuration.GetValue("PollingIntervalMinutes", 5);
        var pollingInterval = TimeSpan.FromMinutes(pollingIntervalMinutes);

        logger.LogInformation(
            "Scraper worker starting. Polling interval: {Interval} minutes",
            pollingIntervalMinutes
        );

        // Wait for Discord bot to be fully ready before starting the scan loop
        logger.LogInformation("Waiting for Discord bot to be ready...");
        var botReady = await discordBotService.WaitUntilReadyAsync(TimeSpan.FromSeconds(30));
        if (!botReady)
        {
            logger.LogWarning("Discord bot did not become ready within 30s — continuing anyway");
        }
        else
        {
            logger.LogInformation("Discord bot is ready");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                logger.LogInformation("Starting player scan at: {Time}", DateTimeOffset.Now);

                var playersFromWoWProgress =
                    await wowProgressService.GetLookingForGuildPlayersAsync(stoppingToken);

                var playersFromRaiderIo = await raiderIOService.GetLfgPlayers(stoppingToken);

                var players = playersFromWoWProgress
                    .Concat(playersFromRaiderIo)
                    .GroupBy(p => $"{p.CharacterName}-{p.Realm}".ToLowerInvariant())
                    .Select(g => g.OrderByDescending(p => p.LastUpdated).First())
                    .ToList();

                if (players.Count == 0)
                {
                    logger.LogInformation("No players found in the scan");
                }
                else
                {
                    var newPlayers = await FilterPlayersAsync(players, stoppingToken);
                    if (newPlayers?.Count > 0)
                    {
                        logger.LogInformation(
                            "Found {NewCount} new player(s) out of {TotalCount} total",
                            newPlayers.Count,
                            players.Count
                        );

                        foreach (var player in newPlayers)
                        {
                            await ProcessPlayerAsync(player, stoppingToken);
                            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                        }
                    }
                    else
                    {
                        logger.LogInformation("No new players found");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during player scan");
            }

            logger.LogInformation("Next scan in {Interval} minutes", pollingIntervalMinutes);
            await Task.Delay(pollingInterval, stoppingToken);
        }

        logger.LogInformation("Scraper worker stopping");
    }

    public async Task<List<Player>?> FilterPlayersAsync(
        List<Player> players,
        CancellationToken cancellationToken
    )
    {
        var filteredPlayers = new List<Player>();

        foreach (var player in players)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lastSeenAt = await apiClient.GetLastSeenAtAsync(player.CharacterName, player.Realm);

            if (lastSeenAt == null || player.LastUpdated > lastSeenAt.Value)
            {
                if (lastSeenAt != null)
                {
                    logger.LogInformation(
                        "Player {Character}-{Realm} re-listed (LastUpdated: {Updated}, LastSeen: {Seen})",
                        player.CharacterName,
                        player.Realm,
                        player.LastUpdated,
                        lastSeenAt.Value
                    );
                }

                await apiClient.AddSeenPlayerAsync(
                    player.CharacterName,
                    player.Realm,
                    player.LastUpdated
                );
                filteredPlayers.Add(player);
            }
        }
        return filteredPlayers;
    }

    public async Task ProcessPlayerAsync(Player player, CancellationToken cancellationToken)
    {
        // 1. Enrich from RaiderIO
        var raiderIoData = await raiderIOService.GetCharacterProfileAsync(
            "eu",
            player.RealmSlug,
            player.CharacterName,
            cancellationToken
        );

        // 2. Enrich from WarcraftLogs
        var warcraftLogsData = await warcraftLogsService.GetCharacterDataAsync(
            player,
            cancellationToken
        );

        // 3. Enrich from WoWProgress detail page
        Player detailedPlayer;
        if (player.Source == LfgSource.WoWProgress)
        {
            detailedPlayer = await wowProgressService.GetPlayerDetailsAsync(
                player,
                cancellationToken
            );
        }
        else
        {
            // For RaiderIO-sourced players, we may already have most of the details we need from the RaiderIO profile, so we can skip the WoWProgress detail page enrichment to reduce load and avoid potential issues with WoWProgress blocking requests.
            detailedPlayer = player;
        }

        // 4. Language filter
        if (!string.IsNullOrWhiteSpace(player.Languages))
        {
            if (!player.Languages.ToLower().Contains("eng"))
            {
                logger.LogInformation(
                    "Player {Character}-{Realm} does not speak English. Skipping.",
                    player.CharacterName,
                    player.Realm
                );
                return;
            }
        }

        // 5. Build markdown summaries from enrichment data
        var warcraftLogsZoneRankings = warcraftLogsData
            ?.Data
            ?.CharacterData
            ?.Character
            ?.ZoneRankings;

        var raiderIoSummaryParts = new List<string>();
        raiderIoSummaryParts.Add(
            PlayerSummaryHelper.GetCurrentExpansionProgressionSummary(raiderIoData)
        );
        raiderIoSummaryParts.Add(PlayerSummaryHelper.GetCuttingEdgeSummary(raiderIoData));
        var raiderIoSummary = string.Join("\n\n", raiderIoSummaryParts);

        var wclSummaryParts = new List<string>();
        if (warcraftLogsZoneRankings != null)
        {
            wclSummaryParts.Add(PlayerSummaryHelper.GetAllStarsSummary(warcraftLogsZoneRankings));
            wclSummaryParts.Add(PlayerSummaryHelper.GetBossSummary(warcraftLogsZoneRankings));
        }
        var warcraftLogsSummary =
            wclSummaryParts.Count > 0 ? string.Join("\n\n", wclSummaryParts) : null;

        // 6. POST enriched player to API
        var createRequest = new CreatePlayerRequest
        {
            CharacterName = detailedPlayer.CharacterName,
            Class = detailedPlayer.Class,
            Realm = detailedPlayer.Realm,
            RealmSlug = detailedPlayer.RealmSlug,
            ItemLevel = detailedPlayer.ItemLevel,
            LastUpdated = detailedPlayer.LastUpdated,
            CharacterUrl = detailedPlayer.CharacterUrl,
            BattleTag = detailedPlayer.BattleTag,
            Bio = detailedPlayer.Bio,
            Languages = detailedPlayer.Languages,
            SpecsPlaying = detailedPlayer.SpecsPlaying,
            GuildHistory = detailedPlayer.GuildHistory.ToList(),
            RaiderIoSummary = raiderIoSummary,
            WarcraftLogsSummary = warcraftLogsSummary,
        };

        var apiPlayer = await apiClient.CreatePlayerAsync(createRequest);

        // 7. Send Discord message with buttons
        var messageId = await discordBotService.SendPlayerMessageAsync(
            detailedPlayer,
            raiderIoData,
            apiPlayer.Id
        );

        // 8. Update the API record with the Discord message ID
        if (messageId.HasValue)
        {
            // Re-POST with discord info (the upsert will update)
            createRequest.DiscordMessageId = messageId.Value;
            await apiClient.CreatePlayerAsync(createRequest);
        }

        logger.LogInformation(
            "Successfully processed player {Character}-{Realm}",
            detailedPlayer.CharacterName,
            detailedPlayer.Realm
        );
    }
}
