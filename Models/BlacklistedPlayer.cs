using Dapper.Contrib.Extensions;

namespace SuperRecruiter.Models;

[Table("blacklisted_players")]
public class BlacklistedPlayer
{
    [Key]
    public int Id { get; set; }

    [ExplicitKey]
    public string CharacterName { get; set; } = string.Empty;

    [ExplicitKey]
    public string Realm { get; set; } = string.Empty;

    public string? Reason { get; set; }

    public DateTime BlacklistedAt { get; set; } = DateTime.UtcNow;
}
