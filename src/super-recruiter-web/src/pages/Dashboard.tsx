import { useEffect, useState } from "react";
import { fetchPlayers, updatePlayerStatus } from "../api";
import { type PlayerResponse, PlayerStatus } from "../types";

const STATUS_LABELS: Record<PlayerStatus, string> = {
  [PlayerStatus.New]: "New",
  [PlayerStatus.Interested]: "Interested",
  [PlayerStatus.Contacted]: "Contacted",
  [PlayerStatus.Declined]: "Declined",
  [PlayerStatus.Blacklisted]: "Blacklisted",
};

const STATUS_COLORS: Record<PlayerStatus, string> = {
  [PlayerStatus.New]: "#3b82f6",
  [PlayerStatus.Interested]: "#22c55e",
  [PlayerStatus.Contacted]: "#a855f7",
  [PlayerStatus.Declined]: "#6b7280",
  [PlayerStatus.Blacklisted]: "#ef4444",
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

export default function Dashboard() {
  const [players, setPlayers] = useState<PlayerResponse[]>([]);
  const [filter, setFilter] = useState<PlayerStatus | undefined>(undefined);
  const [loading, setLoading] = useState(true);
  const [selectedPlayer, setSelectedPlayer] = useState<PlayerResponse | null>(
    null,
  );

  const reload = () => {
    setLoading(true);
    fetchPlayers(filter)
      .then(setPlayers)
      .catch(console.error)
      .finally(() => setLoading(false));
  };

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

  return (
    <div style={{ padding: "1rem" }}>
      <h1>Super Recruiter</h1>

      <div
        style={{
          marginBottom: "1rem",
          display: "flex",
          gap: "0.5rem",
          flexWrap: "wrap",
        }}
      >
        <button
          onClick={() => setFilter(undefined)}
          style={{ fontWeight: filter === undefined ? "bold" : "normal" }}
        >
          All
        </button>
        {Object.entries(STATUS_LABELS).map(([key, label]) => (
          <button
            key={key}
            onClick={() => setFilter(Number(key) as PlayerStatus)}
            style={{
              fontWeight: filter === Number(key) ? "bold" : "normal",
              color: STATUS_COLORS[Number(key) as PlayerStatus],
            }}
          >
            {label}
          </button>
        ))}
        <button onClick={reload} style={{ marginLeft: "auto" }}>
          Refresh
        </button>
      </div>

      {loading ? (
        <p>Loading...</p>
      ) : (
        <div style={{ display: "flex", gap: "1rem" }}>
          <div style={{ flex: 1 }}>
            <table style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr style={{ borderBottom: "2px solid #333" }}>
                  <th style={{ textAlign: "left", padding: "0.5rem" }}>
                    Player
                  </th>
                  <th style={{ textAlign: "left", padding: "0.5rem" }}>
                    Class
                  </th>
                  <th style={{ textAlign: "left", padding: "0.5rem" }}>
                    Realm
                  </th>
                  <th style={{ textAlign: "right", padding: "0.5rem" }}>
                    iLvl
                  </th>
                  <th style={{ textAlign: "left", padding: "0.5rem" }}>
                    Status
                  </th>
                  <th style={{ textAlign: "left", padding: "0.5rem" }}>
                    Found
                  </th>
                  <th style={{ textAlign: "left", padding: "0.5rem" }}>
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody>
                {players.map((p) => (
                  <tr
                    key={p.id}
                    onClick={() => setSelectedPlayer(p)}
                    style={{
                      borderBottom: "1px solid #222",
                      cursor: "pointer",
                      background:
                        selectedPlayer?.id === p.id ? "#1a1a2e" : "transparent",
                    }}
                  >
                    <td style={{ padding: "0.5rem" }}>{p.characterName}</td>
                    <td
                      style={{
                        padding: "0.5rem",
                        color: CLASS_COLORS[p.class?.toLowerCase()] ?? "#fff",
                      }}
                    >
                      {p.class}
                    </td>
                    <td style={{ padding: "0.5rem" }}>{p.realm}</td>
                    <td style={{ padding: "0.5rem", textAlign: "right" }}>
                      {p.itemLevel.toFixed(1)}
                    </td>
                    <td
                      style={{
                        padding: "0.5rem",
                        color: STATUS_COLORS[p.status],
                        fontWeight: "bold",
                      }}
                    >
                      {STATUS_LABELS[p.status]}
                    </td>
                    <td style={{ padding: "0.5rem", fontSize: "0.85rem" }}>
                      {new Date(p.createdAt).toLocaleDateString()}
                    </td>
                    <td style={{ padding: "0.5rem" }}>
                      <select
                        value={p.status}
                        onClick={(e) => e.stopPropagation()}
                        onChange={(e) =>
                          handleStatusChange(
                            p.id,
                            Number(e.target.value) as PlayerStatus,
                          )
                        }
                        style={{ padding: "0.25rem" }}
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
              <p style={{ textAlign: "center", color: "#888" }}>
                No players found
              </p>
            )}
          </div>

          {selectedPlayer && (
            <div
              style={{
                flex: 1,
                maxWidth: "500px",
                padding: "1rem",
                border: "1px solid #333",
                borderRadius: "8px",
                maxHeight: "80vh",
                overflow: "auto",
              }}
            >
              <h2
                style={{
                  color:
                    CLASS_COLORS[selectedPlayer.class?.toLowerCase()] ?? "#fff",
                }}
              >
                {selectedPlayer.characterName}-{selectedPlayer.realm}
              </h2>
              <p>
                <strong>Class:</strong> {selectedPlayer.class} |{" "}
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

              <div
                style={{
                  display: "flex",
                  gap: "0.5rem",
                  margin: "0.5rem 0",
                  flexWrap: "wrap",
                }}
              >
                <a
                  href={`https://worldofwarcraft.blizzard.com/en-gb/character/eu/${selectedPlayer.realmSlug}/${selectedPlayer.characterName}`}
                  target="_blank"
                  rel="noreferrer"
                >
                  Armory
                </a>
                <a
                  href={selectedPlayer.characterUrl}
                  target="_blank"
                  rel="noreferrer"
                >
                  WoWProgress
                </a>
                <a
                  href={`https://www.warcraftlogs.com/character/eu/${selectedPlayer.realmSlug}/${selectedPlayer.characterName}`}
                  target="_blank"
                  rel="noreferrer"
                >
                  WCL
                </a>
              </div>

              {selectedPlayer.bio && (
                <>
                  <h3>Bio</h3>
                  <p style={{ fontSize: "0.9rem", whiteSpace: "pre-wrap" }}>
                    {selectedPlayer.bio}
                  </p>
                </>
              )}

              {selectedPlayer.geminiTake && (
                <>
                  <h3>AI Evaluation</h3>
                  <pre
                    style={{
                      fontSize: "0.85rem",
                      whiteSpace: "pre-wrap",
                      background: "#111",
                      padding: "0.5rem",
                      borderRadius: "4px",
                    }}
                  >
                    {selectedPlayer.geminiTake}
                  </pre>
                </>
              )}

              {selectedPlayer.guildHistory?.length > 0 && (
                <>
                  <h3>Guild History</h3>
                  <ul style={{ fontSize: "0.85rem", paddingLeft: "1.2rem" }}>
                    {selectedPlayer.guildHistory.slice(0, 15).map((g, i) => (
                      <li key={i}>{g}</li>
                    ))}
                  </ul>
                </>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
