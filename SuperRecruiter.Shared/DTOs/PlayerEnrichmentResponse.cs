using System.Text.Json;
using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Shared.DTOs;

public sealed record DiscoveryProgression(DateTimeOffset FetchedAt, Dictionary<string, RaidTier> Raids);

public sealed record SourceSnapshot(int SchemaVersion, JsonElement? Data, DateTimeOffset? FetchedAt, DateTimeOffset LastAttemptAt, string Outcome, string? Error = null);

public sealed record PlayerEnrichmentResponse(SourceSnapshot? RaiderIo, SourceSnapshot? WarcraftLogs, SourceSnapshot? WowProgress);

public sealed record PlayerEnrichmentRefreshResponse(PlayerResponse Player, PlayerEnrichmentResponse Enrichment);
