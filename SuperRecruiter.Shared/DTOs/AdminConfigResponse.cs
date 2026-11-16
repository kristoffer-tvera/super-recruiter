namespace SuperRecruiter.Shared.DTOs;

public class AdminConfigResponse
{
    public int BossKills { get; set; }

    /// <summary>Raider.IO raid slug whose Mythic kills are tracked; empty falls back to Raider.IO's current-tier selector.</summary>
    public string TrackedRaid { get; set; } = string.Empty;
    public List<string> AcceptedClasses { get; set; } = [];
}
