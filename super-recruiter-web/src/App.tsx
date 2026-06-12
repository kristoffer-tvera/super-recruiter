import { useMemo, useState } from "react";
import { getStoredApiKey, setStoredApiKey } from "./api";
import Dashboard from "./pages/Dashboard";
import "./App.css";

function getInitialRoute(): {
  realmSlug?: string;
  characterName?: string;
} {
  const path = window.location.pathname;
  // Match /{realmSlug}/{characterName} format
  const match = path.match(/^\/([a-z0-9-]+)\/([^/]+)$/);
  if (match) return { realmSlug: match[1], characterName: match[2] };
  return {};
}

function App() {
  const initial = useMemo(() => getInitialRoute(), []);
  const [hasApiKey, setHasApiKey] = useState(() => Boolean(getStoredApiKey()));

  function handleApiKeyPrompt(): void {
    const currentApiKey = getStoredApiKey();
    const input = window.prompt(
      "Enter API key (leave blank to clear):",
      currentApiKey,
    );

    if (input === null) {
      return;
    }

    setStoredApiKey(input);
    setHasApiKey(Boolean(getStoredApiKey()));
  }

  return (
    <div>
      <nav className="navbar navbar-expand border-bottom">
        <div className="container-fluid">
          <span className="navbar-brand mb-0 h1">Super Recruiter</span>
        </div>
      </nav>
      <div className="container-fluid pt-3">
        <Dashboard
          initialRealmSlug={initial.realmSlug}
          initialCharacterName={initial.characterName}
        />
      </div>
      <button
        type="button"
        className="api-key-lock"
        onClick={handleApiKeyPrompt}
        title={hasApiKey ? "API key set" : "Set API key"}
        aria-label={hasApiKey ? "API key set" : "Set API key"}
      >
        {hasApiKey ? "🔓" : "🔒"}
      </button>
    </div>
  );
}

export default App;
