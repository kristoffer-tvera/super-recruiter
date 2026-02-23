using System.Text.Json;
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
    GeminiService geminiService,
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

        // Initial delay to let services initialize (especially Discord bot)
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                logger.LogInformation("Starting player scan at: {Time}", DateTimeOffset.Now);

                var players = await wowProgressService.GetLookingForGuildPlayersAsync(
                    stoppingToken
                );

                players = [.. players.Take(10)]; // while debugging

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

            var isBlacklisted = await apiClient.IsPlayerBlacklistedAsync(
                player.CharacterName,
                player.Realm
            );

            if (isBlacklisted)
            {
                logger.LogDebug(
                    "Skipping blacklisted player: {Character}-{Realm}",
                    player.CharacterName,
                    player.Realm
                );
                continue;
            }

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

        // if (raiderIoData != null)
        // {
        //     var cuttingEdgeScores = raiderIoData.Raid_achievement_curve;
        //     var manaForge = cuttingEdgeScores?.FirstOrDefault(a => a.Raid == "Manaforge Omega");
        //     var liberationOfUndermine = cuttingEdgeScores?.FirstOrDefault(a =>
        //         a.Raid == "Liberation Of Undermine"
        //     );

        //     if (manaForge == null && liberationOfUndermine == null)
        //     {
        //         logger.LogWarning(
        //             "Manaforge Omega or Liberation of Undermine data not found for {Character}-{Realm}",
        //             player.CharacterName,
        //             player.Realm
        //         );
        //         return;
        //     }

        //     if (manaForge?.Cutting_edge == null && liberationOfUndermine?.Cutting_edge == null)
        //     {
        //         logger.LogInformation(
        //             "Player {Character}-{Realm} does not have CE. Skipping.",
        //             player.CharacterName,
        //             player.Realm
        //         );
        //         return;
        //     }
        // }

        // 2. Enrich from WarcraftLogs
        var warcraftLogsData = await warcraftLogsService.GetCharacterDataAsync(
            player,
            cancellationToken
        );

        // 3. Enrich from WoWProgress detail page
        var detailedPlayer = await wowProgressService.GetPlayerDetailsAsync(
            player,
            cancellationToken
        );

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

        // 5. Get Gemini AI evaluation
        // var geminiTake = await geminiService.GetGeminiTake(
        //     GetGenerativeAiDescription(detailedPlayer, raiderIoData, warcraftLogsData)
        // );
        var geminiTake = "Gemini take placeholder";

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
            RaiderIoDataJson = raiderIoData != null ? JsonSerializer.Serialize(raiderIoData) : null,
            WarcraftLogsDataJson =
                warcraftLogsData != null ? JsonSerializer.Serialize(warcraftLogsData) : null,
            GeminiTake = geminiTake,
        };

        var apiPlayer = await apiClient.CreatePlayerAsync(createRequest);

        // 7. Send Discord message with buttons
        var messageId = await discordBotService.SendPlayerMessageAsync(
            detailedPlayer,
            raiderIoData,
            warcraftLogsData,
            geminiTake,
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

    private string GetGenerativeAiDescription(
        Player player,
        RaiderIOProfile? raiderIoData,
        WarcraftLogsCharacterResponse? warcraftLogsData
    )
    {
        var textBlocks = new List<string>
        {
            $"Character: {player.CharacterName}",
            $"Realm: {player.Realm}",
            $"Bio: {player.Bio}",
            $"Language: {player.Languages}",
            $"Specs: {player.SpecsPlaying}",
        };

        var warcraftLogsZoneRankings = warcraftLogsData?.Data?.CharacterData.Character.ZoneRankings;
        var currentExpansionProgression = PlayerSummaryHelper.GetCurrentExpansionProgressionSummary(
            raiderIoData
        );
        var cuttingEdgeProgression = PlayerSummaryHelper.GetCuttingEdgeSummary(raiderIoData);
        var bossRankings = PlayerSummaryHelper.GetBossSummary(warcraftLogsZoneRankings);
        var allStars = PlayerSummaryHelper.GetAllStarsSummary(warcraftLogsZoneRankings);
        var guildHistory = player.GuildHistory.Any()
            ? $"## Guild History:\n- {string.Join("\n- ", player.GuildHistory)}"
            : "No guild history available";

        textBlocks.Add(currentExpansionProgression);
        textBlocks.Add(cuttingEdgeProgression);
        textBlocks.Add(bossRankings);
        textBlocks.Add(allStars);
        textBlocks.Add(guildHistory);

        return string.Join("\n\n", textBlocks);
    }
}
