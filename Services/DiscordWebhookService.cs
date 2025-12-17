using System.Text.Json;
using super_recruiter.Models;

namespace super_recruiter.Services;

public interface IDiscordWebhookService
{
    Task SendNewPlayersNotificationAsync(
        List<Player> newPlayers,
        CancellationToken cancellationToken = default
    );
}

public class DiscordWebhookService : IDiscordWebhookService
{
    private readonly ILogger<DiscordWebhookService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string? _webhookUrl;

    public DiscordWebhookService(
        HttpClient httpClient,
        ILogger<DiscordWebhookService> logger,
        IConfiguration configuration
    )
    {
        _httpClient = httpClient;
        _logger = logger;
        _webhookUrl = configuration["Discord:WebhookUrl"];
    }

    public async Task SendNewPlayersNotificationAsync(
        List<Player> newPlayers,
        CancellationToken cancellationToken = default
    )
    {
        if (newPlayers.Count == 0)
        {
            _logger.LogDebug("No new players to notify about");
            return;
        }

        // Mock implementation for now
        _logger.LogInformation("=== MOCK DISCORD WEBHOOK ===");
        _logger.LogInformation(
            "Would send notification for {Count} new player(s) looking for guild:",
            newPlayers.Count
        );

        foreach (var player in newPlayers)
        {
            _logger.LogInformation("  - {Player}", player.ToString());
        }

        // Uncomment when ready to actually send to Discord
        /*
        if (string.IsNullOrWhiteSpace(_webhookUrl))
        {
            _logger.LogWarning("Discord webhook URL not configured");
            return;
        }

        try
        {
            var embeds = newPlayers.Select(p => new
            {
                title = $"{p.Race} {p.Class}",
                description = $"**Item Level:** {p.ItemLevel:F2}\n**Realm:** {p.Realm}\n**Guild:** {p.Guild ?? "None"}",
                url = p.CharacterUrl,
                color = 3447003, // Blue color
                footer = new { text = $"Last updated: {p.LastUpdated:g}" }
            }).ToArray();

            var payload = new
            {
                content = $"🔔 **{newPlayers.Count} new player(s) looking for guild!**",
                embeds
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync(_webhookUrl, content, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            _logger.LogInformation("Successfully sent Discord notification");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending Discord webhook notification");
        }
        */

        _logger.LogInformation("=== END MOCK ===");
    }
}
