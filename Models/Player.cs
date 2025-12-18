namespace SuperRecruiter.Models;

public class Player
{
    public string Race { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public string? Guild { get; set; }
    public string Realm { get; set; } = string.Empty;
    public double ItemLevel { get; set; }
    public DateTime LastUpdated { get; set; }
    public string CharacterUrl { get; set; } = string.Empty;

    public override string ToString()
    {
        return $"{Race} {Class} ({ItemLevel:F2}) - {Realm}"
            + (Guild != null ? $" [{Guild}]" : "")
            + $" - Updated: {LastUpdated:g}";
    }
}
