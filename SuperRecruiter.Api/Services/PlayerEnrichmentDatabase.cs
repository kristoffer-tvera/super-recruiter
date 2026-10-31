using System.Text.Json;
using Dapper;
using SuperRecruiter.Shared.DTOs;

namespace SuperRecruiter.Api.Services;

public partial class PlayerDatabaseService
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PlayerEnrichmentResponse> GetEnrichmentAsync(int id, bool includeData = true, CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();
        var projection = includeData ? "" : " - 'data'";
        var row = await connection.QuerySingleAsync<SnapshotRow>(
            new CommandDefinition(
                $"""
                SELECT (raiderio_data{projection})::text AS RaiderIo,
                    (warcraftlogs_data{projection})::text AS WarcraftLogs,
                    (wowprogress_data{projection})::text AS WowProgress
                FROM players WHERE id = @Id
                """,
                new { Id = id },
                cancellationToken: cancellationToken
            )
        );
        return new(ReadSnapshot(row.RaiderIo), ReadSnapshot(row.WarcraftLogs), ReadSnapshot(row.WowProgress));
    }

    public async Task<bool> TryBeginEnrichmentAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();
        return await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE players SET enrichment_attempted_at = CURRENT_TIMESTAMP
                    WHERE id = @Id AND (enrichment_attempted_at IS NULL
                        OR enrichment_attempted_at <= CURRENT_TIMESTAMP - INTERVAL '60 seconds')
                    """,
                    new { Id = id },
                    cancellationToken: cancellationToken
                )
            ) == 1;
    }

    public async Task SaveEnrichmentAsync(int id, PlayerEnrichmentResponse snapshots, EnrichmentProjection projection, CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE players SET raiderio_data = CAST(@RaiderIo AS jsonb),
                    warcraftlogs_data = CAST(@WarcraftLogs AS jsonb), wowprogress_data = COALESCE(CAST(@WowProgress AS jsonb), wowprogress_data),
                    raiderio_summary = COALESCE(@RaiderIoSummary, raiderio_summary),
                    warcraftlogs_summary = COALESCE(@WarcraftLogsSummary, warcraftlogs_summary),
                    battletag = COALESCE(@BattleTag, battletag), bio = COALESCE(@Bio, bio),
                    languages = COALESCE(@Languages, languages), specs_playing = COALESCE(@SpecsPlaying, specs_playing),
                    guild_history = COALESCE(@GuildHistory, guild_history),
                    discovery_progression = COALESCE(CAST(@Progression AS jsonb), discovery_progression),
                    current_tier_mythic_kill_count = COALESCE(@MythicKills, current_tier_mythic_kill_count),
                    enriched_at = CASE WHEN @Succeeded THEN CURRENT_TIMESTAMP ELSE enriched_at END,
                    updated_at = CASE WHEN @Succeeded THEN CURRENT_TIMESTAMP AT TIME ZONE 'UTC' ELSE updated_at END
                WHERE id = @Id
                """,
                new
                {
                    Id = id,
                    RaiderIo = snapshots.RaiderIo is null ? null : JsonSerializer.Serialize(snapshots.RaiderIo, SnapshotJsonOptions),
                    WarcraftLogs = snapshots.WarcraftLogs is null ? null : JsonSerializer.Serialize(snapshots.WarcraftLogs, SnapshotJsonOptions),
                    WowProgress = snapshots.WowProgress is null ? null : JsonSerializer.Serialize(snapshots.WowProgress, SnapshotJsonOptions),
                    projection.RaiderIoSummary,
                    projection.WarcraftLogsSummary,
                    projection.Details?.BattleTag,
                    projection.Details?.Bio,
                    projection.Details?.Languages,
                    projection.Details?.SpecsPlaying,
                    GuildHistory = projection.Details?.GuildHistory,
                    Progression = projection.Progression is null ? null : JsonSerializer.Serialize(projection.Progression, SnapshotJsonOptions),
                    MythicKills = projection.Progression?.Raids.Values.Sum(raid => raid.Mythic_bosses_killed),
                    Succeeded = new[] { snapshots.RaiderIo, snapshots.WarcraftLogs, snapshots.WowProgress }.Any(source => source?.Outcome == "success"),
                },
                cancellationToken: cancellationToken
            )
        );
    }

    public async Task SaveWowProgressAsync(int id, CompleteWowProgressJobRequest result, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var succeeded = result.Outcome == "success";
        var snapshot = succeeded
            ? JsonSerializer.Serialize(new SourceSnapshot(1, JsonSerializer.SerializeToElement(result.Details, SnapshotJsonOptions), now, now, "success"), SnapshotJsonOptions)
            : JsonSerializer.Serialize(
                new
                {
                    schemaVersion = 1,
                    lastAttemptAt = now,
                    outcome = result.Outcome,
                    error = result.Outcome,
                },
                SnapshotJsonOptions
            );
        using var connection = CreateConnection();
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE players SET wowprogress_data = CASE WHEN @Succeeded THEN CAST(@Snapshot AS jsonb)
                    ELSE COALESCE(wowprogress_data, '{"schemaVersion":1,"data":null,"fetchedAt":null}'::jsonb) || CAST(@Snapshot AS jsonb) END,
                    battletag = COALESCE(@BattleTag, battletag), bio = COALESCE(@Bio, bio),
                    languages = COALESCE(@Languages, languages), specs_playing = COALESCE(@SpecsPlaying, specs_playing),
                    guild_history = COALESCE(@GuildHistory, guild_history),
                    enriched_at = CASE WHEN @Succeeded THEN CURRENT_TIMESTAMP ELSE enriched_at END,
                    updated_at = CASE WHEN @Succeeded THEN CURRENT_TIMESTAMP AT TIME ZONE 'UTC' ELSE updated_at END
                WHERE id = @Id
                """,
                new
                {
                    Id = id,
                    Succeeded = succeeded,
                    Snapshot = snapshot,
                    result.Details?.BattleTag,
                    result.Details?.Bio,
                    result.Details?.Languages,
                    result.Details?.SpecsPlaying,
                    result.Details?.GuildHistory,
                },
                cancellationToken: cancellationToken
            )
        );
    }

    private static SourceSnapshot? ReadSnapshot(string? value) => value is null ? null : JsonSerializer.Deserialize<SourceSnapshot>(value, SnapshotJsonOptions);

    private sealed class SnapshotRow
    {
        public string? RaiderIo { get; set; }
        public string? WarcraftLogs { get; set; }
        public string? WowProgress { get; set; }
    }
}

public sealed record EnrichmentProjection(string? RaiderIoSummary, string? WarcraftLogsSummary, WowProgressDetails? Details, DiscoveryProgression? Progression);
