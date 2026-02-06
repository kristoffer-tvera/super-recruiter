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

            var rankings = warcraftLogsData?.Data?.CharacterData.Character.ZoneRankings;

            var embeds = new List<object>
            {
                new
                {
                    title = $"**{newPlayer.CharacterName}-{newPlayer.Realm}** | {newPlayer.Class} |  {newPlayer.ItemLevel}",
                    description = $"Bio here",
                    // url = newPlayer.CharacterUrl,
                    // Blue color
                    footer = new { text = $"Last updated: {newPlayer.LastUpdated:g}" },
                    thumbnail = new { url = thumbnail },
                    fields = new[]
                    {
                        new
                        {
                            name = "Warcraftlogs - Allstars",
                            value = rankings != null
                                ? string.Join(
                                    "\n",
                                    rankings.AllStars.Select(r =>
                                        $"__{r.Spec}__ | {r.RankPercent:F0}% | ({r.Points:F0} out of {r.PossiblePoints:F0})"
                                    )
                                )
                                : "No WarcraftLogs data",
                            inline = false,
                        },
                        new
                        {
                            name = "Warcraftlogs - All bosses (Current tier)",
                            value = rankings != null
                                ? string.Join(
                                    "\n\n",
                                    rankings.Rankings.Select(rank =>
                                        $"{rank.Encounter.Name} ({rank.TotalKills}) \nBest: {rank.RankPercent:F0}% | Median: {rank.MedianPercent:F0}%"
                                    )
                                )
                                : "No WarcraftLogs data",
                            inline = false,
                        },
                        new
                        {
                            name = "Ahead of the Curve, Cutting Edge",
                            value = raiderIoProfile?.Raid_achievement_curve != null
                                ? string.Join(
                                    "\n",
                                    raiderIoProfile.Raid_achievement_curve.Select(tier =>
                                        // $"{tier.Raid} \nHeroic: {tier.Aotc.ToString("dd.MM.yyyy") ?? "N/A"} | Mythic: {tier.Cutting_edge.ToString("dd.MM.yyyy") ?? "N/A"}"
                                        $"{tier.Raid} | M: {tier.Cutting_edge.ToString("dd.MM.yyyy") ?? "N/A"}"
                                    )
                                )
                                : "No raid data",
                            inline = false,
                        },
                        new
                        {
                            name = "Current Expansion",
                            value = raiderIoProfile?.Raid_progression_summary != null
                                ? string.Join("\n", raiderIoProfile.Raid_progression_summary)
                                : "No raid data",
                            inline = false,
                        },
                        new
                        {
                            name = "External Sites",
                            value = string.Join(" | ", links),
                            inline = false,
                        },
                    },
                },
            };

            var payload = new
            {
                content = $"**{newPlayer.CharacterName}-{newPlayer.Realm}** | {newPlayer.Class} |  {newPlayer.ItemLevel}",
                tts = false,
                avatar_url = thumbnail,
                embeds,
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

            logger.LogInformation("Successfully sent Discord notification");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending Discord webhook notification");
        }
    }
}
