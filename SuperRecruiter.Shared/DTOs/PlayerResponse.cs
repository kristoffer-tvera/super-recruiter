using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Shared.DTOs;

public class PlayerResponse
{
    public int Id { get; set; }
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
    public string[] GuildHistory { get; set; } = [];
    public string? RaiderIoSummary { get; set; }
    public string? WarcraftLogsSummary { get; set; }
    public string? GeminiTake { get; set; }
    public PlayerStatus Status { get; set; }
    public ulong? DiscordMessageId { get; set; }
    public ulong? DiscordChannelId { get; set; }
    public int CurrentTierMythicKillCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
