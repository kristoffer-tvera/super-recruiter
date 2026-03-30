import { useEffect, useState, useCallback } from "react";
import {
  fetchPlayers,
  fetchPlayer,
  updatePlayerStatus,
  requestAiSummary,
} from "../api";
import { type PlayerResponse, PlayerStatus, type PlayerFilter } from "../types";
import {
  STATUS_LABELS,
  STATUS_BADGE,
  CLASS_COLORS,
  CLASS_ICONS,
} from "../constants";
import FilterBar from "../components/FilterBar";
import PlayerDetailModal from "../components/PlayerDetailModal";

const PAGE_SIZE = 20;

export default function Dashboard({
  initialPlayerId,
}: {
  initialPlayerId?: number;
}) {
  const [players, setPlayers] = useState<PlayerResponse[]>([]);
  const [playerFilter, setPlayerFilter] = useState<PlayerFilter>({
    limit: PAGE_SIZE,
    offset: 0,
  });
  const [loading, setLoading] = useState(true);
  const [selectedPlayer, setSelectedPlayer] = useState<PlayerResponse | null>(
    null,
  );
  const [aiLoading, setAiLoading] = useState(false);

  const reload = () => {
    setLoading(true);
    fetchPlayers(playerFilter)
      .then(setPlayers)
      .catch(console.error)
      .finally(() => setLoading(false));
  };

  const handleFilterChange = (next: PlayerFilter) => {
    setPlayerFilter({ ...next, limit: PAGE_SIZE, offset: 0 });
  };

  const goToPage = (offset: number) => {
    setPlayerFilter((prev) => ({ ...prev, offset }));
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
    fetchPlayers(playerFilter)
      .then(setPlayers)
      .catch(console.error)
      .finally(() => setLoading(false));
  }, [playerFilter]);

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
      <FilterBar
        filter={playerFilter}
        onChange={handleFilterChange}
        onRefresh={reload}
      />

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

      {/* Pagination */}
      {!loading && (
        <div className="d-flex justify-content-center align-items-center gap-3 mt-3">
          <button
            className="btn btn-sm btn-outline-secondary"
            disabled={(playerFilter.offset ?? 0) === 0}
            onClick={() =>
              goToPage(Math.max(0, (playerFilter.offset ?? 0) - PAGE_SIZE))
            }
          >
            &laquo; Previous
          </button>
          <span className="text-muted small">
            Page {Math.floor((playerFilter.offset ?? 0) / PAGE_SIZE) + 1}
          </span>
          {players.length >= PAGE_SIZE && (
            <button
              className="btn btn-sm btn-outline-secondary"
              onClick={() => goToPage((playerFilter.offset ?? 0) + PAGE_SIZE)}
            >
              Next &raquo;
            </button>
          )}
        </div>
      )}

      {/* Player detail modal */}
      {selectedPlayer && (
        <PlayerDetailModal
          player={selectedPlayer}
          aiLoading={aiLoading}
          onClose={closeModal}
          onStatusChange={handleStatusChange}
          onRequestAi={handleRequestAi}
        />
      )}
    </>
  );
}
