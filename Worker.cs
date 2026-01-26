using SuperRecruiter.Models;
using SuperRecruiter.Services;

namespace SuperRecruiter;

public class Worker(
    ILogger<Worker> logger,
    IWowProgressService wowProgressService,
    IDiscordWebhookService discordWebhookService,
    IRaiderIOService raiderIOService,
    IWarcraftLogsService warcraftLogsService,
    IConfiguration configuration
) : BackgroundService
{
    private HashSet<string> _seenPlayers = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Get polling interval from config (default to 5 minutes)
        var pollingIntervalMinutes = configuration.GetValue<int>("PollingIntervalMinutes", 5);
        var pollingInterval = TimeSpan.FromMinutes(pollingIntervalMinutes);

        logger.LogInformation(
            "Super Recruiter worker starting. Polling interval: {Interval} minutes",
            pollingIntervalMinutes
        );

        // var (bslProfile, bslRaidProgress) = await _raiderIOService.GetCharacterProfileAsync(
        //     "eu",
        //     "stormscale",
        //     "bsl",
        //     stoppingToken
        // );

        var bslPlayer = new Player { CharacterName = "Bsl", Realm = "Stormscale" };

        var bslData = await warcraftLogsService.GetCharacterDataAsync(
            bslPlayer,
            stoppingToken
        );

        return;

        // RETURN;

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
                    // Identify new players we haven't seen before
                    var newPlayers = new List<Player>();

                    foreach (var player in players)
                    {
                        // Create a unique key for each player
                        var playerKey = $"{player.CharacterName}|{player.Realm}";

                        if (!_seenPlayers.Contains(playerKey))
                        {
                            _seenPlayers.Add(playerKey);
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

                        // Break into chunks of 10 for Discord limitation
                        for (int i = 0; i < newPlayers.Count; i += 10)
                        {
                            var chunk = newPlayers.Skip(i).Take(10).ToList();
                            await discordWebhookService.SendNewPlayersNotificationAsync(
                                chunk,
                                stoppingToken
                            );
                        }
                    }
                    else
                    {
                        logger.LogInformation(
                            "No new players found. Total players tracked: {Count}",
                            _seenPlayers.Count
                        );
                    }

                    // Cleanup old entries if the set gets too large (keep last 1000)
                    if (_seenPlayers.Count > 1000)
                    {
                        logger.LogInformation("Cleaning up player tracking cache");
                        _seenPlayers = new HashSet<string>(
                            _seenPlayers.Skip(_seenPlayers.Count - 500)
                        );
                    }
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
