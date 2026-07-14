import { useMemo, useState } from "react";
import { getStoredApiKey, setStoredApiKey } from "./api";
import Dashboard from "./pages/Dashboard";
import AdminConfig from "./pages/AdminConfig";
import "./App.css";

type Page = "dashboard" | "admin";

function getInitialRoute(): {
  page: Page;
  realmSlug?: string;
  characterName?: string;
} {
  const path = window.location.pathname;
  if (path === "/admin") return { page: "admin" };
  // Match /{realmSlug}/{characterName} format
  const match = path.match(/^\/([a-z0-9-]+)\/([^/]+)$/);
  if (match)
    return { page: "dashboard", realmSlug: match[1], characterName: match[2] };
  return { page: "dashboard" };
}

function App() {
  const initial = useMemo(() => getInitialRoute(), []);
  const [page, setPage] = useState<Page>(initial.page);
  const [hasApiKey, setHasApiKey] = useState(() => Boolean(getStoredApiKey()));

  function navigate(to: Page, url: string): void {
    history.pushState(null, "", url);
    setPage(to);
  }

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
          <div className="navbar-nav ms-3">
            <a
              className={`nav-link${page === "dashboard" ? " active" : ""}`}
              href="/"
              onClick={(e) => {
                e.preventDefault();
                navigate("dashboard", "/");
              }}
            >
              Dashboard
            </a>
            <a
              className={`nav-link${page === "admin" ? " active" : ""}`}
              href="/admin"
              onClick={(e) => {
                e.preventDefault();
                navigate("admin", "/admin");
              }}
            >
              Admin
            </a>
          </div>
        </div>
      </nav>
      <div className="container-fluid pt-3">
        {page === "admin" ? (
          <AdminConfig />
        ) : (
          <Dashboard
            initialRealmSlug={initial.realmSlug}
            initialCharacterName={initial.characterName}
          />
        )}
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
