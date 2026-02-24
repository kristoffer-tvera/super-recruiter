import { useEffect, useState, useCallback } from "react";
import Markdown from "react-markdown";
import {
  fetchPlayers,
  fetchPlayer,
  updatePlayerStatus,
  requestAiSummary,
} from "../api";
import { type PlayerResponse, PlayerStatus } from "../types";

const STATUS_LABELS: Record<PlayerStatus, string> = {
  [PlayerStatus.New]: "New",
  [PlayerStatus.Interested]: "Interested",
  [PlayerStatus.Contacted]: "Contacted",
  [PlayerStatus.Declined]: "Declined",
  [PlayerStatus.Blacklisted]: "Blacklisted",
};

const STATUS_BADGE: Record<PlayerStatus, string> = {
  [PlayerStatus.New]: "bg-primary",
  [PlayerStatus.Interested]: "bg-success",
  [PlayerStatus.Contacted]: "bg-info",
  [PlayerStatus.Declined]: "bg-secondary",
  [PlayerStatus.Blacklisted]: "bg-danger",
};

const CLASS_COLORS: Record<string, string> = {
  "death knight": "#C41F3B",
  "demon hunter": "#A330C9",
  druid: "#FF7D0A",
  evoker: "#33937F",
  hunter: "#ABD473",
  mage: "#69CCF0",
  monk: "#00FF96",
  paladin: "#F58CBA",
  priest: "#FFFFFF",
  rogue: "#FFF569",
  shaman: "#0070DE",
  warlock: "#9482C9",
  warrior: "#C79C6E",
};

