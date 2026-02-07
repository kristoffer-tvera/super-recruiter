using SuperRecruiter.Models;
using SuperRecruiter.Services;

namespace SuperRecruiter;

public class Worker(
    ILogger<Worker> logger,
    IWowProgressService wowProgressService,
    IDiscordWebhookService discordWebhookService,
    IRaiderIOService raiderIOService,
    IWarcraftLogsService warcraftLogsService,
    IPlayerDatabaseService playerDatabaseService,
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

                players = players.Take(3).ToList(); // while debugging

                if (players.Count > 0)
                {
                    // Identify new players or players who have re-listed (updated their listing)
                    var newPlayers = new List<Player>();

                    foreach (var player in players)
                    {
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
                            newPlayers.Add(player);
                        }
                    }

                    if (newPlayers.Count > 0)
                    {
                        logger.LogInformation(
                            "Found {NewCount} new player(s) out of {TotalCount} total",
                            newPlayers.Count,
                            players.Count
                        );

                        foreach (var player in newPlayers)
                        {
                            var raiderIoData = await raiderIOService.GetCharacterProfileAsync(
                                "eu",
                                player.RealmSlug,
                                player.CharacterName,
                                stoppingToken
                            );

                            var warcraftLogsData = await warcraftLogsService.GetCharacterDataAsync(
                                player,
                                stoppingToken
                            );

                            var detailedPlayer = await wowProgressService.GetPlayerDetailsAsync(
                                player,
                                stoppingToken
                            );

                            await discordWebhookService.SendNewPlayerNotificationAsync(
                                detailedPlayer,
                                raiderIoData,
                                warcraftLogsData,
                                stoppingToken
                            );

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
                else
                {
                    logger.LogWarning("No players found in the scan");
                }
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
}
