import Markdown from "react-markdown";
import { useEffect, useState } from "react";
import { RefreshCw } from "lucide-react";
import { type PlayerResponse, type PlayerEnrichmentResponse, PlayerStatus } from "../types";
import { EnrichmentRequestError, fetchPlayer, fetchPlayerEnrichment, fetchWowProgressPending, refreshPlayerEnrichment } from "../api";
import { STATUS_LABELS, CLASS_COLORS, DISCOVERY_SOURCE_LABELS } from "../constants";

interface PlayerDetailModalProps {
    player: PlayerResponse;
    aiLoading: boolean;
    onClose: () => void;
    onStatusChange: (realmSlug: string, characterName: string, status: PlayerStatus) => void;
    onRequestAi: () => void;
    onPlayerUpdated: (player: PlayerResponse) => void;
}

interface RaidProgress {
    summary?: string;
    total_bosses?: number;
    normal_bosses_killed?: number;
    heroic_bosses_killed?: number;
    mythic_bosses_killed?: number;
}

interface RaiderIoSnapshotData {
    profile?: {
        raid_progression?: Record<string, RaidProgress>;
        raid_achievement_curve?: RaidAchievement[];
    };
}

interface RaidAchievement {
    raid?: string;
    aotc?: string | null;
    cutting_edge?: string | null;
}

interface WarcraftLogsRanking {
    encounter?: { name?: string };
    rankPercent?: number | string | null;
    medianPercent?: number | string | null;
    totalKills?: number;
}

interface WarcraftLogsZoneRankings {
    rankings?: WarcraftLogsRanking[];
    allStars?: { spec?: string; rankPercent?: number | string; points?: number; possiblePoints?: number }[];
}

interface WarcraftLogsSnapshotData {
    data?: { characterData?: { character?: { zoneRankings?: WarcraftLogsZoneRankings } } };
}

function parsePercent(value: number | string | null | undefined) {
    const parsed = typeof value === "number" ? value : Number.parseFloat(value ?? "");
    return Number.isFinite(parsed) ? Math.min(100, Math.max(0, parsed)) : null;
}

function getRecentProgress(data: unknown) {
    const progression = (data as RaiderIoSnapshotData | undefined)?.profile?.raid_progression;
    return Object.entries(progression ?? {}).flatMap(([raidSlug, raid]) => {
        const normal = raid.normal_bosses_killed ?? 0;
        const heroic = raid.heroic_bosses_killed ?? 0;
        const mythic = raid.mythic_bosses_killed ?? 0;
        if (normal + heroic + mythic === 0) return [];

        const total = raid.total_bosses || "?";
        const summary = raid.summary?.trim() || `${mythic || heroic || normal}/${total} ${mythic ? "M" : heroic ? "H" : "N"}`;
        return [
            {
                name: raidSlug
                    .split("-")
                    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
                    .join(" "),
                summary,
                color: mythic > 0 ? "#c792ea" : heroic > 0 ? "#58a6ff" : "#62c370",
            },
        ];
    });
}

function getAchievementProgress(data: unknown) {
    const achievements = (data as RaiderIoSnapshotData | undefined)?.profile?.raid_achievement_curve;
    return (achievements ?? []).flatMap((achievement) => {
        const difficulty = achievement.cutting_edge ? "Mythic" : achievement.aotc ? "Heroic" : null;
        if (!difficulty) return [];

        const raidName = achievement.raid
            ?.split("-")
            .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
            .join(" ");
        const dateValue = achievement.cutting_edge ?? achievement.aotc;
        const date = dateValue ? new Date(dateValue).toLocaleDateString() : "";
        return [
            {
                name: raidName || "Raid",
                difficulty,
                date,
                color: difficulty === "Mythic" ? "#c792ea" : "#58a6ff",
            },
        ];
    });
}

