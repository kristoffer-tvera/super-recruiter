import Markdown from "react-markdown";
import { type PlayerResponse, PlayerStatus } from "../types";
import { STATUS_LABELS, CLASS_COLORS } from "../constants";

interface PlayerDetailModalProps {
  player: PlayerResponse;
  aiLoading: boolean;
  onClose: () => void;
  onStatusChange: (
    realmSlug: string,
    characterName: string,
    status: PlayerStatus,
  ) => void;
  onRequestAi: () => void;
}

export default function PlayerDetailModal({
  player,
  aiLoading,
  onClose,
  onStatusChange,
  onRequestAi,
}: PlayerDetailModalProps) {
  return (
    <>
      <div className="modal fade show d-block" tabIndex={-1} onClick={onClose}>
        <div
          className="modal-dialog modal-xl modal-dialog-scrollable"
          onClick={(e) => e.stopPropagation()}
        >
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
                <strong>Class:</strong> {player.class} &middot;{" "}
                <strong>iLvl:</strong> {player.itemLevel.toFixed(1)}
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
                <a
                  className="btn btn-sm btn-outline-info"
                  href={player.characterUrl}
                  target="_blank"
                  rel="noreferrer"
                >
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
                {player.geminiTake ? (
                  <div className="card">
                    <div className="card-body small">
                      <Markdown>{player.geminiTake}</Markdown>
                    </div>
                  </div>
                ) : (
                  <button
                    className="btn btn-outline-warning btn-sm"
                    disabled={aiLoading}
                    onClick={onRequestAi}
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
                value={player.status}
                onChange={(e) =>
                  onStatusChange(
                    player.realmSlug,
                    player.characterName,
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
