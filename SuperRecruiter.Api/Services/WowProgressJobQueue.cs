using Microsoft.Extensions.Caching.Memory;
using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Api.Services;

public sealed class WowProgressJobQueue(IMemoryCache cache, TimeProvider clock)
{
    private readonly object _gate = new();
    private readonly object _cacheKey = new();
    private readonly object _discoveryKey = new();
    private const int Capacity = 1000;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(3);

    private Dictionary<int, Entry> Entries => cache.GetOrCreate(_cacheKey, _ => new Dictionary<int, Entry>())!;

    public void PublishDiscoveries(List<Player> players)
    {
        lock (_gate)
            cache.Set(_discoveryKey, players.ToArray(), TimeSpan.FromHours(2));
    }

    public Player[] GetDiscoveries()
    {
        lock (_gate)
            return cache.Get<Player[]>(_discoveryKey) ?? [];
    }

    public PlayerEnrichmentResponse WithPending(PlayerEnrichmentResponse snapshots, int playerId)
    {
        lock (_gate)
        {
            Prune();
            return Entries.TryGetValue(playerId, out var entry) && !entry.Completed
                ? snapshots with
                {
                    WowProgress = new(1, snapshots.WowProgress?.Data, snapshots.WowProgress?.FetchedAt, entry.CreatedAt, "pending"),
                }
                : snapshots;
        }
    }

    public bool Enqueue(PlayerResponse player)
    {
        lock (_gate)
        {
            Prune();
            if (Entries.TryGetValue(player.Id, out var existing) && !existing.Completed)
                return true;
            if (Entries.Count >= Capacity && existing is null)
                return false;
            Entries[player.Id] = new(new(Guid.NewGuid(), Guid.Empty, player.Id, player.CharacterName, player.RealmSlug), clock.GetUtcNow());
            return true;
        }
    }

    public bool IsPending(int playerId)
    {
        lock (_gate)
        {
            Prune();
            return Entries.TryGetValue(playerId, out var entry) && !entry.Completed;
        }
    }

    public WowProgressJobResponse? Claim()
    {
        lock (_gate)
        {
            Prune();
            var entry = Entries.Values.FirstOrDefault(entry => !entry.Completed && !entry.Completing && entry.LeasedUntil <= clock.GetUtcNow());
            if (entry is null)
                return null;
            entry.Job = entry.Job with { LeaseId = Guid.NewGuid() };
            entry.LeasedUntil = clock.GetUtcNow().Add(LeaseDuration);
            return entry.Job;
        }
    }

    public async Task<bool> CompleteAsync(Guid id, Guid leaseId, Func<WowProgressJobResponse, Task> save)
    {
        Entry? entry;
        lock (_gate)
        {
            Prune();
            entry = Entries.Values.FirstOrDefault(entry => entry.Job.Id == id && entry.Job.LeaseId == leaseId && leaseId != Guid.Empty);
            if (entry is null)
                return false;
            if (entry.Completed)
                return true;
            if (entry.Completing || entry.LeasedUntil <= clock.GetUtcNow())
                return false;
            entry.Completing = true;
        }
        try
        {
            await save(entry.Job);
            lock (_gate)
            {
                entry.Completed = true;
                entry.CreatedAt = clock.GetUtcNow();
            }
            return true;
        }
        finally
        {
            lock (_gate)
                entry.Completing = false;
        }
    }

    private void Prune()
    {
        var now = clock.GetUtcNow();
        foreach (
            var pair in Entries
                .Where(pair => !pair.Value.Completing && now - pair.Value.CreatedAt > (pair.Value.Completed ? TimeSpan.FromMinutes(10) : TimeSpan.FromDays(1)))
                .ToArray()
        )
            Entries.Remove(pair.Key);
    }

    private sealed class Entry(WowProgressJobResponse job, DateTimeOffset createdAt)
    {
        public WowProgressJobResponse Job { get; set; } = job;
        public DateTimeOffset CreatedAt { get; set; } = createdAt;
        public DateTimeOffset LeasedUntil { get; set; }
        public bool Completing { get; set; }
        public bool Completed { get; set; }
    }
}
