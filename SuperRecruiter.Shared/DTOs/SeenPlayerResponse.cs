namespace SuperRecruiter.Shared.DTOs;

public class SeenPlayerResponse
{
    public string CharacterName { get; set; } = string.Empty;
    public string Realm { get; set; } = string.Empty;
    public DateTime LastSeenAt { get; set; }
}
