using Dapper.Contrib.Extensions;

namespace SuperRecruiter.Models;

[Table("seen_players")]
public class SeenPlayer
{
    [Key]
    public int Id { get; set; }

    [ExplicitKey]
    public string CharacterName { get; set; } = string.Empty;

    [ExplicitKey]
    public string Realm { get; set; } = string.Empty;

    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;

    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}
