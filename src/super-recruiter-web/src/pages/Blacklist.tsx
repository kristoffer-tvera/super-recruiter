import { useEffect, useState } from "react";
import { addToBlacklist, fetchBlacklist, removeFromBlacklist } from "../api";
import { type BlacklistEntry } from "../types";

export default function Blacklist() {
  const [entries, setEntries] = useState<BlacklistEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [name, setName] = useState("");
  const [realm, setRealm] = useState("");
  const [reason, setReason] = useState("");

  useEffect(() => {
    fetchBlacklist()
      .then(setEntries)
      .catch(console.error)
      .finally(() => setLoading(false));
  }, []);

  const handleAdd = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim() || !realm.trim()) return;

    addToBlacklist(name.trim(), realm.trim(), reason.trim() || undefined)
      .then(() => fetchBlacklist())
      .then(setEntries)
      .catch(console.error);
  };

  const handleRemove = async (id: number) => {
    try {
      await removeFromBlacklist(id);
      setEntries((prev) => prev.filter((e) => e.id !== id));
    } catch (err) {
      console.error(err);
    }
  };

  return (
    <div style={{ padding: "1rem" }}>
      <h1>Blacklist</h1>

      <form
        onSubmit={handleAdd}
        style={{
          display: "flex",
          gap: "0.5rem",
          marginBottom: "1rem",
          flexWrap: "wrap",
        }}
      >
        <input
          placeholder="Character name"
          value={name}
          onChange={(e) => setName(e.target.value)}
          required
        />
        <input
          placeholder="Realm"
          value={realm}
          onChange={(e) => setRealm(e.target.value)}
          required
        />
        <input
          placeholder="Reason (optional)"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
        />
        <button type="submit">Add</button>
      </form>

      {loading ? (
        <p>Loading...</p>
      ) : (
        <table style={{ width: "100%", borderCollapse: "collapse" }}>
          <thead>
            <tr style={{ borderBottom: "2px solid #333" }}>
              <th style={{ textAlign: "left", padding: "0.5rem" }}>
                Character
              </th>
              <th style={{ textAlign: "left", padding: "0.5rem" }}>Realm</th>
              <th style={{ textAlign: "left", padding: "0.5rem" }}>Reason</th>
              <th style={{ textAlign: "left", padding: "0.5rem" }}>Date</th>
              <th style={{ textAlign: "left", padding: "0.5rem" }}></th>
            </tr>
          </thead>
          <tbody>
            {entries.map((e) => (
              <tr key={e.id} style={{ borderBottom: "1px solid #222" }}>
                <td style={{ padding: "0.5rem" }}>{e.characterName}</td>
                <td style={{ padding: "0.5rem" }}>{e.realm}</td>
                <td style={{ padding: "0.5rem" }}>{e.reason ?? "—"}</td>
                <td style={{ padding: "0.5rem", fontSize: "0.85rem" }}>
                  {new Date(e.blacklistedAt).toLocaleDateString()}
                </td>
                <td style={{ padding: "0.5rem" }}>
                  <button
                    onClick={() => handleRemove(e.id)}
                    style={{ color: "#ef4444" }}
                  >
                    Remove
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {!loading && entries.length === 0 && (
        <p style={{ color: "#888" }}>No blacklisted players</p>
      )}
    </div>
  );
}
