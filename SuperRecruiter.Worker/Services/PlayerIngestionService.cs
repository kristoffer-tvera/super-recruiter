using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Shared.Helpers;
using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Worker.Services;

/// <summary>
/// Turns a discovered (or manually supplied) player into an enriched Discord post and API record.
/// Shared by the scraper loop and the Discord bot's manual add command.
/// </summary>
public class PlayerIngestionService(
    ILogger<PlayerIngestionService> logger,
    WowProgressService wowProgressService,
    RaiderIOService raiderIOService,
    WarcraftLogsService warcraftLogsService,
    SuperRecruiterApiClient apiClient,
    AdminFilterService adminFilterService,
    DiscordBotService discordBotService
)
{
    public async Task<PlayerResponse?> ProcessPlayerAsync(Player player, CancellationToken cancellationToken)
    {
        var hasEmptyNameOrRealm = string.IsNullOrWhiteSpace(player.CharacterName) || string.IsNullOrWhiteSpace(player.Realm);
        var nameIsNotOnlyLetters = player.CharacterName != null && player.CharacterName.Any(c => !char.IsLetter(c));

        if (hasEmptyNameOrRealm || nameIsNotOnlyLetters)
        {
            logger.LogInformation("Skipping player with invalid name or realm: '{CharacterName}' on '{Realm}'", player.CharacterName, player.Realm);
            return null;
        }

        var (detailedPlayer, raiderIoData, warcraftLogsData) = await EnrichPlayerAsync(player, cancellationToken);

        // Manually added players are vouched for by an officer, so the language filter doesn't apply.
        if (player.Source != LfgSource.Manual && ShouldSkipForLanguage(player))
        {
            return null;
        }

        var (raiderIoSummary, warcraftLogsSummary) = BuildSummaries(raiderIoData, warcraftLogsData);
        var createRequest = BuildCreatePlayerRequest(detailedPlayer, raiderIoSummary, warcraftLogsSummary);

        createRequest.CurrentTierMythicKillCount = raiderIoData?.Raid_progression?.Sum(raid => raid.Value.Mythic_bosses_killed) ?? 0;

        // Manual adds are vouched for by an officer, so they bypass the admin filters.
        var shouldPost =
            player.Source == LfgSource.Manual || await adminFilterService.ShouldPostToDiscordAsync(detailedPlayer, createRequest.CurrentTierMythicKillCount, cancellationToken);

        var messageId = shouldPost ? await discordBotService.SendPlayerMessageAsync(detailedPlayer, raiderIoData) : null;

        if (messageId.HasValue)
        {
            createRequest.DiscordMessageId = messageId.Value;
        }

        var apiPlayer = await apiClient.CreatePlayerAsync(createRequest);

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
                ]
            );
        }

        logger.LogInformation("Successfully processed player {Character}-{Realm}", detailedPlayer.CharacterName, detailedPlayer.Realm);

        return apiPlayer;
    }

    private async Task<(Player DetailedPlayer, RaiderIOProfile? RaiderIoData, WarcraftLogsCharacterResponse? WarcraftLogsData)> EnrichPlayerAsync(
        Player player,
        CancellationToken cancellationToken
    )
    {
        var raiderIoData = await raiderIOService.GetCharacterProfileAsync("eu", player.RealmSlug, player.CharacterName, cancellationToken);

        var warcraftLogsData = await warcraftLogsService.GetCharacterDataAsync(player, cancellationToken);

        var detailedPlayer = player.Source == LfgSource.WoWProgress ? await wowProgressService.GetPlayerDetailsAsync(player, cancellationToken) : player;

        return (detailedPlayer, raiderIoData, warcraftLogsData);
    }

    private bool ShouldSkipForLanguage(Player player)
    {
        if (string.IsNullOrWhiteSpace(player.Languages))
        {
            return false;
        }

        if (player.Languages.ToLower().Contains("eng"))
        {
            return false;
        }

        logger.LogInformation("Player {Character}-{Realm} does not speak English. Skipping.", player.CharacterName, player.Realm);
        return true;
    }

    private static (string RaiderIoSummary, string? WarcraftLogsSummary) BuildSummaries(RaiderIOProfile? raiderIoData, WarcraftLogsCharacterResponse? warcraftLogsData)
    {
        var raiderIoSummary = string.Join(
            "\n\n",
            [PlayerSummaryHelper.GetCurrentExpansionProgressionSummary(raiderIoData), PlayerSummaryHelper.GetCuttingEdgeSummary(raiderIoData)]
        );

        var warcraftLogsZoneRankings = warcraftLogsData?.Data?.CharacterData?.Character?.ZoneRankings;

        if (warcraftLogsZoneRankings == null)
        {
            return (raiderIoSummary, null);
        }

        var warcraftLogsSummary = string.Join(
            "\n\n",
            [PlayerSummaryHelper.GetAllStarsSummary(warcraftLogsZoneRankings), PlayerSummaryHelper.GetBossSummary(warcraftLogsZoneRankings)]
        );

        return (raiderIoSummary, warcraftLogsSummary);
    }

    private static CreatePlayerRequest BuildCreatePlayerRequest(Player detailedPlayer, string raiderIoSummary, string? warcraftLogsSummary)
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
            RaiderIoSummary = raiderIoSummary,
            WarcraftLogsSummary = warcraftLogsSummary,
        };
    }
}
