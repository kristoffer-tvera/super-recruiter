using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SuperRecruiter.Shared.Constants;
using SuperRecruiter.Shared.Helpers;
using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Worker.Services;

/// <summary>
/// Discord bot that connects via gateway to send messages with interactive buttons
/// and handle button-click interactions.
/// </summary>
public partial class DiscordBotService : IHostedService
{
    private const ulong OfficerRoleId = 420722779432943616;
    private const string AddPlayerCommandName = "add-player";
    private const string ChatFallbackReply = "... ye, I'm gonna have to sit this one out, I have technical issues right now.";
    private const int DiscordMessageLimit = 2000;

    private static readonly TimeSpan ChatCooldown = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<ulong, DateTime> _lastChatReplyByUser = new();

    private readonly DiscordSocketClient _client;
    private readonly ILogger<DiscordBotService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly string? _botToken;
    private readonly ulong _channelId;
    private readonly ulong _guildId;
    private readonly string _frontendBaseUrl;
    private readonly TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DiscordBotService(ILogger<DiscordBotService> logger, IConfiguration configuration, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _botToken = configuration["Discord:BotToken"];
        _channelId = configuration.GetValue<ulong>("Discord:ChannelId");
        _guildId = configuration.GetValue<ulong>("Discord:GuildId");
        _frontendBaseUrl = configuration["FrontendBaseUrl"] ?? throw new InvalidOperationException("FrontendBaseUrl is not configured");

        _client = new DiscordSocketClient(new DiscordSocketConfig { GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages, LogLevel = LogSeverity.Info });

        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.InteractionCreated += InteractionCreatedAsync;
        _client.MessageReceived += MessageReceivedAsync;
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

    private async Task ReadyAsync()
    {
        _logger.LogInformation("Discord bot connected as {User}", _client.CurrentUser?.Username);
        _logger.LogInformation("Discord bot guilds: {GuildCount} | Cached channels: {ChannelCount}", _client.Guilds.Count, _client.Guilds.SelectMany(g => g.Channels).Count());

        var targetChannel = _client.GetChannel(_channelId);

        await RegisterSlashCommandsAsync();

        _readyTcs.TrySetResult();
    }

    private async Task RegisterSlashCommandsAsync()
    {
        var command = new SlashCommandBuilder()
            .WithName(AddPlayerCommandName)
            .WithDescription("Manually add a character to Super Recruiter")
            .AddOption("character", ApplicationCommandOptionType.String, "Character name", isRequired: true)
            .AddOption(
                new SlashCommandOptionBuilder()
                    .WithName("realm")
                    .WithDescription("EU realm")
                    .WithType(ApplicationCommandOptionType.String)
                    .WithRequired(true)
                    .WithAutocomplete(true)
            )
            .Build();

        try
        {
            // Guild commands register instantly; global commands can take up to an hour to propagate.
            var guild = _guildId != 0 ? _client.GetGuild(_guildId) : null;

            if (guild != null)
            {
                await guild.CreateApplicationCommandAsync(command);
                _logger.LogInformation("Registered /{Command} for guild {GuildId}", AddPlayerCommandName, _guildId);
            }
            else
            {
                if (_guildId != 0)
                    _logger.LogWarning("Discord:GuildId {GuildId} not found in the bot's guilds — registering /{Command} globally instead", _guildId, AddPlayerCommandName);

                await _client.CreateGlobalApplicationCommandAsync(command);
                _logger.LogInformation("Registered /{Command} globally", AddPlayerCommandName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register the /{Command} slash command", AddPlayerCommandName);
        }
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
    public async Task<ulong?> SendPlayerMessageAsync(Player player, RaiderIOProfile? raiderIoProfile)
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
            $"[WCL](https://www.warcraftlogs.com/character/eu/{player.RealmSlug}/{player.CharacterName})",
        };

        // Manually added players have no WoWProgress listing.
        if (!string.IsNullOrWhiteSpace(player.CharacterUrl))
        {
            links.Insert(2, $"[WoWProgress]({player.CharacterUrl})");
        }

        var currentTierExp = PlayerSummaryHelper.GetCurrentExpansionProgressForDiscord(raiderIoProfile);

        // Build the embed
        var embed = new EmbedBuilder()
            .WithTitle($"{player.CharacterName}-{player.Realm} | {player.Class} | {player.ItemLevel} | {currentTierExp}")
            .WithColor(new Color((uint)PlayerSummaryHelper.ClassColorFromClassName(player.Class)))
            .WithThumbnailUrl(thumbnail)
            .WithDescription(player.Bio != null ? player.Bio[..Math.Min(player.Bio.Length, 2000)] : "No bio available")
            .AddField("Links", string.Join(" | ", links))
            .AddField("Languages / Specs", $"{player.Languages ?? "N/A"} | {player.SpecsPlaying ?? "N/A"}");

        // Build action row with buttons
        var components = new ComponentBuilder()
            .WithButton("Interested", $"status:interested:{player.RealmSlug}:{player.CharacterName}", ButtonStyle.Success)
            .WithButton("Contacted", $"status:contacted:{player.RealmSlug}:{player.CharacterName}", ButtonStyle.Primary)
            .WithButton("Declined", $"status:declined:{player.RealmSlug}:{player.CharacterName}", ButtonStyle.Secondary)
            .WithButton("Blacklist", $"status:blacklist:{player.RealmSlug}:{player.CharacterName}", ButtonStyle.Danger)
            .WithButton("Open", style: ButtonStyle.Link, url: $"{_frontendBaseUrl}/{player.RealmSlug}/{player.CharacterName}")
            .Build();

        var message = await channel.SendMessageAsync(embed: embed.Build(), components: components);

        _logger.LogInformation("Sent Discord message {MessageId} for player {Player}", message.Id, player.CharacterName);

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

    private Task MessageReceivedAsync(SocketMessage socketMessage)
    {
        if (socketMessage is not SocketUserMessage message)
            return Task.CompletedTask;

        if (message.Author.IsBot || message.Author.IsWebhook || message.Author.Id == _client.CurrentUser?.Id)
            return Task.CompletedTask;

        // Only a direct @mention counts — @everyone and role pings are ignored.
        if (message.MentionedEveryone || _client.CurrentUser == null || !message.MentionedUsers.Any(u => u.Id == _client.CurrentUser.Id))
            return Task.CompletedTask;

        // Don't hold up the gateway while waiting on the AI.
        _ = Task.Run(() => HandleMentionAsync(message));

        return Task.CompletedTask;
    }

    private async Task HandleMentionAsync(SocketUserMessage message)
    {
        try
        {
            if (!TryStartChatCooldown(message.Author.Id))
            {
                _logger.LogDebug("Ignoring mention from {User} — still on cooldown", message.Author.Username);
                return;
            }

            var prompt = MentionPattern().Replace(message.Content, string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(prompt))
            {
                await ReplyInChannelAsync(message, "You rang? Ask me something about recruitment or applicants.");
                return;
            }

            string? reply;
            using (message.Channel.EnterTypingState())
            {
                using var scope = _serviceProvider.CreateScope();
                var apiClient = scope.ServiceProvider.GetRequiredService<SuperRecruiterApiClient>();

                var displayName = (message.Author as SocketGuildUser)?.DisplayName ?? message.Author.Username;
                reply = await apiClient.GetChatReplyAsync(prompt, displayName);
            }

            await ReplyInChannelAsync(message, reply ?? ChatFallbackReply);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error replying to a mention from {User}", message.Author.Username);

            try
            {
                await ReplyInChannelAsync(message, ChatFallbackReply);
            }
            catch (Exception replyEx)
            {
                _logger.LogError(replyEx, "Failed to send the chat fallback reply");
            }
        }
    }

    /// <summary>
    /// Stamps the cooldown and returns false if the user replied to too recently.
    /// </summary>
    private bool TryStartChatCooldown(ulong userId)
    {
        var now = DateTime.UtcNow;
        var allowed = false;

        _lastChatReplyByUser.AddOrUpdate(
            userId,
            _ =>
            {
                allowed = true;
                return now;
            },
            (_, lastReply) =>
            {
                if (now - lastReply < ChatCooldown)
                    return lastReply;

                allowed = true;
                return now;
            }
        );

        return allowed;
    }

    private static async Task ReplyInChannelAsync(SocketUserMessage message, string content)
    {
        var text = content.Length > DiscordMessageLimit ? content[..DiscordMessageLimit] : content;

        await message.Channel.SendMessageAsync(text, messageReference: new MessageReference(message.Id), allowedMentions: AllowedMentions.None);
    }

    [GeneratedRegex(@"<@!?\d+>")]
    private static partial Regex MentionPattern();

    private async Task InteractionCreatedAsync(SocketInteraction interaction)
    {
        switch (interaction)
        {
            case SocketAutocompleteInteraction autocomplete:
                await HandleRealmAutocompleteAsync(autocomplete);
                break;
            case SocketSlashCommand slashCommand when slashCommand.Data.Name == AddPlayerCommandName:
                await HandleAddPlayerCommandAsync(slashCommand);
                break;
            case SocketMessageComponent component when component.Data.CustomId.StartsWith("manualadd:"):
                await HandleManualAddComponentAsync(component);
                break;
            case SocketMessageComponent component:
                await HandleStatusComponentAsync(component);
                break;
        }
    }

    private static bool HasOfficerRole(SocketUser user) => user is SocketGuildUser guildUser && guildUser.Roles.Any(r => r.Id == OfficerRoleId);

    private async Task HandleRealmAutocompleteAsync(SocketAutocompleteInteraction interaction)
    {
        if (interaction.Data.CommandName != AddPlayerCommandName || interaction.Data.Current.Name != "realm")
            return;

        var term = interaction.Data.Current.Value?.ToString();
        var results = EuRealms.Search(term).Select(realm => new AutocompleteResult(realm.Name, realm.Name));

        await interaction.RespondAsync(results);
    }

    private async Task HandleAddPlayerCommandAsync(SocketSlashCommand command)
    {
        if (!HasOfficerRole(command.User))
        {
            await command.RespondAsync("You don't have permission to perform this action.", ephemeral: true);
            return;
        }

        // Raider.IO lookups exceed Discord's 3s response window.
        await command.DeferAsync(ephemeral: true);

        var characterInput = command.Data.Options.FirstOrDefault(o => o.Name == "character")?.Value?.ToString()?.Trim() ?? string.Empty;
        var realmInput = command.Data.Options.FirstOrDefault(o => o.Name == "realm")?.Value?.ToString()?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(characterInput) || characterInput.Any(c => !char.IsLetter(c)))
        {
            await command.FollowupAsync("Character name must contain letters only.", ephemeral: true);
            return;
        }

        var realm = EuRealms.Find(realmInput);
        if (realm == null)
        {
            await command.FollowupAsync($"Unknown EU realm: **{realmInput}**. Pick one from the suggestions.", ephemeral: true);
            return;
        }

        var characterName = char.ToUpperInvariant(characterInput[0]) + characterInput[1..].ToLowerInvariant();

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var apiClient = scope.ServiceProvider.GetRequiredService<SuperRecruiterApiClient>();
            var raiderIoService = scope.ServiceProvider.GetRequiredService<RaiderIOService>();

            var existing = await apiClient.GetPlayerByCharacterAsync(realm.Slug, characterName);
            if (existing != null)
            {
                await command.FollowupAsync(
                    $"**{existing.CharacterName}-{existing.Realm}** is already in the system with status **{existing.Status}**.\n{_frontendBaseUrl}/{realm.Slug}/{characterName}",
                    ephemeral: true
                );
                return;
            }

            var profile = await raiderIoService.GetCharacterProfileAsync("eu", realm.Slug, characterName);
            if (profile == null)
            {
                await command.FollowupAsync($"No Raider.IO profile found for **{characterName}-{realm.Name}**. Check the spelling and realm.", ephemeral: true);
                return;
            }

            var progress = PlayerSummaryHelper.GetCurrentExpansionProgressForDiscord(profile);

            var embed = new EmbedBuilder()
                .WithTitle($"{profile.Name}-{realm.Name}")
                .WithColor(new Color((uint)PlayerSummaryHelper.ClassColorFromClassName(profile.Class)))
                .WithThumbnailUrl(profile.Thumbnail_url)
                .AddField("Class / Spec", $"{profile.Class} | {profile.Active_spec_name}", inline: true)
                .AddField("Item level", $"{profile.Gear?.Item_level_equipped ?? 0:F0}", inline: true)
                .AddField("Progress", string.IsNullOrWhiteSpace(progress) ? "No raid data" : progress)
                .Build();

            var components = new ComponentBuilder()
                .WithButton("Add player", $"manualadd:confirm:{realm.Slug}:{characterName}", ButtonStyle.Success)
                .WithButton("Cancel", $"manualadd:cancel:{realm.Slug}:{characterName}", ButtonStyle.Secondary)
                .Build();

            await command.FollowupAsync("Is this the right character?", embed: embed, components: components, ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling /{Command} for {Character}-{Realm}", AddPlayerCommandName, characterName, realm.Slug);
            await command.FollowupAsync("An error occurred looking up that character.", ephemeral: true);
        }
    }

    private async Task HandleManualAddComponentAsync(SocketMessageComponent component)
    {
        if (!HasOfficerRole(component.User))
        {
            await component.RespondAsync("You don't have permission to perform this action.", ephemeral: true);
            return;
        }

        // Custom ID: "manualadd:{action}:{realmSlug}:{characterName}"
        var parts = component.Data.CustomId.Split(':');
        if (parts.Length != 4)
            return;

        var action = parts[1];
        var realmSlug = parts[2];
        var characterName = parts[3];

        if (action == "cancel")
        {
            await component.UpdateAsync(msg =>
            {
                msg.Content = "Cancelled.";
                msg.Embed = null;
                msg.Components = new ComponentBuilder().Build();
            });
            return;
        }

        if (action != "confirm")
            return;

        var realm = EuRealms.Find(realmSlug);
        if (realm == null)
        {
            await component.RespondAsync($"Unknown EU realm: **{realmSlug}**.", ephemeral: true);
            return;
        }

        await component.UpdateAsync(msg =>
        {
            msg.Content = $"Adding **{characterName}-{realm.Name}**...";
            msg.Embed = null;
            msg.Components = new ComponentBuilder().Build();
        });

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var raiderIoService = scope.ServiceProvider.GetRequiredService<RaiderIOService>();
            var ingestionService = scope.ServiceProvider.GetRequiredService<PlayerIngestionService>();

            var profile = await raiderIoService.GetCharacterProfileAsync("eu", realm.Slug, characterName);
            if (profile == null)
            {
                await component.ModifyOriginalResponseAsync(msg => msg.Content = $"No Raider.IO profile found for **{characterName}-{realm.Name}** anymore.");
                return;
            }

            var player = new Player
            {
                CharacterName = profile.Name,
                Class = profile.Class,
                Realm = realm.Name,
                ItemLevel = profile.Gear?.Item_level_equipped ?? 0,
                LastUpdated = DateTime.UtcNow,
                CharacterUrl = string.Empty,
                SpecsPlaying = profile.Active_spec_name,
                Bio = $"Manually added by {component.User.Username}.",
                Source = LfgSource.Manual,
            };

            var created = await ingestionService.ProcessPlayerAsync(player, CancellationToken.None);

            if (created == null)
            {
                await component.ModifyOriginalResponseAsync(msg => msg.Content = $"Could not add **{characterName}-{realm.Name}**.");
                return;
            }

            _logger.LogInformation("Player {Character}-{Realm} manually added by {User}", characterName, realm.Slug, component.User.Username);

            await component.ModifyOriginalResponseAsync(msg =>
                msg.Content = $"Added **{created.CharacterName}-{created.Realm}**.\n{_frontendBaseUrl}/{realm.Slug}/{created.CharacterName}"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error manually adding player {Character}-{Realm}", characterName, realmSlug);
            await component.ModifyOriginalResponseAsync(msg => msg.Content = "An error occurred while adding the player.");
        }
    }

    private async Task HandleStatusComponentAsync(SocketMessageComponent component)
    {
        if (!HasOfficerRole(component.User))
        {
            await component.RespondAsync("You don't have permission to perform this action.", ephemeral: true);
            return;
        }

        // Parse custom ID: "status:{action}:{realmSlug}:{characterName}"
        var parts = component.Data.CustomId.Split(':');
        if (parts.Length != 4 || parts[0] != "status")
            return;

        var action = parts[1];
        var realmSlug = parts[2];
        var characterName = parts[3];

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

            var updated = await apiClient.UpdatePlayerStatusByCharacterAsync(realmSlug, characterName, status.Value);
            if (updated != null)
            {
                var deleteMessage = status.Value == PlayerStatus.Declined || status.Value == PlayerStatus.Blacklisted;

                if (deleteMessage)
                {
                    await component.RespondAsync($"Player **{updated.CharacterName}-{updated.Realm}** marked as **{status.Value}** by {component.User.Mention}.", ephemeral: true);
                    await component.Message.DeleteAsync();
                }
                else
                {
                    await component.RespondAsync($"Player **{updated.CharacterName}-{updated.Realm}** marked as **{status.Value}** by {component.User.Mention}.", ephemeral: false);
                }

                _logger.LogInformation("Player {Character}-{Realm} status updated to {Status} by {User}", characterName, realmSlug, status.Value, component.User.Username);
            }
            else
            {
                await component.RespondAsync("Player not found.", ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Discord interaction for player {Character}-{Realm}", characterName, realmSlug);
            await component.RespondAsync("An error occurred processing your action.", ephemeral: true);
        }
    }
}
