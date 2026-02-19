using SuperRecruiter.Models;
using SuperRecruiter.Services;

namespace SuperRecruiter;

public class Worker(
    ILogger<Worker> logger,
    WowProgressService wowProgressService,
    DiscordWebhookService discordWebhookService,
    RaiderIOService raiderIOService,
    WarcraftLogsService warcraftLogsService,
    PlayerDatabaseService playerDatabaseService,
    GeminiService geminiService,
    IConfiguration configuration
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Get polling interval from config (default to 5 minutes)
        var pollingIntervalMinutes = configuration.GetValue("PollingIntervalMinutes", 5);
        var pollingInterval = TimeSpan.FromMinutes(pollingIntervalMinutes);

        logger.LogInformation(
            "Super Recruiter worker starting. Polling interval: {Interval} minutes",
            pollingIntervalMinutes
        );

        // Initial delay to let services initialize
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

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
                    continue;
                }
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
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); // small delay between notifications
                    }
                }
                else
                {
                    var totalCount = await playerDatabaseService.GetSeenPlayersCountAsync();
                    logger.LogInformation(
                        "No new players found. Total players tracked: {Count}",
                        totalCount
                    );
                }

                // Periodic cleanup of old entries (keep last 30 days)
                await playerDatabaseService.CleanupOldSeenPlayersAsync(30);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during player scan");
            }

            logger.LogInformation("Next scan in {Interval} minutes", pollingIntervalMinutes);
            await Task.Delay(pollingInterval, stoppingToken);
        }

        logger.LogInformation("Super Recruiter worker stopping");
    }

    public async Task<List<Player>?> FilterPlayersAsync(
        List<Player> players,
        CancellationToken cancellationToken
    )
    {
        // Identify new players or players who have re-listed (updated their listing)
        var filteredPlayers = new List<Player>();

        foreach (var player in players)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Check if player is blacklisted
            var isBlacklisted = await playerDatabaseService.IsPlayerBlacklistedAsync(
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

            // Check if we've seen this player before and when
            var lastSeenAt = await playerDatabaseService.GetLastSeenAtAsync(
                player.CharacterName,
                player.Realm
            );

            // Process player if:
            // 1. We've never seen them (lastSeenAt == null), OR
            // 2. Their LastUpdated is newer than when we last saw them (they re-listed)
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

                await playerDatabaseService.AddSeenPlayerAsync(
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
        var raiderIoData = await raiderIOService.GetCharacterProfileAsync(
            "eu",
            player.RealmSlug,
            player.CharacterName,
            cancellationToken
        );

        if (raiderIoData != null)
        {
            var cuttingEdgeScores = raiderIoData.Raid_achievement_curve;
            var manaForge = cuttingEdgeScores?.FirstOrDefault(a => a.Raid == "Manaforge Omega");
            var liberationOfUndermine = cuttingEdgeScores?.FirstOrDefault(a =>
                a.Raid == "Liberation Of Undermine"
            );

            if (manaForge == null && liberationOfUndermine == null)
            {
                logger.LogWarning(
                    "Manaforge Omega or Liberation of Undermine data not found for player {Character}-{Realm}",
                    player.CharacterName,
                    player.Realm
                );
                // await discordWebhookService.SendPlayerWasFilteredOutNotificationAsync(
                //     player,
                //     "Manaforge Omega or Liberation of Undermine data not found",
                //     cancellationToken
                // );
                return;
            }

            if (manaForge?.Cutting_edge == null && liberationOfUndermine?.Cutting_edge == null)
            {
                logger.LogInformation(
                    "Player {Character}-{Realm} does not have Manaforge Omega Cutting Edge or Liberation of Undermine Cutting Edge. Skipping.",
                    player.CharacterName,
                    player.Realm
                );
                // await discordWebhookService.SendPlayerWasFilteredOutNotificationAsync(
                //     player,
                //     "Does not have Manaforge Omega or Liberation of Undermine Cutting Edge",
                //     cancellationToken
                // );
                return;
            }
        }

        var warcraftLogsData = await warcraftLogsService.GetCharacterDataAsync(
            player,
            cancellationToken
        );

        var detailedPlayer = await wowProgressService.GetPlayerDetailsAsync(
            player,
            cancellationToken
        );

        if (!string.IsNullOrWhiteSpace(player.Languages))
        {
            if (!player.Languages.ToLower().Contains("eng"))
            {
                logger.LogInformation(
                    "Player {Character}-{Realm} does not speak English. Skipping.",
                    player.CharacterName,
                    player.Realm
                );
                // await discordWebhookService.SendPlayerWasFilteredOutNotificationAsync(
                //     player,
                //     "Does not speak English",
                //     cancellationToken
                // );
                return;
            }
        }

        var geminiTake = await geminiService.GetGeminiTake(
            GetGenerativeAiDescription(detailedPlayer, raiderIoData, warcraftLogsData)
        );

        await discordWebhookService.SendNewPlayerNotificationAsync(
            detailedPlayer,
            raiderIoData,
            warcraftLogsData,
            geminiTake,
            cancellationToken
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
        var currentExpansionProgression =
            DiscordWebhookService.GetCurrentExpansionProgressionSummary(raiderIoData);
        var cuttingEdgeProgression = DiscordWebhookService.GetCuttingEdgeSummary(raiderIoData);
        var bossRankings = DiscordWebhookService.GetBossSummary(warcraftLogsZoneRankings);
        var allStars = DiscordWebhookService.GetAllStarsSummary(warcraftLogsZoneRankings);
        var guildHistory = player.GuildHistory.Any()
            ? $"## Guild History:\n- {string.Join("\n- ", player.GuildHistory)}"
            : "No guild history available";

        textBlocks.Add(currentExpansionProgression);
        textBlocks.Add(cuttingEdgeProgression);
        textBlocks.Add(bossRankings);
        textBlocks.Add(allStars);
        textBlocks.Add(guildHistory);

        var generativeAiDescription = string.Join("\n\n", textBlocks);
        return generativeAiDescription;
    }
}
