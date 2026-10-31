import Markdown from "react-markdown";
import { useEffect, useState } from "react";
import { RefreshCw } from "lucide-react";
import { type PlayerResponse, type PlayerEnrichmentResponse, PlayerStatus } from "../types";
import { EnrichmentRequestError, fetchPlayer, fetchPlayerEnrichment, fetchWowProgressPending, refreshPlayerEnrichment } from "../api";
import { STATUS_LABELS, CLASS_COLORS } from "../constants";

interface PlayerDetailModalProps {
    player: PlayerResponse;
    aiLoading: boolean;
    onClose: () => void;
    onStatusChange: (realmSlug: string, characterName: string, status: PlayerStatus) => void;
    onRequestAi: () => void;
    onPlayerUpdated: (player: PlayerResponse) => void;
}

export default function PlayerDetailModal({ player, aiLoading, onClose, onStatusChange, onRequestAi, onPlayerUpdated }: PlayerDetailModalProps) {
    const [enrichment, setEnrichment] = useState<PlayerEnrichmentResponse | null>(null);
    const [metadataLoading, setMetadataLoading] = useState(true);
    const [refreshing, setRefreshing] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [retryAt, setRetryAt] = useState(0);
    const wowProgressPending = enrichment?.wowProgress?.outcome === "pending";
    const aiStale = !!player.geminiTake && !!player.enrichedAt && (!player.geminiGeneratedAt || new Date(player.enrichedAt) > new Date(player.geminiGeneratedAt));

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
            setEnrichment(result.enrichment);
            onPlayerUpdated(result.player);
            setRetryAt(Date.now() + 60_000);
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
                                {player.raiderIoSummary && (
                                    <div className="col">
                                        <h6>Raider.IO</h6>
                                        <div className="small">
                                            <Markdown>{player.raiderIoSummary}</Markdown>
                                        </div>
                                    </div>
                                )}

                                {/* Warcraft Logs Summary */}
                                {player.warcraftLogsSummary && (
                                    <div className="col">
                                        <h6>Warcraft Logs</h6>
                                        <div className="small">
                                            <Markdown>{player.warcraftLogsSummary}</Markdown>
                                        </div>
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
