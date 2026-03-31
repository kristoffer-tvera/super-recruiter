using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Worker.Services;

/// <summary>
/// In-memory cache for player and seen-player data.
/// Primed once per scan cycle from the API, then used for fast lookups
/// during filtering. Writes (AddSeenPlayer, AddOrUpdatePlayer) update
/// both the cache and the API so the cache stays consistent.
/// </summary>
public class PlayerCacheService(
    SuperRecruiterApiClient apiClient,
    ILogger<PlayerCacheService> logger
)
{
    // Key: "charactername-realm" (lowered)
    private Dictionary<string, DateTime> _seenPlayers = new();
    private Dictionary<string, PlayerResponse> _players = new();
    private bool _seenPlayersLoaded;

    private static string Key(string name, string realm) =>
        $"{name.ToLowerInvariant()}-{realm.ToLowerInvariant()}";

    /// <summary>
    /// Load seen-player data from the API once, and refresh player data
    /// only when <paramref name="refreshPlayers"/> is true.
    /// </summary>
    public async Task RefreshAsync(bool refreshPlayers)
    {
        var tasks = new List<Task>();

        Task<List<SeenPlayerResponse>>? seenTask = null;
        if (!_seenPlayersLoaded)
        {
            seenTask = apiClient.GetAllSeenPlayersAsync();
            tasks.Add(seenTask);
        }

        Task<List<PlayerResponse>>? playersTask = null;
        if (refreshPlayers)
        {
            playersTask = apiClient.GetAllPlayersAsync();
            tasks.Add(playersTask);
        }

        await Task.WhenAll(tasks);

        if (seenTask != null)
        {
            _seenPlayers = seenTask
                .Result.GroupBy(sp => Key(sp.CharacterName, sp.Realm))
                .ToDictionary(g => g.Key, g => g.Max(sp => sp.LastSeenAt));
            _seenPlayersLoaded = true;
            logger.LogInformation("Seen players cache loaded: {Count} entries", _seenPlayers.Count);
        }

        if (playersTask != null)
        {
            _players = playersTask
                .Result.GroupBy(p => Key(p.CharacterName, p.Realm))
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.UpdatedAt).First());
            logger.LogInformation("Players cache refreshed: {Count} entries", _players.Count);
        }
    }

    public DateTime? GetLastSeenAt(string characterName, string realm)
    {
        return _seenPlayers.TryGetValue(Key(characterName, realm), out var lastSeenAt)
            ? lastSeenAt
            : null;
    }

    public bool IsBlacklisted(string characterName, string realm)
    {
        return _players.TryGetValue(Key(characterName, realm), out var player)
            && player.Status == PlayerStatus.Blacklisted;
    }

    /// <summary>
    /// Mark a player as seen — updates both the local cache and the API.
    /// </summary>
    public async Task AddSeenPlayerAsync(string characterName, string realm, DateTime lastUpdated)
    {
        _seenPlayers[Key(characterName, realm)] = lastUpdated;
        await apiClient.AddSeenPlayerAsync(characterName, realm, lastUpdated);
    }
}