function getLegacyAchievementProgress(summary?: string) {
    if (!summary) return [];
    const heading = "## __Ahead of the Curve / Cutting Edge__";
    const headingIndex = summary.indexOf(heading);
    if (headingIndex < 0) return [];

    return summary
        .slice(headingIndex + heading.length)
        .split("\n")
        .flatMap((line) => {
            const match = line.match(/^\s*-\s+\*\*(.+?)\*\*\s+\|\s+(Mythic|Heroic)(?:\s+\|\s+(.+))?$/);
            if (!match) return [];
            const [, name, difficulty, date = ""] = match;
            return [
                {
                    name,
                    difficulty,
                    date,
                    color: difficulty === "Mythic" ? "#c792ea" : "#58a6ff",
                },
            ];
        });
}

function getWarcraftLogsView(data: unknown) {
    const rankings = (data as WarcraftLogsSnapshotData | undefined)?.data?.characterData?.character?.zoneRankings;
    if (!rankings) return null;

    const bosses = (rankings.rankings ?? []).flatMap((ranking) => {
        const best = parsePercent(ranking.rankPercent);
        const median = parsePercent(ranking.medianPercent);
        if (ranking.totalKills === 0 || (best === null && median === null)) return [];
        return [
            {
                name: ranking.encounter?.name || "Encounter",
                totalKills: ranking.totalKills ?? 0,
                best,
                median,
            },
        ];
    });

    return {
        bosses,
        allStars: rankings.allStars ?? [],
    };
}

