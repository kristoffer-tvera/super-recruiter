using System.Text.Json;
using Discord;
using Discord.WebSocket;
using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Shared.Helpers;
using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Worker.Services;

/// <summary>
/// Discord bot that connects via gateway to send messages with interactive buttons
/// and handle button-click interactions.
/// </summary>
public class DiscordBotService : IHostedService
{
    private readonly DiscordSocketClient _client;
    private readonly ILogger<DiscordBotService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly string? _botToken;
    private readonly ulong _channelId;

    public DiscordBotService(
        ILogger<DiscordBotService> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider
    )
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _botToken = configuration["Discord:BotToken"];
        _channelId = configuration.GetValue<ulong>("Discord:ChannelId");

        _client = new DiscordSocketClient(
            new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages,
                LogLevel = LogSeverity.Info,
            }
        );

        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.InteractionCreated += InteractionCreatedAsync;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_botToken))
        {
            _logger.LogWarning("Discord bot token not configured — bot will not start");
            return;
        }

        await _client.LoginAsync(TokenType.Bot, _botToken);
        await _client.StartAsync();
        _logger.LogInformation("Discord bot started");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client.LoginState == LoginState.LoggedIn)
        {
            await _client.StopAsync();
            _logger.LogInformation("Discord bot stopped");
        }
    }

    private Task LogAsync(LogMessage msg)
    {
        _logger.LogInformation("Discord.Net: {Message}", msg.ToString());
        return Task.CompletedTask;
    }

    private Task ReadyAsync()
    {
        _logger.LogInformation("Discord bot connected as {User}", _client.CurrentUser?.Username);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends a player notification message to the configured channel with interactive buttons.
    /// Returns the Discord message ID so it can be stored in the API.
    /// </summary>
    public async Task<ulong?> SendPlayerMessageAsync(
        Player player,
        RaiderIOProfile? raiderIoProfile,
        WarcraftLogsCharacterResponse? warcraftLogsData,
        string? geminiTake,
        int apiPlayerId
    )
    {
        if (_client.ConnectionState != ConnectionState.Connected)
        {
            _logger.LogWarning("Discord bot not connected — cannot send message");
            return null;
        }

        var channel = _client.GetChannel(_channelId) as IMessageChannel;
        if (channel == null)
        {
            _logger.LogWarning("Discord channel {ChannelId} not found", _channelId);
            return null;
        }

        var thumbnail = raiderIoProfile?.Thumbnail_url ?? "";
        var warcraftLogsZoneRankings = warcraftLogsData
            ?.Data
            ?.CharacterData
            ?.Character
            ?.ZoneRankings;

        var links = new List<string>
        {
            $"[Armory](https://worldofwarcraft.blizzard.com/en-gb/character/eu/{player.RealmSlug}/{player.CharacterName})",
            raiderIoProfile != null
                ? $"[RaiderIO]({raiderIoProfile.Profile_url})"
                : "RaiderIO (no data)",
            $"[WoWProgress]({player.CharacterUrl})",
            $"[WCL](https://www.warcraftlogs.com/character/eu/{player.RealmSlug}/{player.CharacterName})",
        };

        // Build the embed
        var embed = new EmbedBuilder()
            .WithTitle(
                $"{player.CharacterName}-{player.Realm} | {player.Class} | {player.ItemLevel}"
            )
            .WithColor(new Color((uint)PlayerSummaryHelper.ClassColorFromClassName(player.Class)))
            .WithThumbnailUrl(thumbnail)
            .WithDescription(
                player.Bio != null
                    ? player.Bio[..Math.Min(player.Bio.Length, 2000)]
                    : "No bio available"
            )
            .AddField("Links", string.Join(" | ", links))
            .AddField(
                "Languages / Specs",
                $"{player.Languages ?? "N/A"} | {player.SpecsPlaying ?? "N/A"}"
            )
            .AddField(
                "Progression",
                PlayerSummaryHelper.GetCurrentExpansionProgressionSummary(raiderIoProfile)
            )
            .AddField("AOTC / CE", PlayerSummaryHelper.GetCuttingEdgeSummary(raiderIoProfile));

        if (warcraftLogsZoneRankings != null)
        {
            var allStars = PlayerSummaryHelper.GetAllStarsSummary(warcraftLogsZoneRankings);
            if (allStars.Length <= 1024)
                embed.AddField("WCL Allstars", allStars);

            var bosses = PlayerSummaryHelper.GetBossSummary(warcraftLogsZoneRankings);
            if (bosses.Length <= 1024)
                embed.AddField("WCL Bosses", bosses);
        }

        if (player.GuildHistory.Any())
        {
            var historyText = string.Join("\n", player.GuildHistory.Take(10));
            if (historyText.Length > 1024)
                historyText = historyText[..1024];
            embed.AddField("Guild History", historyText);
        }

        if (!string.IsNullOrEmpty(geminiTake))
        {
            var take = geminiTake.Length > 1024 ? geminiTake[..1024] : geminiTake;
            embed.AddField("AI Evaluation", take);
        }

        // Build action row with buttons
        var components = new ComponentBuilder()
            .WithButton("Interested", $"status:interested:{apiPlayerId}", ButtonStyle.Success)
            .WithButton("Contacted", $"status:contacted:{apiPlayerId}", ButtonStyle.Primary)
            .WithButton("Declined", $"status:declined:{apiPlayerId}", ButtonStyle.Secondary)
            .WithButton("Blacklist", $"status:blacklist:{apiPlayerId}", ButtonStyle.Danger)
            .Build();

        var message = await channel.SendMessageAsync(embed: embed.Build(), components: components);

        _logger.LogInformation(
            "Sent Discord message {MessageId} for player {Player}",
            message.Id,
            player.CharacterName
        );

        return message.Id;
    }

    private async Task InteractionCreatedAsync(SocketInteraction interaction)
    {
        if (interaction is not SocketMessageComponent component)
            return;

        // Parse custom ID: "status:{action}:{playerId}"
        var parts = component.Data.CustomId.Split(':');
        if (parts.Length != 3 || parts[0] != "status")
            return;

        var action = parts[1];
        if (!int.TryParse(parts[2], out var playerId))
            return;

        var status = action switch
        {
            "interested" => PlayerStatus.Interested,
            "contacted" => PlayerStatus.Contacted,
            "declined" => PlayerStatus.Declined,
            "blacklist" => PlayerStatus.Blacklisted,
            _ => (PlayerStatus?)null,
        };

        if (status == null)
        {
            await component.RespondAsync("Unknown action.", ephemeral: true);
            return;
        }

        try
        {
            // Use a scope to get the transient API client
            using var scope = _serviceProvider.CreateScope();
            var apiClient = scope.ServiceProvider.GetRequiredService<SuperRecruiterApiClient>();

            var updated = await apiClient.UpdatePlayerStatusAsync(playerId, status.Value);
            if (updated != null)
            {
                await component.RespondAsync(
                    $"Player **{updated.CharacterName}-{updated.Realm}** marked as **{status.Value}** by {component.User.Mention}.",
                    ephemeral: false
                );

                _logger.LogInformation(
                    "Player {Id} status updated to {Status} by {User}",
                    playerId,
                    status.Value,
                    component.User.Username
                );
            }
            else
            {
                await component.RespondAsync("Player not found.", ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Discord interaction for player {Id}", playerId);
            await component.RespondAsync(
                "An error occurred processing your action.",
                ephemeral: true
            );
        }
    }
}
