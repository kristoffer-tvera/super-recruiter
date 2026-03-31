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
    private readonly string _frontendBaseUrl;
    private readonly TaskCompletionSource _readyTcs = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

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
        _frontendBaseUrl =
            configuration["FrontendBaseUrl"]
            ?? throw new InvalidOperationException("FrontendBaseUrl is not configured");

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
        _client.Disconnected += ex =>
        {
            _logger.LogWarning(ex, "Discord bot disconnected");
            return Task.CompletedTask;
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_botToken))
        {
            _logger.LogWarning("Discord bot token not configured — bot will not start");
            return;
        }

        await _client.LoginAsync(TokenType.Bot, _botToken);
        _logger.LogInformation("Discord bot login completed, starting gateway connection...");
        await _client.StartAsync();
        _logger.LogInformation("Discord bot StartAsync returned — waiting for Ready event");
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
        _logger.LogInformation(
            "Discord bot guilds: {GuildCount} | Cached channels: {ChannelCount}",
            _client.Guilds.Count,
            _client.Guilds.SelectMany(g => g.Channels).Count()
        );

        var targetChannel = _client.GetChannel(_channelId);

        _readyTcs.TrySetResult();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Waits until the bot has fully connected and the guild/channel cache is populated.
    /// Returns false if the bot isn't configured or doesn't become ready within the timeout.
    /// </summary>
    public async Task<bool> WaitUntilReadyAsync(TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(_botToken))
            return false;

        var delay = Task.Delay(timeout ?? TimeSpan.FromSeconds(30));
        var completed = await Task.WhenAny(_readyTcs.Task, delay);
        return completed == _readyTcs.Task;
    }

    /// <summary>
    /// Sends a player notification message to the configured channel with interactive buttons.
    /// Returns the Discord message ID so it can be stored in the API.
    /// </summary>
    public async Task<ulong?> SendPlayerMessageAsync(
        Player player,
        RaiderIOProfile? raiderIoProfile,
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
            _logger.LogWarning(
                "Discord channel {ChannelId} not found. ConnectionState={State}, Guilds={GuildCount}, LoginState={LoginState}",
                _channelId,
                _client.ConnectionState,
                _client.Guilds.Count,
                _client.LoginState
            );
            return null;
        }

        var thumbnail = raiderIoProfile?.Thumbnail_url ?? "";

        var links = new List<string>
        {
            $"[Armory](https://worldofwarcraft.blizzard.com/en-gb/character/eu/{player.RealmSlug}/{player.CharacterName})",
            $"[RaiderIO](https://raider.io/characters/eu/{player.RealmSlug}/{player.CharacterName})",
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
            );

        // Build action row with buttons
        var components = new ComponentBuilder()
            .WithButton("Interested", $"status:interested:{apiPlayerId}", ButtonStyle.Success)
            .WithButton("Contacted", $"status:contacted:{apiPlayerId}", ButtonStyle.Primary)
            .WithButton("Declined", $"status:declined:{apiPlayerId}", ButtonStyle.Secondary)
            .WithButton("Blacklist", $"status:blacklist:{apiPlayerId}", ButtonStyle.Danger)
            .WithButton(
                "Open",
                style: ButtonStyle.Link,
                url: $"{_frontendBaseUrl}/players/{apiPlayerId}"
            )
            .Build();

        var message = await channel.SendMessageAsync(embed: embed.Build(), components: components);

        _logger.LogInformation(
            "Sent Discord message {MessageId} for player {Player}",
            message.Id,
            player.CharacterName
        );

        return message.Id;
    }

    public async Task SendDebugMessageAsync(string content)
    {
        if (_client.ConnectionState != ConnectionState.Connected)
        {
            _logger.LogWarning("Discord bot not connected — cannot send debug message");
            return;
        }

        var channel = _client.GetChannel(_channelId) as IMessageChannel;
        if (channel == null)
        {
            _logger.LogWarning("Discord channel {ChannelId} not found", _channelId);
            return;
        }

        await channel.SendMessageAsync($"[DEBUG] {content}");
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
                var deleteMessage =
                    status.Value == PlayerStatus.Declined
                    || status.Value == PlayerStatus.Blacklisted;

                if (deleteMessage)
                {
                    await component.RespondAsync(
                        $"Player **{updated.CharacterName}-{updated.Realm}** marked as **{status.Value}** by {component.User.Mention}.",
                        ephemeral: true
                    );
                    await component.Message.DeleteAsync();
                }
                else
                {
                    await component.RespondAsync(
                        $"Player **{updated.CharacterName}-{updated.Realm}** marked as **{status.Value}** by {component.User.Mention}.",
                        ephemeral: false
                    );
                }

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
