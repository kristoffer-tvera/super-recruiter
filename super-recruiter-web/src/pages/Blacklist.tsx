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

    setName("");
    setRealm("");
    setReason("");
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
    <>
      <h4 className="mb-3">Blacklist</h4>

      <form onSubmit={handleAdd} className="row g-2 mb-4 align-items-end">
        <div className="col-auto">
          <input
            className="form-control form-control-sm"
            placeholder="Character name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
          />
        </div>
        <div className="col-auto">
          <input
            className="form-control form-control-sm"
            placeholder="Realm"
            value={realm}
            onChange={(e) => setRealm(e.target.value)}
            required
          />
        </div>
        <div className="col-auto">
          <input
            className="form-control form-control-sm"
            placeholder="Reason (optional)"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
          />
        </div>
        <div className="col-auto">
          <button type="submit" className="btn btn-danger btn-sm">
            Add
          </button>
        </div>
      </form>

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
                <th>Character</th>
                <th>Realm</th>
                <th>Reason</th>
                <th>Date</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {entries.map((e) => (
                <tr key={e.id}>
                  <td>{e.characterName}</td>
                  <td>{e.realm}</td>
                  <td>{e.reason ?? "—"}</td>
                  <td className="text-muted small">
                    {new Date(e.blacklistedAt).toLocaleDateString()}
                  </td>
                  <td>
                    <button
                      className="btn btn-outline-danger btn-sm"
                      onClick={() => handleRemove(e.id)}
                    >
                      Remove
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {entries.length === 0 && (
            <p className="text-center text-muted">No blacklisted players</p>
          )}
        </div>
      )}
    </>
  );
}
