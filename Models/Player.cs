namespace SuperRecruiter.Models;

public class Player
{
    public string CharacterName { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public string Realm { get; set; } = string.Empty;
    public double ItemLevel { get; set; }
    public DateTime LastUpdated { get; set; }
    public string CharacterUrl { get; set; } = string.Empty;

    public override string ToString()
    {
        return $"{CharacterName} - {Class} ({ItemLevel:F2}) - {Realm}"
            + $" - Updated: {LastUpdated:g}";
    }
}
