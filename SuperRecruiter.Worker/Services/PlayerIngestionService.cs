using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Worker.Services;

/// <summary>
/// Stores a lean discovered (or manually supplied) player and sends a Discord notification.
/// Shared by the scraper loop and the Discord bot's manual add command.
/// </summary>
public class PlayerIngestionService(
    ILogger<PlayerIngestionService> logger,
    RaiderIOService raiderIOService,
    SuperRecruiterApiClient apiClient,
    AdminFilterService adminFilterService,
    DiscordBotService discordBotService
)
{
    public async Task<PlayerResponse?> ProcessPlayerAsync(Player player, CancellationToken cancellationToken, RaiderIOProfile? discoveryProfile = null)
    {
        if (string.IsNullOrWhiteSpace(player.CharacterName) || string.IsNullOrWhiteSpace(player.Realm) || player.CharacterName.Any(character => !char.IsLetter(character)))
        {
            logger.LogInformation("Skipping player with invalid name or realm: '{CharacterName}' on '{Realm}'", player.CharacterName, player.Realm);
            return null;
        }

        // Manually added players are vouched for by an officer, so the language filter doesn't apply.
        if (player.Source != LfgSource.Manual && ShouldSkipForLanguage(player))
        {
            return null;
        }

        var detailedPlayer = player;
        var raiderIoData = discoveryProfile ?? await raiderIOService.GetCharacterProfileAsync("eu", player.RealmSlug, player.CharacterName, cancellationToken);

        var createRequest = BuildCreatePlayerRequest(detailedPlayer);
        createRequest.DiscoveryProgression = raiderIoData?.Raid_progression is { Count: > 0 } progression ? new DiscoveryProgression(DateTimeOffset.UtcNow, progression) : null;

        createRequest.CurrentTierMythicKillCount = raiderIoData?.Raid_progression?.Sum(raid => raid.Value.Mythic_bosses_killed) ?? 0;

        // Manual adds are vouched for by an officer, so they bypass the admin filters.
        var shouldPost =
            player.Source == LfgSource.Manual
            || await adminFilterService.ShouldPostToDiscordAsync(
                detailedPlayer,
                createRequest.DiscoveryProgression is null ? -1 : createRequest.CurrentTierMythicKillCount,
                cancellationToken
            );

        var messageId = shouldPost ? await discordBotService.SendPlayerMessageAsync(detailedPlayer, raiderIoData) : null;

        if (messageId.HasValue)
        {
            createRequest.DiscordMessageId = messageId.Value;
        }

        var apiPlayer = await apiClient.CreatePlayerAsync(createRequest, cancellationToken);

        // Manual adds bypass the scraper's seen-player bookkeeping, so record it here to avoid a duplicate post later.
        if (player.Source == LfgSource.Manual)
        {
            await apiClient.BulkAddSeenPlayersAsync(
                [
                    new SeenPlayerRequest
                    {
                        CharacterName = detailedPlayer.CharacterName,
                        Realm = detailedPlayer.Realm,
                        LastUpdated = detailedPlayer.LastUpdated,
                    },
                ],
                cancellationToken
            );
        }

        logger.LogInformation("Successfully processed player {Character}-{Realm}", detailedPlayer.CharacterName, detailedPlayer.Realm);

        return apiPlayer;
    }

    private bool ShouldSkipForLanguage(Player player)
    {
        if (string.IsNullOrWhiteSpace(player.Languages) || player.Languages.Equals("N/A", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (player.Languages.ToLower().Contains("eng"))
        {
            return false;
        }

        string[] knownNonEnglish =
        [
            "German",
            "Spanish",
            "French",
            "Italian",
            "Russian",
            "Norwegian",
            "Polish",
            "Swedish",
            "Turkish",
            "Danish",
            "Dutch",
            "Portuguese",
            "Finnish",
            "Greek",
            "Hungarian",
            "Czech",
            "Ukrainian",
            "Romanian",
        ];
        var languages = player.Languages.Split([',', ';', '/', '|', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!languages.Any(language => knownNonEnglish.Contains(language, StringComparer.OrdinalIgnoreCase)))
            return false;

        logger.LogInformation("Player {Character}-{Realm} does not speak English. Skipping.", player.CharacterName, player.Realm);
        return true;
    }

    private static CreatePlayerRequest BuildCreatePlayerRequest(Player detailedPlayer)
    {
        return new CreatePlayerRequest
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
        };
    }
}
