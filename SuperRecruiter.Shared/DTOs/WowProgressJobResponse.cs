namespace SuperRecruiter.Shared.DTOs;

/// <summary>A leased character-profile request for the local WoWProgress collector.</summary>
public sealed record WowProgressJobResponse(Guid Id, Guid LeaseId, int PlayerId, string CharacterName, string RealmSlug);

/// <summary>Parsed WoWProgress profile data, without the upstream HTML.</summary>
public sealed record WowProgressDetails(string? BattleTag, string? Bio, string? Languages, string? SpecsPlaying, string[] GuildHistory);

/// <summary>The result of a leased WoWProgress profile request.</summary>
public sealed record CompleteWowProgressJobRequest(Guid LeaseId, string Outcome, WowProgressDetails? Details);

/// <summary>Memory-only status for a player's queued local enrichment.</summary>
public sealed record WowProgressPendingResponse(bool Pending);
