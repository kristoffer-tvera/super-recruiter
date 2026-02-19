using System.Text.Json;
using SuperRecruiter.Models;

namespace SuperRecruiter.Services;

/// <summary>
/// https://discord.com/developers/docs/components/reference
/// </summary>
/// <param name="httpClient"></param>
/// <param name="logger"></param>
/// <param name="configuration"></param>
public class DiscordWebhookService(
    HttpClient httpClient,
    ILogger<DiscordWebhookService> logger,
    IConfiguration configuration
)
{
    private readonly string? _webhookUrl = configuration["Discord:WebhookUrl"];

    public async Task SendPlayerWasFilteredOutNotificationAsync(
        Player player,
        string reason,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(_webhookUrl))
        {
            logger.LogWarning("Discord webhook URL not configured");
            return;
        }

        var payload = new
        {
            content = $"Player **{player.CharacterName}-{player.Realm}** was filtered out: {reason}",
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(_webhookUrl, content, cancellationToken);

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

        logger.LogInformation(
            "Sent Discord notification for filtered out player: {Player} - Reason: {Reason}",
            player,
            reason
        );
    }

    public async Task SendNewPlayerNotificationAsync(
        Player player,
        RaiderIOProfile? raiderIoProfile = null,
        WarcraftLogsCharacterResponse? warcraftLogsData = null,
        string geminiTake = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var thumbnail = raiderIoProfile?.Thumbnail_url ?? "";
            var links = new List<string>
            {
                $"[Armory](https://worldofwarcraft.blizzard.com/en-gb/character/eu/{player.RealmSlug}/{player.CharacterName})",
                raiderIoProfile != null
                    ? $"[RaiderIO]({raiderIoProfile.Profile_url})"
                    : "RaiderIO (no data)",
                $"[WoWProgress]({player.CharacterUrl})",
                $"[WCL](https://www.warcraftlogs.com/character/eu/{player.RealmSlug}/{player.CharacterName})",
            };

            var warcraftLogsZoneRankings = warcraftLogsData
                ?.Data
                ?.CharacterData
                .Character
                .ZoneRankings;

            var personContainer = new
            {
                type = 17, // ComponentType.CONTAINER
                accent_color = ClassColorFromClassName(player.Class),
                components = new[]
                {
                    new
                    {
                        type = 10, // Text Display
                        content = $"# **{player.CharacterName}-{player.Realm}** | {player.Class} |  {player.ItemLevel}",
                    },
                    new
                    {
                        type = 10, // Text Display
                        content = player.Bio != null
                            ? $"{player.Bio[..Math.Min(player.Bio.Length, 3700)]}\n\n"
                            : "No bio available\n\n",
                    },
                    new
                    {
                        type = 10, // Text Display
                        content = $"### Languages: {player.Languages} | Specs: {player.SpecsPlaying}",
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
                content = GetCurrentExpansionProgressionSummary(raiderIoProfile),
            };

            var wclAllstars = new
            {
                type = 10, // Text Display
                content = GetAllStarsSummary(warcraftLogsZoneRankings),
            };

            var wclBosses = new
            {
                type = 10, // Text Display
                content = GetBossSummary(warcraftLogsZoneRankings),
            };

            var aotc = new
            {
                type = 10, // Text Display
                content = GetCuttingEdgeSummary(raiderIoProfile),
            };

            var guildHistory = new
            {
                type = 10, // Text Display
                content = player.GuildHistory.Any()
                    ? $"## Guild History:\n- {string.Join("\n- ", player.GuildHistory)}"
                    : "No guild history available",
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
                new
                {
                    type = 14, // ComponentType.SEPARATOR
                    divider = true,
                    spacing = 2,
                },
                guildHistory,
            };

            if (!string.IsNullOrEmpty(geminiTake))
            {
                components.Add(
                    new
                    {
                        type = 14, // ComponentType.SEPARATOR
                        divider = true,
                        spacing = 2,
                    }
                );
                components.Add(
                    new
                    {
                        type = 10, // Text Display
                        content = geminiTake,
                    }
                );
            }

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

    private static int ClassColorFromClassName(string className)
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

    public static string GetBossSummary(ZoneRankings? warcraftLogsZoneRankings)
    {
        var header = "## __WarcraftLogs - Boss Rankings__:\n- ";
        var rankings =
            warcraftLogsZoneRankings != null
                ? string.Join(
                    "\n- ",
                    warcraftLogsZoneRankings
                        .Rankings.Where(rank => rank.TotalKills > 0)
                        .Select(rank =>
                            $"**{rank.Encounter.Name}** ({rank.TotalKills}) | Best: {rank.RankPercent:F0}% | Median: {rank.MedianPercent:F0}% | Fastest kill: {rank.FastestKillFormatted}"
                        )
                )
                : "No WarcraftLogs data";
        return header + rankings;
    }

    public static string GetAllStarsSummary(ZoneRankings? zoneRankings)
    {
        var header = "## __WarcraftLogs - Allstars__:";

        if (zoneRankings == null || zoneRankings.AllStars == null)
            return $"{header}\n- No WarcraftLogs data";

        var best =
            $"\n- **Best** Perf. Avg {zoneRankings.BestPerformanceAverage:F0}% |  **Median** Perf. Avg {zoneRankings.MedianPerformanceAverage:F0}%";

        var allStars = zoneRankings
            .AllStars.Select(a =>
                $"**{a.Spec}** | {a.RankPercent:F0}% | ({a.Points:F0} out of {a.PossiblePoints:F0})"
            )
            .ToList();

        return header + best + "\n- " + string.Join("\n- ", allStars);
    }

    public static string GetCurrentExpansionProgressionSummary(RaiderIOProfile? profile)
    {
        var header = "## __Current Expansion Progression__:";

        if (profile?.Raid_progression_summary == null)
            return $"{header}\n- No raid data";

        var progression = string.Join("\n- ", profile.Raid_progression_summary);
        return $"{header}\n- {progression}";
    }

    public static string GetCuttingEdgeSummary(RaiderIOProfile? profile)
    {
        var header = "## __Ahead of the Curve / Cutting Edge__:";

        if (profile?.Raid_achievement_curve == null)
            return $"{header}\n- No RaiderIO data";

        var curve = string.Join(
            "\n- ",
            profile.Raid_achievement_curve.Select(tier =>
                $"**{tier.Raid}** | {(tier.Cutting_edge != null ? "Mythic | " + tier.Cutting_edge.Value.ToString("dd.MM.yyyy") : tier.Aotc != null ? "Heroic | " + tier.Aotc.Value.ToString("dd.MM.yyyy") : "Uncleared")}"
            )
        );

        return $"{header}\n- {curve}";
    }
}
