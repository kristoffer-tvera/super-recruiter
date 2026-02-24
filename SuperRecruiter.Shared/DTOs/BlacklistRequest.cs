namespace SuperRecruiter.Shared.DTOs;

public class BlacklistRequest
{
    public string CharacterName { get; set; } = string.Empty;
    public string Realm { get; set; } = string.Empty;
    public string? Reason { get; set; }
}
