import { useEffect, useState } from "react";
import { fetchAdminConfig, updateAdminConfig } from "../api";
import { type AdminConfig } from "../types";

const WOW_CLASSES = [
  "Death Knight",
  "Demon Hunter",
  "Druid",
  "Evoker",
  "Hunter",
  "Mage",
  "Monk",
  "Paladin",
  "Priest",
  "Rogue",
  "Shaman",
  "Warlock",
  "Warrior",
] as const;

function AdminConfigPage() {
  const [config, setConfig] = useState<AdminConfig>({
    bossKills: 0,
    acceptedClasses: [],
  });
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    fetchAdminConfig()
      .then(setConfig)
      .catch(() => setError("Failed to load config"))
      .finally(() => setLoading(false));
  }, []);

  function toggleClass(cls: string): void {
    const lower = cls.toLowerCase();
    setConfig((prev) => ({
      ...prev,
      acceptedClasses: prev.acceptedClasses.includes(lower)
        ? prev.acceptedClasses.filter((c) => c !== lower)
        : [...prev.acceptedClasses, lower],
    }));
    setSaved(false);
  }

  async function handleSave(): Promise<void> {
    setSaving(true);
    setError(null);
    setSaved(false);
    try {
      const updated = await updateAdminConfig(config);
      setConfig(updated);
      setSaved(true);
    } catch {
      setError("Failed to save config");
    } finally {
      setSaving(false);
    }
  }

  if (loading) {
    return (
      <div className="text-center py-5">
        <div className="spinner-border" role="status">
          <span className="visually-hidden">Loading...</span>
        </div>
      </div>
    );
  }

  return (
    <div className="row justify-content-center">
      <div className="col-lg-8">
        <h2 className="mb-4">Admin Config</h2>

        <div className="card mb-4">
          <div className="card-header">Filter Settings</div>
          <div className="card-body">
            <div className="mb-4">
              <label htmlFor="bossKills" className="form-label fw-semibold">
                Minimum Mythic Bosses Killed
              </label>
              <input
                id="bossKills"
                type="number"
                className="form-control"
                style={{ maxWidth: 120 }}
                min={0}
                value={config.bossKills}
                onChange={(e) => {
                  setConfig((prev) => ({
                    ...prev,
                    bossKills: Math.max(0, Number(e.target.value)),
                  }));
                  setSaved(false);
                }}
              />
              <div className="form-text">
                Players with fewer mythic boss kills will be filtered out.
              </div>
            </div>

            <div>
              <div className="fw-semibold mb-1">Accepted Classes</div>
              <div className="form-text mb-3">
                Only players of these classes will pass through the filter. If
                none are selected, all classes are accepted.
              </div>
              <div className="row g-2">
                {WOW_CLASSES.map((cls) => {
                  const lower = cls.toLowerCase();
                  const checked = config.acceptedClasses.includes(lower);
                  return (
                    <div key={cls} className="col-6 col-sm-4 col-md-3">
                      <div className="form-check">
                        <input
                          className="form-check-input"
                          type="checkbox"
                          id={`class-${lower.replace(" ", "-")}`}
                          checked={checked}
                          onChange={() => toggleClass(cls)}
                        />
                        <label
                          className="form-check-label"
                          htmlFor={`class-${lower.replace(" ", "-")}`}
                        >
                          {cls}
                        </label>
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>
          </div>
        </div>

        {error && (
          <div className="alert alert-danger" role="alert">
            {error}
          </div>
        )}
        {saved && (
          <div className="alert alert-success" role="alert">
            Config saved successfully.
          </div>
        )}

        <button
          type="button"
          className="btn btn-primary"
          onClick={handleSave}
          disabled={saving}
        >
          {saving ? "Saving..." : "Save Config"}
        </button>
      </div>
    </div>
  );
}

export default AdminConfigPage;
