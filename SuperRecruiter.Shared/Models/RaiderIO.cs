using System.Text.Json.Serialization;

namespace SuperRecruiter.Shared.Models;

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
    public string Thumbnail_url { get; set; } = string.Empty;
    public string Profile_url { get; set; } = string.Empty;
    public RaiderIOGear? Gear { get; set; }
    public Dictionary<string, RaidTier>? Raid_progression { get; set; }
    public List<string>? Raid_progression_summary { get; set; }
    public List<RaidTierCurve>? Raid_achievement_curve { get; set; }
}

public class RaiderIOGear
{
    public double Item_level_equipped { get; set; }
    public double Item_level_total { get; set; }
}

public class RaidTier
{
    public string Summary { get; set; } = string.Empty;
    public int Total_bosses { get; set; }
    public int Normal_bosses_killed { get; set; }
    public int Heroic_bosses_killed { get; set; }
    public int Mythic_bosses_killed { get; set; }
}

public class RaidTierCurve
{
    public string Raid { get; set; } = string.Empty;
    public DateTime? Aotc { get; set; }
    public DateTime? Cutting_edge { get; set; }
}

public class RaiderIoRecruitmentFeed
{
    [JsonPropertyName("matches")]
    public List<Match>? Matches { get; set; }
}

public class Match
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("data")]
    public RaiderIoData? Data { get; set; }
}

public class RaiderIoData
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("level")]
    public int? Level { get; set; }

    [JsonPropertyName("faction")]
    public int? Faction { get; set; }

    [JsonPropertyName("achievementPoints")]
    public int? AchievementPoints { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("itemLevelEquipped")]
    public double? ItemLevelEquipped { get; set; } = 0.0;

    [JsonPropertyName("recruitment")]
    public Recruitment? Recruitment { get; set; }

    [JsonPropertyName("realm")]
    public RaiderIoRealm? Realm { get; set; }

    [JsonPropertyName("class")]
    public RaiderIoClass? Class { get; set; }
}

public class RaiderIoClass
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class RaiderIoRealm
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }
}

public class Recruitment
{
    [JsonPropertyName("guild_raids")]
    public GuildRaids? GuildRaids { get; set; }
}

public class GuildRaids
{
    [JsonPropertyName("profile")]
    public Profile? Profile { get; set; }

    [JsonPropertyName("additional_character_count")]
    public int? AdditionalCharacterCount { get; set; }
}

public class Profile
{
    [JsonPropertyName("published_at")]
    public DateTime? PublishedAt { get; set; }
}

public class RaiderIOLfg
{
    [JsonPropertyName("matches")]
    public List<LfgMatch>? Matches { get; set; }
}

public class LfgMatch
{
    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("data")]
    public LfgData Data { get; set; }
}

public class LfgData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("wowCharacterId")]
    public int WowCharacterId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("achievementPoints")]
    public int AchievementPoints { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; }

    [JsonPropertyName("itemLevelEquipped")]
    public double ItemLevelEquipped { get; set; }

    [JsonPropertyName("recruitment")]
    public LfgRecruitment Recruitment { get; set; }

    [JsonPropertyName("realm")]
    public LfgRealm Realm { get; set; }

    [JsonPropertyName("class")]
    public LfgClass Class { get; set; }

    [JsonPropertyName("spec")]
    public LfgSpec Spec { get; set; }
}

public class LfgSpec
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("slug")]
    public string Slug { get; set; }
}

public class LfgGuildRaids
{
    [JsonPropertyName("profile")]
    public LfgProfile Profile { get; set; }

    [JsonPropertyName("additional_character_count")]
    public int AdditionalCharacterCount { get; set; }
}

public class LfgProfile
{
    [JsonPropertyName("throttled_published_at")]
    public DateTime ThrottledPublishedAt { get; set; }

    [JsonPropertyName("caption")]
    public string Caption { get; set; }

    [JsonPropertyName("languages")]
    public List<int> Languages { get; set; }
}

public class LfgRealm
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("slug")]
    public string Slug { get; set; }

    [JsonPropertyName("altSlug")]
    public string AltSlug { get; set; }
}

public class LfgRecruitment
{
    [JsonPropertyName("guild_raids")]
    public LfgGuildRaids GuildRaids { get; set; }
}

public class LfgClass
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("slug")]
    public string Slug { get; set; }
}
