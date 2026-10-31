using System.Collections.Concurrent;
using System.Text.Json;
using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Shared.Helpers;
using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Api.Services;

public sealed class PlayerEnrichmentService(
    PlayerDatabaseService database,
    PlayerSourceClient sources,
    ILogger<PlayerEnrichmentService> logger,
    WowProgressJobQueue wowProgressJobs
)
{
    private readonly ConcurrentDictionary<int, byte> _active = new();
    private readonly SemaphoreSlim _capacity = new(2, 2);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PlayerEnrichmentRefreshResponse> RefreshAsync(PlayerResponse player, CancellationToken cancellationToken)
    {
        if (!_active.TryAdd(player.Id, 0))
            throw new EnrichmentBusyException(false);
        var entered = false;
        try
        {
            entered = await _capacity.WaitAsync(0, cancellationToken);
            if (!entered || !await database.TryBeginEnrichmentAsync(player.Id, cancellationToken))
                throw new EnrichmentBusyException(true);
            var previous = await database.GetEnrichmentAsync(player.Id, cancellationToken: cancellationToken);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(25));
            var raiderIo = CaptureAsync(previous.RaiderIo, token => sources.FetchRaiderIoAsync(player, token), 20, deadline.Token);
            var warcraftLogs = CaptureAsync(previous.WarcraftLogs, token => sources.FetchWarcraftLogsAsync(player, token), 20, deadline.Token);
            await Task.WhenAll(raiderIo, warcraftLogs);
            var snapshots = new PlayerEnrichmentResponse(await raiderIo, await warcraftLogs, null);
            cancellationToken.ThrowIfCancellationRequested();
            await database.SaveEnrichmentAsync(player.Id, snapshots, Project(snapshots), cancellationToken);
            var queued = wowProgressJobs.Enqueue(player);
            snapshots = snapshots with { WowProgress = previous.WowProgress };
            snapshots = queued
                ? wowProgressJobs.WithPending(snapshots, player.Id)
                : snapshots with
                {
                    WowProgress = new(1, previous.WowProgress?.Data, previous.WowProgress?.FetchedAt, DateTimeOffset.UtcNow, "queue_full"),
                };
            var updated = await database.GetPlayerByIdAsync(player.Id, cancellationToken) ?? throw new InvalidOperationException("Player was removed during enrichment");
            return new(updated, WithoutData(snapshots));
        }
        finally
        {
            if (entered)
                _capacity.Release();
            _active.TryRemove(player.Id, out _);
        }
    }

    public async Task<SourceSnapshot> CaptureAsync(SourceSnapshot? previous, Func<CancellationToken, Task<JsonElement>> fetch, int seconds, CancellationToken cancellationToken)
    {
        var attemptedAt = DateTimeOffset.UtcNow;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
        try
        {
            var data = await fetch(deadline.Token);
            if (System.Text.Encoding.UTF8.GetByteCount(data.GetRawText()) > 1024 * 1024)
                throw new SourceFetchException("response_too_large");
            return new SourceSnapshot(1, data.Clone(), DateTimeOffset.UtcNow, attemptedAt, "success");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(previous, attemptedAt, "timeout");
        }
        catch (SourceFetchException exception)
        {
            return Failed(previous, attemptedAt, exception.Outcome);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Enrichment source failed with {ExceptionType}", exception.GetType().Name);
            return Failed(previous, attemptedAt, "upstream_error");
        }
    }

    private static SourceSnapshot Failed(SourceSnapshot? previous, DateTimeOffset attemptedAt, string outcome) =>
        new(1, previous?.Data, previous?.FetchedAt, attemptedAt, outcome, outcome);

    public static PlayerEnrichmentResponse WithoutData(PlayerEnrichmentResponse snapshots) =>
        new(
            snapshots.RaiderIo is { } raiderIo ? raiderIo with { Data = null } : null,
            snapshots.WarcraftLogs is { } warcraftLogs ? warcraftLogs with { Data = null } : null,
            snapshots.WowProgress is { } wowProgress ? wowProgress with { Data = null } : null
        );

    public static EnrichmentProjection Project(PlayerEnrichmentResponse snapshots)
    {
        string? raiderSummary = null;
        string? logsSummary = null;
        DiscoveryProgression? progression = null;
        WowProgressDetails? details = null;
        if (snapshots.RaiderIo is { Outcome: "success", Data: { } rawRaider })
        {
            raiderSummary = TryProject(() =>
            {
                var profile = rawRaider.GetProperty("profile").Deserialize<RaiderIOProfile>(JsonOptions)!;
                return PlayerSummaryHelper.GetCuttingEdgeSummary(profile);
            });
            progression = TryProject(() =>
            {
                var current = rawRaider.GetProperty("currentTier").Deserialize<RaiderIOProfile>(JsonOptions);
                return current?.Raid_progression is { Count: > 0 } raids ? new DiscoveryProgression(snapshots.RaiderIo.FetchedAt!.Value, raids) : null;
            });
        }
        if (snapshots.WarcraftLogs is { Outcome: "success", Data: { } rawLogs })
        {
            logsSummary = TryProject(() =>
            {
                var rankings = rawLogs.Deserialize<WarcraftLogsCharacterResponse>(JsonOptions)?.Data?.CharacterData?.Character?.ZoneRankings;
                return rankings is null
                    ? "No public WarcraftLogs rankings"
                    : string.Join("\n\n", PlayerSummaryHelper.GetAllStarsSummary(rankings), PlayerSummaryHelper.GetBossSummary(rankings));
            });
        }
        if (snapshots.WowProgress is { Outcome: "success", Data: { } rawWow })
            details = TryProject(() => rawWow.Deserialize<WowProgressDetails>(JsonOptions));
        return new(raiderSummary, logsSummary, details, progression);
    }

    private static TResult? TryProject<TResult>(Func<TResult?> project)
        where TResult : class
    {
        try
        {
            return project();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or NullReferenceException or FormatException)
        {
            return null;
        }
    }
}

public sealed class EnrichmentBusyException(bool cooldown) : Exception
{
    public bool Cooldown { get; } = cooldown;
}
