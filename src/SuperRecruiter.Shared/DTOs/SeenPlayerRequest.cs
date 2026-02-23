namespace SuperRecruiter.Shared.DTOs;

public class SeenPlayerRequest
{
    public string CharacterName { get; set; } = string.Empty;
    public string Realm { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }
}
