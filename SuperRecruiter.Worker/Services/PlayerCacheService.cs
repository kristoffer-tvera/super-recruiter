using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Worker.Services;

/// <summary>
/// In-memory cache for player and seen-player data.
/// Primed once per scan cycle from the API using lightweight cache endpoint,
/// then used for fast lookups during filtering. Seen player batches are collected
/// and flushed in bulk to minimize HTTP calls and database load.
/// </summary>
public class PlayerCacheService(SuperRecruiterApiClient apiClient, ILogger<PlayerCacheService> logger)
{
    // Key: "charactername-realm" (lowered)
    private Dictionary<string, DateTime> _seenPlayers = new();
    private Dictionary<string, PlayerCacheResponse> _players = new();
    private bool _seenPlayersLoaded;

    // Batch for seen players to be flushed
    private List<SeenPlayerRequest> _seenPlayerBatch = new();

    private static string Key(string name, string realm) => $"{name.ToLowerInvariant()}-{realm.ToLowerInvariant()}";

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

        Task<List<PlayerCacheResponse>>? playersTask = null;
        if (refreshPlayers)
        {
            playersTask = apiClient.GetPlayersCacheAsync();
            tasks.Add(playersTask);
        }

        await Task.WhenAll(tasks);

        if (seenTask != null)
        {
            _seenPlayers = seenTask.Result.GroupBy(sp => Key(sp.CharacterName, sp.Realm)).ToDictionary(g => g.Key, g => g.Max(sp => sp.LastSeenAt));
            _seenPlayersLoaded = true;
            logger.LogInformation("Seen players cache loaded: {Count} entries", _seenPlayers.Count);
        }

        if (playersTask != null)
        {
            _players = playersTask.Result.GroupBy(p => Key(p.CharacterName, p.Realm)).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.UpdatedAt).First());
            logger.LogInformation("Players cache refreshed: {Count} entries", _players.Count);
        }
    }

    public DateTime? GetLastSeenAt(string characterName, string realm)
    {
        return _seenPlayers.TryGetValue(Key(characterName, realm), out var lastSeenAt) ? lastSeenAt : null;
    }

    public bool IsBlacklisted(string characterName, string realm)
    {
        return _players.TryGetValue(Key(characterName, realm), out var player) && player.Status == PlayerStatus.Blacklisted;
    }

    /// <summary>
    /// Queue a player to be marked as seen. Batches multiple updates and flushes them
    /// in bulk via FlushSeenPlayerBatchAsync() to minimize HTTP calls.
    /// </summary>
    public void QueueSeenPlayer(string characterName, string realm, DateTime lastUpdated)
    {
        _seenPlayers[Key(characterName, realm)] = lastUpdated;
        _seenPlayerBatch.Add(
            new SeenPlayerRequest
            {
                CharacterName = characterName,
                Realm = realm,
                LastUpdated = lastUpdated,
            }
        );
    }

    /// <summary>
    /// Flush all queued seen players to the API in a single bulk operation.
    /// Should be called at the end of each scan cycle.
    /// </summary>
    public async Task FlushSeenPlayerBatchAsync()
    {
        if (_seenPlayerBatch.Count == 0)
            return;

        try
        {
            await apiClient.BulkAddSeenPlayersAsync(_seenPlayerBatch);
            logger.LogInformation("Flushed {Count} seen players to API", _seenPlayerBatch.Count);
            _seenPlayerBatch.Clear();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to flush seen player batch");
            throw;
        }
    }
}
