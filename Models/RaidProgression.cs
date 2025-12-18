namespace SuperRecruiter.Models;

public class RaiderIOProfile
{
    public string Name { get; set; } = string.Empty;
    public string Race { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public string Active_spec_name { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string Faction { get; set; } = string.Empty;
    public int Achievement_points { get; set; }
    public string Realm { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Profile_url { get; set; } = string.Empty;
    public Dictionary<string, RaidTier>? Raid_progression { get; set; }
}

public class RaidTier
{
    public string Summary { get; set; } = string.Empty;
    public int Total_bosses { get; set; }
    public int Normal_bosses_killed { get; set; }
    public int Heroic_bosses_killed { get; set; }
    public int Mythic_bosses_killed { get; set; }
}
