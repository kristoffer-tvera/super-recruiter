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

const CLASS_ICONS: Record<string, string> = {
  "death knight":
    "https://render.worldofwarcraft.com/eu/icons/56/classicon_deathknight.jpg",
  deathknight:
    "https://render.worldofwarcraft.com/eu/icons/56/classicon_deathknight.jpg",
  "demon hunter":
    "https://render.worldofwarcraft.com/eu/icons/56/classicon_demonhunter.jpg",
  druid: "https://render.worldofwarcraft.com/eu/icons/56/classicon_druid.jpg",
  evoker: "https://render.worldofwarcraft.com/eu/icons/56/classicon_evoker.jpg",
  hunter: "https://render.worldofwarcraft.com/eu/icons/56/classicon_hunter.jpg",
  mage: "https://render.worldofwarcraft.com/eu/icons/56/classicon_mage.jpg",
  monk: "https://render.worldofwarcraft.com/eu/icons/56/classicon_monk.jpg",
  paladin:
    "https://render.worldofwarcraft.com/eu/icons/56/classicon_paladin.jpg",
  priest: "https://render.worldofwarcraft.com/eu/icons/56/classicon_priest.jpg",
  rogue: "https://render.worldofwarcraft.com/eu/icons/56/classicon_rogue.jpg",
  shaman: "https://render.worldofwarcraft.com/eu/icons/56/classicon_shaman.jpg",
  warlock:
    "https://render.worldofwarcraft.com/eu/icons/56/classicon_warlock.jpg",
  warrior:
    "https://render.worldofwarcraft.com/eu/icons/56/classicon_warrior.jpg",
};

export default function Dashboard({
  initialPlayerId,
}: {
  initialPlayerId?: number;
}) {
  const [players, setPlayers] = useState<PlayerResponse[]>([]);
  const [filter, setFilter] = useState<PlayerStatus | undefined>(undefined);
  const [classFilter, setClassFilter] = useState<string | undefined>(undefined);
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
    fetchPlayers(filter, classFilter)
      .then(setPlayers)
      .catch(console.error)
      .finally(() => setLoading(false));
  }, [filter, classFilter]);

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
        <select
          className="form-select form-select-sm ms-2"
          style={{ width: "auto", minWidth: "120px" }}
          value={classFilter}
          onChange={(e) => setClassFilter(e.target.value || undefined)}
        >
          <option value="">All Classes</option>
          {Object.keys(CLASS_ICONS).map((cls) => (
            <option key={cls} value={cls}>
              {cls.charAt(0).toUpperCase() + cls.slice(1)}
            </option>
          ))}
        </select>
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
        <div className="text-light">
          {/* Header row */}
          <div
            className="row fw-bold border-bottom py-3 small d-none d-md-flex align-items-center"
            style={{ backgroundColor: "#1A1716", color: "#fff" }}
          >
            <div className="col-3">Player</div>
            <div className="col-1">Class</div>
            <div className="col-2">Realm</div>
            <div className="col-1 text-end">iLvl</div>
            <div className="col-1">Status</div>
            <div className="col-2">Found</div>
            <div className="col-2">Actions</div>
          </div>

          {players.map((p, i) => (
            <div
              key={p.id}
              className={`row align-items-center py-2 ${i % 2 === 0 ? "table-bg-light" : "table-bg-dark"}`}
              style={{ cursor: "pointer", borderRadius: 4 }}
              onClick={() => openPlayer(p)}
            >
              <div
                className="col-md-3 fw-semibold"
                style={{
                  color: CLASS_COLORS[p.class?.toLowerCase()] ?? "#fff",
                }}
              >
                {p.characterName}
              </div>
              <div className="col-md-1">
                {CLASS_ICONS[p.class.toLowerCase()] && (
                  <img
                    src={CLASS_ICONS[p.class.toLowerCase()]}
                    alt={p.class}
                    style={{ width: 20, height: 20 }}
                  />
                )}
              </div>
              <div className="col-md-2">{p.realm}</div>
              <div className="col-md-1 text-end">{p.itemLevel.toFixed(1)}</div>
              <div className="col-md-1">
                <span className={`badge ${STATUS_BADGE[p.status]}`}>
                  {STATUS_LABELS[p.status]}
                </span>
              </div>
              <div className="col-md-2 small">
                {new Date(p.createdAt).toLocaleDateString(undefined, {
                  year: "numeric",
                  month: "2-digit",
                  day: "2-digit",
                  hour: "2-digit",
                  minute: "2-digit",
                })}
              </div>
              <div className="col-md-2">
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
              </div>
            </div>
          ))}

          {players.length === 0 && (
            <p className="text-center text-muted mt-3">No players found</p>
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
