using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Worker.Services;

/// <summary>
/// In-memory cache for player and seen-player data.
/// Seen players load once; player status refreshes on demand with a bounded lifetime.
/// Seen player batches are collected
/// and flushed in bulk to minimize HTTP calls and database load.
/// </summary>
public class PlayerCacheService(SuperRecruiterApiClient apiClient, ILogger<PlayerCacheService> logger, IConfiguration configuration, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan _playerCacheLifetime = TimeSpan.FromMinutes(
        configuration.GetValue("PlayerCacheRefreshMinutes", configuration.GetValue("PollingIntervalMinutes", 5) * 4)
    );
    private DateTimeOffset _nextPlayersRefreshAt = DateTimeOffset.MinValue;

    // Key: "charactername-realm" (lowered)
    private Dictionary<string, DateTime> _seenPlayers = new();
    private Dictionary<string, PlayerCacheResponse> _players = new();
    private bool _seenPlayersLoaded;

    // Batch for seen players to be flushed
    private readonly Dictionary<string, SeenPlayerRequest> _seenPlayerBatch = new();

    private static string Key(string name, string realm) => $"{name.ToLowerInvariant()}-{realm.ToLowerInvariant()}";

    /// <summary>
    /// Load seen-player data from the API once, and refresh player data
    /// only when <paramref name="refreshPlayers"/> is true and its lifetime has expired.
    /// </summary>
    public async Task RefreshAsync(bool refreshPlayers, CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task>();

        Task<List<SeenPlayerResponse>>? seenTask = null;
        if (!_seenPlayersLoaded)
        {
            seenTask = apiClient.GetAllSeenPlayersAsync(cancellationToken);
            tasks.Add(seenTask);
        }

        Task<List<PlayerCacheResponse>>? playersTask = null;
        if (refreshPlayers && _clock.GetUtcNow() >= _nextPlayersRefreshAt)
        {
            playersTask = apiClient.GetPlayersCacheAsync(cancellationToken);
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
            _nextPlayersRefreshAt = _clock.GetUtcNow() + _playerCacheLifetime;
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
        var key = Key(characterName, realm);
        if (_seenPlayers.TryGetValue(key, out var existing) && existing >= lastUpdated)
            return;

        _seenPlayers[key] = lastUpdated;
        _seenPlayerBatch[key] = new SeenPlayerRequest
        {
            CharacterName = characterName,
            Realm = realm,
            LastUpdated = lastUpdated,
        };
    }

    /// <summary>
    /// Flush all queued seen players to the API in a single bulk operation.
    /// Should be called at the end of each scan cycle.
    /// </summary>
    public async Task<bool> FlushSeenPlayerBatchAsync(CancellationToken cancellationToken = default)
    {
        if (_seenPlayerBatch.Count == 0)
            return false;

        try
        {
            await apiClient.BulkAddSeenPlayersAsync(_seenPlayerBatch.Values.ToList(), cancellationToken);
            logger.LogInformation("Flushed {Count} seen players to API", _seenPlayerBatch.Count);
            _seenPlayerBatch.Clear();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to flush seen player batch");
            throw;
        }
    }
}
