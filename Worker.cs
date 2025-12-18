using SuperRecruiter.Models;
using SuperRecruiter.Services;

namespace SuperRecruiter;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IWowProgressService _wowProgressService;
    private readonly IDiscordWebhookService _discordWebhookService;
    private readonly IRaiderIOService _raiderIOService;
    private readonly IConfiguration _configuration;
    private HashSet<string> _seenPlayers = new();

    public Worker(
        ILogger<Worker> logger,
        IWowProgressService wowProgressService,
        IDiscordWebhookService discordWebhookService,
        IRaiderIOService raiderIOService,
        IConfiguration configuration
    )
    {
        _logger = logger;
        _wowProgressService = wowProgressService;
        _discordWebhookService = discordWebhookService;
        _raiderIOService = raiderIOService;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Get polling interval from config (default to 5 minutes)
        var pollingIntervalMinutes = _configuration.GetValue<int>("PollingIntervalMinutes", 5);
        var pollingInterval = TimeSpan.FromMinutes(pollingIntervalMinutes);

        _logger.LogInformation(
            "Super Recruiter worker starting. Polling interval: {Interval} minutes",
            pollingIntervalMinutes
        );

        var (bslProfile, bslRaidProgress) = await _raiderIOService.GetCharacterProfileAsync(
            "eu",
            "stormscale",
            "bsl",
            stoppingToken
        );

        return;

        // Initial delay to let services initialize
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Starting player scan at: {Time}", DateTimeOffset.Now);

                var players = await _wowProgressService.GetLookingForGuildPlayersAsync(
                    stoppingToken
                );

                if (players.Count > 0)
                {
                    // Identify new players we haven't seen before
                    var newPlayers = new List<Player>();

                    foreach (var player in players)
                    {
                        // Create a unique key for each player
                        var playerKey = $"{player.CharacterName}|{player.Realm}|{player.ItemLevel}";

                        if (!_seenPlayers.Contains(playerKey))
                        {
                            _seenPlayers.Add(playerKey);
                            newPlayers.Add(player);
                        }
                    }

                    if (newPlayers.Count > 0)
                    {
                        _logger.LogInformation(
                            "Found {NewCount} new player(s) out of {TotalCount} total",
                            newPlayers.Count,
                            players.Count
                        );

                        await _discordWebhookService.SendNewPlayersNotificationAsync(
                            newPlayers,
                            stoppingToken
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "No new players found. Total players tracked: {Count}",
                            _seenPlayers.Count
                        );
                    }

                    // Cleanup old entries if the set gets too large (keep last 1000)
                    if (_seenPlayers.Count > 1000)
                    {
                        _logger.LogInformation("Cleaning up player tracking cache");
                        _seenPlayers = new HashSet<string>(
                            _seenPlayers.Skip(_seenPlayers.Count - 500)
                        );
                    }
                }
                else
                {
                    _logger.LogWarning("No players found in the scan");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during player scan");
            }

            _logger.LogInformation("Next scan in {Interval} minutes", pollingIntervalMinutes);
            await Task.Delay(pollingInterval, stoppingToken);
        }

        _logger.LogInformation("Super Recruiter worker stopping");
    }
}