export default function PlayerDetailModal({ player, aiLoading, onClose, onStatusChange, onRequestAi, onPlayerUpdated }: PlayerDetailModalProps) {
    const [enrichment, setEnrichment] = useState<PlayerEnrichmentResponse | null>(null);
    const [metadataLoading, setMetadataLoading] = useState(true);
    const [refreshing, setRefreshing] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [retryAt, setRetryAt] = useState(0);
    const wowProgressPending = enrichment?.wowProgress?.outcome === "pending";
    const aiStale = !!player.geminiTake && !!player.enrichedAt && (!player.geminiGeneratedAt || new Date(player.enrichedAt) > new Date(player.geminiGeneratedAt));
    const recentProgress = getRecentProgress(enrichment?.raiderIo?.data);
    const hasAchievementSnapshot = Array.isArray((enrichment?.raiderIo?.data as RaiderIoSnapshotData | undefined)?.profile?.raid_achievement_curve);
    const achievementProgress = hasAchievementSnapshot ? getAchievementProgress(enrichment?.raiderIo?.data) : getLegacyAchievementProgress(player.raiderIoSummary);
    const warcraftLogs = getWarcraftLogsView(enrichment?.warcraftLogs?.data);

    useEffect(() => {
        let cancelled = false;
        fetchPlayerEnrichment(player.id)
            .then((result) => {
                if (cancelled) return;
                setEnrichment(result);
                const lastAttempt = Math.max(0, ...Object.values(result).map((source) => (source ? new Date(source.lastAttemptAt).getTime() : 0)));
                if (lastAttempt + 60_000 > Date.now()) setRetryAt(lastAttempt + 60_000);
            })
            .catch(() => {
                if (!cancelled) setError("Unable to read source status");
            })
            .finally(() => {
                if (!cancelled) setMetadataLoading(false);
            });
        return () => {
            cancelled = true;
        };
    }, [player.id]);

    useEffect(() => {
        if (!retryAt) return;
        const timer = window.setTimeout(() => setRetryAt(0), Math.max(0, retryAt - Date.now()));
        return () => window.clearTimeout(timer);
    }, [retryAt]);

    useEffect(() => {
        if (!wowProgressPending) return;
        let cancelled = false;
        let timer: number;
        const poll = async () => {
            try {
                if (!(await fetchWowProgressPending(player.id))) {
                    const [metadata, updated] = await Promise.all([fetchPlayerEnrichment(player.id), fetchPlayer(player.id)]);
                    if (cancelled) return;
                    setEnrichment(metadata);
                    onPlayerUpdated(updated);
                    setError(null);
                    return;
                }
            } catch {
                if (!cancelled) setError("Unable to check WoWProgress status; retrying");
            }
            if (!cancelled) timer = window.setTimeout(poll, 10_000);
        };
        timer = window.setTimeout(poll, 10_000);
        return () => {
            cancelled = true;
            window.clearTimeout(timer);
        };
    }, [wowProgressPending, player.id, onPlayerUpdated]);

    const refresh = async () => {
        setRefreshing(true);
        setError(null);
        try {
            const result = await refreshPlayerEnrichment(player.id);
            onPlayerUpdated(result.player);
            setRetryAt(Date.now() + 60_000);
            setEnrichment(result.enrichment);
            try {
                setEnrichment(await fetchPlayerEnrichment(player.id));
            } catch {
                setError("Source refresh succeeded, but Raider.IO details could not be reloaded");
            }
        } catch (exception) {
            setError(exception instanceof Error ? exception.message : "Unable to refresh player data");
            if (exception instanceof EnrichmentRequestError && exception.retryAfterSeconds > 0) setRetryAt(Date.now() + exception.retryAfterSeconds * 1000);
        } finally {
            setRefreshing(false);
        }
    };

    const hasSnapshot = Object.values(enrichment ?? {}).some((source) => source?.fetchedAt);
    const sourceLabels = { raiderIo: "Raider.IO", warcraftLogs: "Warcraft Logs", wowProgress: "WoWProgress" } as const;

    return (
        <>
            <div className="modal fade show d-block" tabIndex={-1} onClick={onClose}>
                <div className="modal-dialog modal-xl modal-dialog-scrollable" onClick={(e) => e.stopPropagation()}>
                    <div className="modal-content">
                        <div className="modal-header">
                            <h5
                                className="modal-title"
                                style={{
                                    color: CLASS_COLORS[player.class?.toLowerCase()] ?? "inherit",
                                }}
                            >
                                {player.characterName}-{player.realm}
                            </h5>
                            <button type="button" className="btn-close" onClick={onClose} />
                        </div>
                        <div className="modal-body">
                            <p>
                                <strong>Class:</strong> {player.class} &middot; <strong>iLvl:</strong> {player.itemLevel.toFixed(1)}
                            </p>
                            <p>
                                <strong>Found via:</strong>{" "}
                                {player.sources?.length ? player.sources.map((source) => DISCOVERY_SOURCE_LABELS[source] ?? "Other").join(", ") : "Unknown"}
                            </p>
                            {player.battleTag && (
                                <p>
                                    <strong>BattleTag:</strong> {player.battleTag}
                                </p>
                            )}
                            {player.languages && (
                                <p>
                                    <strong>Languages:</strong> {player.languages}
                                </p>
                            )}
                            {player.specsPlaying && (
                                <p>
                                    <strong>Specs:</strong> {player.specsPlaying}
                                </p>
                            )}

                            {/* External links */}
                            <div className="d-flex gap-2 flex-wrap mb-3">
                                <a
                                    className="btn btn-sm btn-outline-info"
                                    href={`https://worldofwarcraft.blizzard.com/en-gb/character/eu/${player.realmSlug}/${player.characterName}`}
                                    target="_blank"
                                    rel="noreferrer"
                                >
                                    Armory
                                </a>
                                <a className="btn btn-sm btn-outline-info" href={player.characterUrl} target="_blank" rel="noreferrer">
                                    WoWProgress
                                </a>
                                <a
                                    className="btn btn-sm btn-outline-info"
                                    href={`https://www.warcraftlogs.com/character/eu/${player.realmSlug}/${player.characterName}`}
                                    target="_blank"
                                    rel="noreferrer"
                                >
                                    WCL
                                </a>
                                <a
                                    className="btn btn-sm btn-outline-info"
                                    href={`https://raider.io/characters/eu/${player.realmSlug}/${player.characterName}`}
                                    target="_blank"
                                    rel="noreferrer"
                                >
                                    Raider.IO
                                </a>
                            </div>

                            <section className="border-top border-bottom py-3 mb-3" aria-label="Source data">
                                <div className="d-flex align-items-center justify-content-between gap-2 flex-wrap mb-2">
                                    <h6 className="mb-0">Source Data</h6>
                                    <button
                                        className="btn btn-sm btn-outline-info d-inline-flex align-items-center gap-2"
                                        disabled={metadataLoading || refreshing || aiLoading || retryAt > 0}
                                        onClick={refresh}
                                        title={retryAt ? "Refresh available after a short cooldown" : "Fetch current source data"}
                                    >
                                        {refreshing ? <span className="spinner-border spinner-border-sm" role="status" /> : <RefreshCw size={16} aria-hidden="true" />}
                                        {refreshing ? "Fetching..." : hasSnapshot ? "Refresh Data" : "Fetch Data"}
                                    </button>
                                </div>
                                {error && (
                                    <div className="alert alert-danger py-2 small" role="alert">
                                        {error}
                                    </div>
                                )}
                                {metadataLoading ? (
                                    <span className="text-muted small">Loading source status...</span>
                                ) : (
                                    <ul className="list-unstyled small mb-0">
                                        {Object.entries(sourceLabels).map(([key, label]) => {
                                            const source = enrichment?.[key as keyof PlayerEnrichmentResponse];
                                            return (
                                                <li key={key} className="d-flex justify-content-between gap-2 flex-wrap py-1">
                                                    <span>{label}</span>
                                                    <span className={source && source.outcome !== "success" ? "text-warning text-break" : "text-muted"}>
                                                        {source?.outcome === "pending"
                                                            ? "Queued for local collector"
                                                            : source?.fetchedAt
                                                              ? new Date(source.fetchedAt).toLocaleString()
                                                              : "Not fetched"}
                                                        {source &&
                                                            source.outcome !== "success" &&
                                                            source.outcome !== "pending" &&
                                                            ` (${source.outcome.replaceAll("_", " ")}; ${source.fetchedAt ? "previous data kept" : "no snapshot"})`}
                                                    </span>
                                                </li>
                                            );
                                        })}
                                    </ul>
                                )}
                                {retryAt > 0 && (
                                    <div className="small text-muted mt-2" role="status">
                                        Refresh available shortly
                                    </div>
                                )}
                            </section>

                            {/* Bio */}
                            {player.bio && (
                                <div className="mb-3">
                                    <h6>Bio</h6>
                                    <p className="small" style={{ whiteSpace: "pre-wrap" }}>
                                        {player.bio}
                                    </p>
                                </div>
                            )}

                            <div className="row mb-3">
                                {/* Raider.IO Summary */}
                                {(recentProgress.length > 0 || achievementProgress.length > 0) && (
                                    <div className="col-12">
                                        <h5>Raider.IO</h5>
                                        <div className="row">
                                            {recentProgress.length > 0 && (
                                                <section className="col-12 col-md-6">
                                                    <h6 className="mt-3">Recent Progress</h6>
                                                    <ul className="list-unstyled small mb-2">
                                                        {recentProgress.map((raid) => (
                                                            <li key={raid.name} style={{ color: raid.color }}>
                                                                <strong>{raid.name}</strong> | {raid.summary}
                                                            </li>
                                                        ))}
                                                    </ul>
                                                </section>
                                            )}
                                            {achievementProgress.length > 0 && (
                                                <section className="col-12 col-md-6">
                                                    <h6 className="mt-3">Ahead of the Curve / Cutting Edge</h6>
                                                    <ul className="list-unstyled small mb-2">
                                                        {achievementProgress.map((achievement) => (
                                                            <li key={achievement.name} style={{ color: achievement.color }}>
                                                                <strong>{achievement.name}</strong> | {achievement.difficulty}
                                                                {achievement.date && ` | ${achievement.date}`}
                                                            </li>
                                                        ))}
                                                    </ul>
                                                </section>
                                            )}
                                        </div>
                                    </div>
                                )}

                                {/* Warcraft Logs Summary */}
                                {warcraftLogs && (
                                    <div className="col-12">
                                        <h5>Warcraft Logs</h5>
                                        {warcraftLogs.bosses.length > 0 && (
                                            <section aria-label="Warcraft Logs boss rankings">
                                                <h6>Boss rankings</h6>
                                                <div className="wcl-ranking-chart">
                                                    {warcraftLogs.bosses.map((boss) => (
                                                        <div className="wcl-ranking-row" key={boss.name}>
                                                            <div className="d-flex justify-content-between gap-2 small mb-1">
                                                                <strong>{boss.name}</strong>
                                                                <span className="text-muted">
                                                                    {boss.totalKills} {boss.totalKills === 1 ? "kill" : "kills"}
                                                                </span>
                                                            </div>
                                                            <div className="wcl-ranking-bar-row small">
                                                                {(() => {
                                                                    const medianWidth = boss.median === null ? 0 : Math.min(boss.median, boss.best ?? boss.median);
                                                                    const bestWidth = boss.best === null ? 0 : Math.max(0, boss.best - medianWidth);
                                                                    return (
                                                                        <div
                                                                            className="wcl-ranking-track"
                                                                            role="img"
                                                                            aria-label={`Best parse ${boss.best === null ? "unavailable" : `${boss.best.toFixed(0)} percent`}; median ${boss.median === null ? "unavailable" : `${boss.median.toFixed(0)} percent`}`}
                                                                        >
                                                                            {medianWidth > 0 && (
                                                                                <div className="wcl-ranking-bar wcl-ranking-median" style={{ width: `${medianWidth}%` }} />
                                                                            )}
                                                                            {bestWidth > 0 && (
                                                                                <div
                                                                                    className="wcl-ranking-bar wcl-ranking-best"
                                                                                    style={{ left: `${medianWidth}%`, width: `${bestWidth}%` }}
                                                                                />
                                                                            )}
                                                                        </div>
                                                                    );
                                                                })()}
                                                                <span className="text-end">{boss.best === null ? "-" : `${boss.best.toFixed(0)}%`}</span>
                                                            </div>
                                                        </div>
                                                    ))}
                                                </div>
                                            </section>
                                        )}
                                        {warcraftLogs.allStars.length > 0 && (
                                            <section className="small mt-3">
                                                <h6>All-stars</h6>
                                                <ul className="list-unstyled mb-0">
                                                    {warcraftLogs.allStars.map((star) => (
                                                        <li key={star.spec}>
                                                            <strong>{star.spec}</strong> | {parsePercent(star.rankPercent)?.toFixed(0) ?? "-"}%
                                                            {star.points !== undefined &&
                                                                star.possiblePoints !== undefined &&
                                                                ` | ${star.points.toFixed(0)} / ${star.possiblePoints}`}
                                                        </li>
                                                    ))}
                                                </ul>
                                            </section>
                                        )}
                                    </div>
                                )}
                            </div>

                            {/* Guild History */}
                            {player.guildHistory?.length > 0 && (
                                <div className="mb-3">
                                    <h6>Guild History</h6>
                                    <ul className="list-group list-group-flush small">
                                        {player.guildHistory.slice(0, 15).map((g, i) => (
                                            <li key={i} className="list-group-item py-1">
                                                {g}
                                            </li>
                                        ))}
                                    </ul>
                                </div>
                            )}

                            {/* AI Evaluation */}
                            <div className="mb-3">
                                <h6>AI Evaluation</h6>
                                {aiStale && <p className="small text-warning">Source data has changed since this evaluation.</p>}
                                {player.geminiTake ? (
                                    <div className="card">
                                        <div className="card-body small">
                                            <Markdown>{player.geminiTake}</Markdown>
                                        </div>
                                    </div>
                                ) : null}
                                {(!player.geminiTake || aiStale) && (
                                    <button className="btn btn-outline-warning btn-sm" disabled={aiLoading || refreshing} onClick={onRequestAi}>
                                        {aiLoading ? (
                                            <>
                                                <span className="spinner-border spinner-border-sm me-1" role="status" />
                                                Generating...
                                            </>
                                        ) : aiStale ? (
                                            "Regenerate AI Summary"
                                        ) : (
                                            "Request AI Summary"
                                        )}
                                    </button>
                                )}
                            </div>
                        </div>
                        <div className="modal-footer">
                            <select
                                className="form-select form-select-sm"
                                style={{ width: "auto" }}
                                value={player.status}
                                onChange={(e) => onStatusChange(player.realmSlug, player.characterName, Number(e.target.value) as PlayerStatus)}
                            >
                                {Object.entries(STATUS_LABELS).map(([key, label]) => (
                                    <option key={key} value={key}>
                                        {label}
                                    </option>
                                ))}
                            </select>
                            <button className="btn btn-secondary btn-sm" onClick={onClose}>
                                Close
                            </button>
                        </div>
                    </div>
                </div>
            </div>
            <div className="modal-backdrop fade show" />
        </>
    );
}