export default function Dashboard({
  initialPlayerId,
}: {
  initialPlayerId?: number;
}) {
  const [players, setPlayers] = useState<PlayerResponse[]>([]);
  const [filter, setFilter] = useState<PlayerStatus | undefined>(undefined);
  const [loading, setLoading] = useState(true);
  const [selectedPlayer, setSelectedPlayer] = useState<PlayerResponse | null>(
    null,
  );
  const [aiLoading, setAiLoading] = useState(false);

  const reload = () => {
    setLoading(true);
    fetchPlayers(filter)
      .then(setPlayers)
      .catch(console.error)
      .finally(() => setLoading(false));
  };

  // Open a player modal and update the URL
  const openPlayer = useCallback((player: PlayerResponse) => {
    setSelectedPlayer(player);
    window.history.pushState(null, "", `/players/${player.id}`);
  }, []);

  const closeModal = useCallback(() => {
    setSelectedPlayer(null);
    window.history.pushState(null, "", "/");
  }, []);

  // Handle browser back/forward
  useEffect(() => {
    const onPopState = () => {
      const match = window.location.pathname.match(/^\/players\/(\d+)$/);
      if (match) {
        const id = Number(match[1]);
        const existing = players.find((p) => p.id === id);
        if (existing) {
          setSelectedPlayer(existing);
        } else {
          fetchPlayer(id).then(setSelectedPlayer).catch(console.error);
        }
      } else {
        setSelectedPlayer(null);
      }
    };
    window.addEventListener("popstate", onPopState);
    return () => window.removeEventListener("popstate", onPopState);
  }, [players]);

  // On first load, check if URL has a player ID to deep-link into
  useEffect(() => {
    if (initialPlayerId) {
      fetchPlayer(initialPlayerId).then(setSelectedPlayer).catch(console.error);
    }
  }, [initialPlayerId]);

  useEffect(() => {
    fetchPlayers(filter)
      .then(setPlayers)
      .catch(console.error)
      .finally(() => setLoading(false));
  }, [filter]);

  const handleStatusChange = async (id: number, status: PlayerStatus) => {
    try {
      const updated = await updatePlayerStatus(id, status);
      setPlayers((prev) => prev.map((p) => (p.id === id ? updated : p)));
      if (selectedPlayer?.id === id) setSelectedPlayer(updated);
    } catch (e) {
      console.error(e);
    }
  };

  const handleRequestAi = async () => {
    if (!selectedPlayer) return;
    setAiLoading(true);
    try {
      const updated = await requestAiSummary(selectedPlayer.id);
      setPlayers((prev) =>
        prev.map((p) => (p.id === updated.id ? updated : p)),
      );
      setSelectedPlayer(updated);
    } catch (e) {
      console.error(e);
    } finally {
      setAiLoading(false);
    }
  };

  return (
    <>
      {/* Filter bar */}
      <div className="d-flex gap-2 flex-wrap align-items-center mb-3">
        <button
          className={`btn btn-sm ${filter === undefined ? "btn-primary" : "btn-outline-primary"}`}
          onClick={() => setFilter(undefined)}
        >
          All
        </button>
        {Object.entries(STATUS_LABELS).map(([key, label]) => {
          const s = Number(key) as PlayerStatus;
          return (
            <button
              key={key}
              className={`btn btn-sm ${filter === s ? "btn-primary" : "btn-outline-primary"}`}
              onClick={() => setFilter(s)}
            >
              {label}
            </button>
          );
        })}
        <button
          className="btn btn-sm btn-outline-secondary ms-auto"
          onClick={reload}
        >
          Refresh
        </button>
      </div>

      {/* Table */}
      {loading ? (
        <div className="text-center py-5">
          <div className="spinner-border" role="status">
            <span className="visually-hidden">Loading...</span>
          </div>
        </div>
      ) : (
        <div className="table-responsive">
          <table className="table table-hover table-striped align-middle">
            <thead>
              <tr>
                <th>Player</th>
                <th>Class</th>
                <th>Realm</th>
                <th className="text-end">iLvl</th>
                <th>Status</th>
                <th>Found</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {players.map((p) => (
                <tr
                  key={p.id}
                  style={{ cursor: "pointer" }}
                  onClick={() => openPlayer(p)}
                >
                  <td>{p.characterName}</td>
                  <td
                    style={{
                      color: CLASS_COLORS[p.class?.toLowerCase()] ?? "#fff",
                    }}
                  >
                    {p.class}
                  </td>
                  <td>{p.realm}</td>
                  <td className="text-end">{p.itemLevel.toFixed(1)}</td>
                  <td>
                    <span className={`badge ${STATUS_BADGE[p.status]}`}>
                      {STATUS_LABELS[p.status]}
                    </span>
                  </td>
                  <td className="text-muted small">
                    {new Date(p.createdAt).toLocaleDateString()}
                  </td>
                  <td>
                    <select
                      className="form-select form-select-sm"
                      style={{ width: "auto", minWidth: "120px" }}
                      value={p.status}
                      onClick={(e) => e.stopPropagation()}
                      onChange={(e) =>
                        handleStatusChange(
                          p.id,
                          Number(e.target.value) as PlayerStatus,
                        )
                      }
                    >
                      {Object.entries(STATUS_LABELS).map(([key, label]) => (
                        <option key={key} value={key}>
                          {label}
                        </option>
                      ))}
                    </select>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {players.length === 0 && (
            <p className="text-center text-muted">No players found</p>
          )}
        </div>
      )}

      {/* Player detail modal */}
      {selectedPlayer && (
        <>
          <div
            className="modal fade show d-block"
            tabIndex={-1}
            onClick={closeModal}
          >
            <div
              className="modal-dialog modal-xl modal-dialog-scrollable"
              onClick={(e) => e.stopPropagation()}
            >
              <div className="modal-content">
                <div className="modal-header">
                  <h5
                    className="modal-title"
                    style={{
                      color:
                        CLASS_COLORS[selectedPlayer.class?.toLowerCase()] ??
                        "inherit",
                    }}
                  >
                    {selectedPlayer.characterName}-{selectedPlayer.realm}
                  </h5>
                  <button
                    type="button"
                    className="btn-close"
                    onClick={closeModal}
                  />
                </div>
                <div className="modal-body">
                  <p>
                    <strong>Class:</strong> {selectedPlayer.class} &middot;{" "}
                    <strong>iLvl:</strong> {selectedPlayer.itemLevel.toFixed(1)}
                  </p>
                  {selectedPlayer.battleTag && (
                    <p>
                      <strong>BattleTag:</strong> {selectedPlayer.battleTag}
                    </p>
                  )}
                  {selectedPlayer.languages && (
                    <p>
                      <strong>Languages:</strong> {selectedPlayer.languages}
                    </p>
                  )}
                  {selectedPlayer.specsPlaying && (
                    <p>
                      <strong>Specs:</strong> {selectedPlayer.specsPlaying}
                    </p>
                  )}

                  {/* External links */}
                  <div className="d-flex gap-2 flex-wrap mb-3">
                    <a
                      className="btn btn-sm btn-outline-info"
                      href={`https://worldofwarcraft.blizzard.com/en-gb/character/eu/${selectedPlayer.realmSlug}/${selectedPlayer.characterName}`}
                      target="_blank"
                      rel="noreferrer"
                    >
                      Armory
                    </a>
                    <a
                      className="btn btn-sm btn-outline-info"
                      href={selectedPlayer.characterUrl}
                      target="_blank"
                      rel="noreferrer"
                    >
                      WoWProgress
                    </a>
                    <a
                      className="btn btn-sm btn-outline-info"
                      href={`https://www.warcraftlogs.com/character/eu/${selectedPlayer.realmSlug}/${selectedPlayer.characterName}`}
                      target="_blank"
                      rel="noreferrer"
                    >
                      WCL
                    </a>
                    <a
                      className="btn btn-sm btn-outline-info"
                      href={`https://raider.io/characters/eu/${selectedPlayer.realmSlug}/${selectedPlayer.characterName}`}
                      target="_blank"
                      rel="noreferrer"
                    >
                      Raider.IO
                    </a>
                  </div>

                  {/* Bio */}
                  {selectedPlayer.bio && (
                    <div className="mb-3">
                      <h6>Bio</h6>
                      <p className="small" style={{ whiteSpace: "pre-wrap" }}>
                        {selectedPlayer.bio}
                      </p>
                    </div>
                  )}

                  <div className="row mb-3">
                    {/* Raider.IO Summary */}
                    {selectedPlayer.raiderIoSummary && (
                      <div className="col">
                        <h6>Raider.IO</h6>
                        <div className="small">
                          <Markdown>{selectedPlayer.raiderIoSummary}</Markdown>
                        </div>
                      </div>
                    )}

                    {/* Warcraft Logs Summary */}
                    {selectedPlayer.warcraftLogsSummary && (
                      <div className="col">
                        <h6>Warcraft Logs</h6>
                        <div className="small">
                          <Markdown>
                            {selectedPlayer.warcraftLogsSummary}
                          </Markdown>
                        </div>
                      </div>
                    )}
                  </div>

                  {/* Guild History */}
                  {selectedPlayer.guildHistory?.length > 0 && (
                    <div className="mb-3">
                      <h6>Guild History</h6>
                      <ul className="list-group list-group-flush small">
                        {selectedPlayer.guildHistory
                          .slice(0, 15)
                          .map((g, i) => (
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
                    {selectedPlayer.geminiTake ? (
                      <div className="card">
                        <div className="card-body small">
                          <Markdown>{selectedPlayer.geminiTake}</Markdown>
                        </div>
                      </div>
                    ) : (
                      <button
                        className="btn btn-outline-warning btn-sm"
                        disabled={aiLoading}
                        onClick={handleRequestAi}
                      >
                        {aiLoading ? (
                          <>
                            <span
                              className="spinner-border spinner-border-sm me-1"
                              role="status"
                            />
                            Generating...
                          </>
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
                    value={selectedPlayer.status}
                    onChange={(e) =>
                      handleStatusChange(
                        selectedPlayer.id,
                        Number(e.target.value) as PlayerStatus,
                      )
                    }
                  >
                    {Object.entries(STATUS_LABELS).map(([key, label]) => (
                      <option key={key} value={key}>
                        {label}
                      </option>
                    ))}
                  </select>
                  <button
                    className="btn btn-secondary btn-sm"
                    onClick={closeModal}
                  >
                    Close
                  </button>
                </div>
              </div>
            </div>
          </div>
          <div className="modal-backdrop fade show" />
        </>
      )}
    </>
  );
}
