using System.Text.Json;
using SuperRecruiter.Models;

namespace SuperRecruiter.Services;

public interface IDiscordWebhookService
{
    Task SendNewPlayersNotificationAsync(
        List<Player> newPlayers,
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

        try
        {
            var embeds = new List<object>();
            foreach (var p in newPlayers)
            {
                var raiderIoUrl =
                    $"https://raider.io/characters/eu/{p.RealmSlug}/{p.CharacterName}";
                var warcraftLogsUrl =
                    $"https://www.warcraftlogs.com/character/eu/{p.RealmSlug}/{p.CharacterName}";
                embeds.Add(
                    new
                    {
                        title = $"{p.CharacterName} {p.Class}",
                        description = $"**Item Level:** {p.ItemLevel:F2}\n**Realm:** {p.Realm}\n**[Raider.IO]({raiderIoUrl})** | **[Warcraft Logs]({warcraftLogsUrl})**",
                        url = p.CharacterUrl,
                        color = 3447003, // Blue color
                        footer = new { text = $"Last updated: {p.LastUpdated:g}" },
                    }
                );
            }

            var payload = new
            {
                content = $"🔔 **{newPlayers.Count} new player(s) looking for guild!**",
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
