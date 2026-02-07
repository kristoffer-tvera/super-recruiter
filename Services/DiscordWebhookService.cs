using System.Text.Json;
using SuperRecruiter.Models;

namespace SuperRecruiter.Services;

public interface IDiscordWebhookService
{
    Task SendNewPlayersNotificationAsync(
        List<Player> newPlayers,
        CancellationToken cancellationToken = default
    );

    Task SendNewPlayerNotificationAsync(
        Player newPlayer,
        RaiderIOProfile? raiderIoProfile = null,
        WarcraftLogsCharacterResponse? warcraftLogsData = null,
        CancellationToken cancellationToken = default
    );
}

public class DiscordWebhookService(
    HttpClient httpClient,
    ILogger<DiscordWebhookService> logger,
    IConfiguration configuration
) : IDiscordWebhookService
{
    private readonly string? _webhookUrl = configuration["Discord:WebhookUrl"];

    public async Task SendNewPlayersNotificationAsync(
        List<Player> newPlayers,
        CancellationToken cancellationToken = default
    )
    {
        if (newPlayers.Count == 0)
        {
            logger.LogDebug("No new players to notify about");
            return;
        }

        if (string.IsNullOrWhiteSpace(_webhookUrl))
        {
            logger.LogWarning("Discord webhook URL not configured");
            return;
        }
        foreach (var p in newPlayers)
        {
            await SendNewPlayerNotificationAsync(p, null, null, cancellationToken);
        }
    }

    public async Task SendNewPlayerNotificationAsync(
        Player newPlayer,
        RaiderIOProfile? raiderIoProfile = null,
        WarcraftLogsCharacterResponse? warcraftLogsData = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var thumbnail = raiderIoProfile?.Thumbnail_url ?? "";
            var links = new List<string>
            {
                $"[Armory](https://worldofwarcraft.blizzard.com/en-gb/character/eu/{newPlayer.RealmSlug}/{newPlayer.CharacterName})",
                raiderIoProfile != null
                    ? $"[RaiderIO]({raiderIoProfile.Profile_url})"
                    : "RaiderIO (no data)",
                $"[WoWProgress](https://www.wowprogress.com/character/eu/{newPlayer.RealmSlug}/{newPlayer.CharacterName})",
                $"[WCL](https://www.warcraftlogs.com/character/eu/{newPlayer.RealmSlug}/{newPlayer.CharacterName})",
            };

            var warcraftLogsZoneRankings = warcraftLogsData
                ?.Data
                ?.CharacterData
                .Character
                .ZoneRankings;

            var personContainer = new
            {
                type = 17, // ComponentType.CONTAINER
                accent_color = ClassColorFromClassName(newPlayer.Class),
                components = new[]
                {
                    new
                    {
                        type = 10, // Text Display
                        content = $"# **{newPlayer.CharacterName}-{newPlayer.Realm}** | {newPlayer.Class} |  {newPlayer.ItemLevel}",
                    },
                    new
                    {
                        type = 10, // Text Display
                        content = newPlayer.Bio != null
                            ? $"{newPlayer.Bio}\n\n"
                            : "No bio available\n\n",
                    },
                    new
                    {
                        type = 10, // Text Display
                        content = $"## External Links:\n{string.Join(" | ", links)}",
                    },
                },
            };

            var currentExpansionProgression = new
            {
                type = 10, // Text Display
                content = $"## __Current Expansion Progression__:\n- {(raiderIoProfile?.Raid_progression_summary != null ? string.Join("\n- ", raiderIoProfile.Raid_progression_summary) : "No raid data")}",
            };

            var wclAllstars = new
            {
                type = 10, // Text Display
                content = $"## __WarcraftLogs - Allstars__:\n- {(warcraftLogsZoneRankings != null ? string.Join("\n- ", warcraftLogsZoneRankings.AllStars.Select(r => $"**{r.Spec}** | {r.RankPercent:F0}% | ({r.Points:F0} out of {r.PossiblePoints:F0})")) : "No WarcraftLogs data")}",
            };

            var wclBosses = new
            {
                type = 10, // Text Display
                content = $"## __WarcraftLogs - Bosses current tier__:\n- {(warcraftLogsZoneRankings != null ? string.Join("\n- ", warcraftLogsZoneRankings.Rankings.Select(rank => $"**{rank.Encounter.Name}** as {rank.Spec} | Best: {rank.RankPercent:F0}% | Median: {rank.MedianPercent:F0}% | Fastest kill: {rank.FastestKillFormatted}")) : "No WarcraftLogs data")}",
            };

            var aotc = new
            {
                type = 10, // Text Display
                content = $"## __Ahead of the Curve, Cutting Edge__:\n- {(raiderIoProfile?.Raid_achievement_curve != null ? string.Join("\n- ", raiderIoProfile.Raid_achievement_curve.Select(tier => $"**{tier.Raid}** | {(tier.Cutting_edge != null ? "Mythic | " + tier.Cutting_edge.Value.ToString("dd.MM.yyyy") : tier.Aotc != null ? "Heroic | " + tier.Aotc.Value.ToString("dd.MM.yyyy") : "Uncleared")}")) : "No RaiderIO data")}",
            };

            var components = new List<object>
            {
                personContainer,
                new
                {
                    type = 14, // ComponentType.SEPARATOR
                    divider = true,
                    spacing = 2,
                },
                currentExpansionProgression,
                new
                {
                    type = 14, // ComponentType.SEPARATOR
                    divider = true,
                    spacing = 2,
                },
                wclAllstars,
                wclBosses,
                new
                {
                    type = 14, // ComponentType.SEPARATOR
                    divider = true,
                    spacing = 2,
                },
                aotc,
            };

            var payload = new
            {
                tts = false,
                avatar_url = thumbnail,
                flags = 32768,
                components,
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(
                _webhookUrl + "?with_components=true",
                content,
                cancellationToken
            );

            if (!response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError(
                    "Discord webhook request failed with status {StatusCode}: {ResponseContent}",
                    response.StatusCode,
                    responseContent
                );
                response.EnsureSuccessStatusCode();
            }

            logger.LogInformation("Successfully sent Discord notification");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending Discord webhook notification");
        }
    }

    private int ClassColorFromClassName(string className)
    {
        return className.ToLower() switch
        {
            "death knight" => 0xC41F3B,
            "demon hunter" => 0xA330C9,
            "druid" => 0xFF7D0A,
            "evoker" => 0x33937F,
            "hunter" => 0xABD473,
            "mage" => 0x69CCF0,
            "monk" => 0x00FF96,
            "paladin" => 0xF58CBA,
            "priest" => 0xFFFFFF,
            "rogue" => 0xFFF569,
            "shaman" => 0x0070DE,
            "warlock" => 0x9482C9,
            "warrior" => 0xC79C6E,
            _ => 0xFFFFFF, // Default to white if unknown
        };
    }
}
