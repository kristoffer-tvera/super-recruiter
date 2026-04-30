using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Shared.DTOs;

/// <summary>
/// Lightweight DTO for cache synchronization. Contains only essential fields
/// to minimize data transfer and database load.
/// </summary>
public class PlayerCacheResponse
{
    public int Id { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public string Realm { get; set; } = string.Empty;
    public PlayerStatus Status { get; set; }
    public DateTime UpdatedAt { get; set; }
}
