using SuperRecruiter.Shared.Models;
using SuperRecruiter.Worker.Services;

namespace SuperRecruiter.Worker;

public class ScraperWorker(
    ILogger<ScraperWorker> logger,
    WowProgressService wowProgressService,
    RaiderIOService raiderIOService,
    SuperRecruiterApiClient apiClient,
    PlayerCacheService playerCache,
    PlayerIngestionService playerIngestionService,
    DiscordBotService discordBotService,
    IConfiguration configuration
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollingIntervalMinutes = configuration.GetValue("PollingIntervalMinutes", 5);
        var pollingInterval = TimeSpan.FromMinutes(pollingIntervalMinutes);

        logger.LogInformation("Scraper worker starting. Polling interval: {Interval} minutes", pollingIntervalMinutes);

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

        var cycleCount = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                cycleCount = await RunScanCycleAsync(cycleCount, stoppingToken);
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

    private async Task<int> RunScanCycleAsync(int cycleCount, CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting player scan at: {Time}", DateTimeOffset.Now);

        var refreshPlayers = cycleCount % 4 == 0;
        await playerCache.RefreshAsync(refreshPlayers);

        var nextCycleCount = cycleCount + 1;
        var players = await GetMergedPlayersAsync(stoppingToken);
        await ProcessDiscoveredPlayersAsync(players, stoppingToken);

        // Flush all queued seen players in a single bulk operation
        await playerCache.FlushSeenPlayerBatchAsync();

        // Run cleanup every 6 cycles (every 6 hours at 30-min intervals)
        if (nextCycleCount % 6 == 0)
        {
            logger.LogInformation("Running seen player cleanup");
            await apiClient.CleanupSeenPlayersAsync(30);
        }

        return nextCycleCount;
    }

    private async Task<List<Player>> GetMergedPlayersAsync(CancellationToken stoppingToken)
    {
        var playersFromWoWProgress = await wowProgressService.GetLookingForGuildPlayersAsync(stoppingToken);
        var playersFromRaiderIo = await raiderIOService.GetLfgPlayers(stoppingToken);

        return playersFromWoWProgress
            .Concat(playersFromRaiderIo)
            .GroupBy(p => $"{p.CharacterName}-{p.Realm}".ToLowerInvariant())
            .Select(g => g.OrderByDescending(p => p.LastUpdated).First())
            .ToList();
    }

    private async Task ProcessDiscoveredPlayersAsync(List<Player> players, CancellationToken stoppingToken)
    {
        if (players.Count == 0)
        {
            logger.LogInformation("No players found in the scan");
            return;
        }

        var newPlayers = await FilterPlayersAsync(players, stoppingToken);
        if (newPlayers?.Count > 0)
        {
            logger.LogInformation("Found {NewCount} new player(s) out of {TotalCount} total", newPlayers.Count, players.Count);

            foreach (var player in newPlayers)
            {
                await playerIngestionService.ProcessPlayerAsync(player, stoppingToken);
            }
            return;
        }

        logger.LogInformation("No new players found");
    }

    public async Task<List<Player>?> FilterPlayersAsync(List<Player> players, CancellationToken cancellationToken)
    {
        var filteredPlayers = new List<Player>();

        foreach (var player in players)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (playerCache.IsBlacklisted(player.CharacterName, player.Realm))
            {
                logger.LogDebug("Skipping blacklisted player: {Character}-{Realm}", player.CharacterName, player.Realm);
                continue;
            }

            var lastSeenAt = playerCache.GetLastSeenAt(player.CharacterName, player.Realm);

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

                playerCache.QueueSeenPlayer(player.CharacterName, player.Realm, player.LastUpdated);
                filteredPlayers.Add(player);
            }
        }
        return filteredPlayers;
    }
}
