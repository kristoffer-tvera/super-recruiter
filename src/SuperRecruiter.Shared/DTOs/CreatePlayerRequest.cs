using System.Text.Json.Serialization;

namespace SuperRecruiter.Shared.DTOs;

public class CreatePlayerRequest
{
    public string CharacterName { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public string Realm { get; set; } = string.Empty;
    public string RealmSlug { get; set; } = string.Empty;
    public double ItemLevel { get; set; }
    public DateTime LastUpdated { get; set; }
    public string CharacterUrl { get; set; } = string.Empty;
    public string? BattleTag { get; set; }
    public string? Bio { get; set; }
    public string? Languages { get; set; }
    public string? SpecsPlaying { get; set; }
    public List<string> GuildHistory { get; set; } = [];

    // Enrichment data stored as JSON
    public string? RaiderIoDataJson { get; set; }
    public string? WarcraftLogsDataJson { get; set; }
    public string? GeminiTake { get; set; }

    // Discord tracking
    public ulong? DiscordMessageId { get; set; }
    public ulong? DiscordChannelId { get; set; }
}
