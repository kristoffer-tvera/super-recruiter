namespace SuperRecruiter.Shared.Models;

using SuperRecruiter.Shared.Helpers;

public class Player
{
    public string CharacterName { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public string Realm { get; set; } = string.Empty;
    public string RealmSlug => RealmSlugHelper.ToSlug(Realm);
    public double ItemLevel { get; set; }
    public DateTime LastUpdated { get; set; }
    public string CharacterUrl { get; set; } = string.Empty;

    public string? BattleTag { get; set; }
    public string? Bio { get; set; }
    public string? Languages { get; set; }
    public string? SpecsPlaying { get; set; }

    public string[] GuildHistory { get; set; } = [];

    public LfgSource Source { get; set; }

    public override string ToString()
    {
        return $"{CharacterName} - {Class} ({ItemLevel:F2}) - {Realm}" + $" - Updated: {LastUpdated:g}";
    }
}

public enum LfgSource
{
    WoWProgress,
    RaiderIO,
    Manual,
}
