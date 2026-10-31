using SuperRecruiter.Shared.Models;
using SuperRecruiter.Worker.Services;

namespace SuperRecruiter.Worker;

public class ScraperWorker(
    ILogger<ScraperWorker> logger,
    RaiderIOService raiderIOService,
    SuperRecruiterApiClient apiClient,
    PlayerCacheService playerCache,
    PlayerIngestionService playerIngestionService,
    DiscordBotService discordBotService,
    IConfiguration configuration,
    TimeProvider? timeProvider = null
) : BackgroundService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private DateTimeOffset _nextCleanupAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollingIntervalMinutes = configuration.GetValue("PollingIntervalMinutes", 5);
        var pollingInterval = TimeSpan.FromMinutes(pollingIntervalMinutes);

        logger.LogInformation("Scraper worker starting. Polling interval: {Interval} minutes", pollingIntervalMinutes);

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
                await RunScanCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
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

    internal async Task RunScanCycleAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting player scan at: {Time}", DateTimeOffset.Now);

        var players = await GetMergedPlayersAsync(stoppingToken);
        if (players.Count > 0)
        {
            await playerCache.RefreshAsync(false, stoppingToken);
            if (players.Any(player => playerCache.GetLastSeenAt(player.CharacterName, player.Realm) is not { } lastSeen || player.LastUpdated > lastSeen))
                await playerCache.RefreshAsync(true, stoppingToken);
            await ProcessDiscoveredPlayersAsync(players, stoppingToken);
        }

        var wroteSeenPlayers = await playerCache.FlushSeenPlayerBatchAsync(stoppingToken);

        if (wroteSeenPlayers && _clock.GetUtcNow() >= _nextCleanupAt)
        {
            logger.LogInformation("Running seen player cleanup");
            await apiClient.CleanupSeenPlayersAsync(30, stoppingToken);
            _nextCleanupAt = _clock.GetUtcNow().AddDays(1);
        }
    }

    private async Task<List<Player>> GetMergedPlayersAsync(CancellationToken stoppingToken)
    {
        var wowProgressTask = GetWowProgressDiscoveriesAsync(stoppingToken);
        var raiderIoTask = raiderIOService.GetLfgPlayers(stoppingToken);
        await Task.WhenAll(wowProgressTask, raiderIoTask);
        var wowProgressPlayers = await wowProgressTask;
        var raiderIoPlayers = await raiderIoTask;

        return wowProgressPlayers
            .Concat(raiderIoPlayers)
            .GroupBy(p => $"{p.CharacterName}-{p.Realm}".ToLowerInvariant())
            .Select(g => g.OrderByDescending(p => p.LastUpdated).First())
            .ToList();
    }

    private async Task<List<Player>> GetWowProgressDiscoveriesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await apiClient.GetWowProgressDiscoveriesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Cached WoWProgress listing unavailable; continuing Raider.IO discovery");
            return [];
        }
    }

    private async Task ProcessDiscoveredPlayersAsync(List<Player> players, CancellationToken stoppingToken)
    {
        if (players.Count == 0)
        {
            logger.LogInformation("No players found in the scan");
            return;
        }

        var newPlayers = FilterNewPlayers(players, stoppingToken);
        if (newPlayers.Count == 0)
        {
            logger.LogInformation("No new players found");
            return;
        }

        logger.LogInformation("Found {NewCount} new player(s) out of {TotalCount} total", newPlayers.Count, players.Count);

        foreach (var player in newPlayers)
        {
            stoppingToken.ThrowIfCancellationRequested();
            try
            {
                await playerIngestionService.ProcessPlayerAsync(player, stoppingToken);
                playerCache.QueueSeenPlayer(player.CharacterName, player.Realm, player.LastUpdated);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Failed to process {Character}-{Realm}; will retry next scan", player.CharacterName, player.Realm);
            }
        }
    }

    private List<Player> FilterNewPlayers(IEnumerable<Player> players, CancellationToken cancellationToken)
    {
        var newPlayers = new List<Player>();

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

                newPlayers.Add(player);
            }
        }

        return newPlayers;
    }
}
