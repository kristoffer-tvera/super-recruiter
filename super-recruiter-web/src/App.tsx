import { useState, useMemo } from "react";
import Dashboard from "./pages/Dashboard";
import Blacklist from "./pages/Blacklist";
import "./App.css";

type Page = "dashboard" | "blacklist";

function getInitialRoute(): { page: Page; playerId?: number } {
  const path = window.location.pathname;
  const match = path.match(/^\/players\/(\d+)$/);
  if (match) return { page: "dashboard", playerId: Number(match[1]) };
  if (path === "/blacklist") return { page: "blacklist" };
  return { page: "dashboard" };
}

function App() {
  const initial = useMemo(() => getInitialRoute(), []);
  const [page, setPage] = useState<Page>(initial.page);

  const navigate = (p: Page) => {
    setPage(p);
    window.history.pushState(null, "", p === "dashboard" ? "/" : `/${p}`);
  };

  return (
    <div>
      <nav className="navbar navbar-expand border-bottom">
        <div className="container-fluid">
          <span className="navbar-brand mb-0 h1">Super Recruiter</span>
          <ul className="navbar-nav">
            <li className="nav-item">
              <button
                className={`nav-link btn btn-link ${page === "dashboard" ? "active" : ""}`}
                onClick={() => navigate("dashboard")}
              >
                Dashboard
              </button>
            </li>
            <li className="nav-item">
              <button
                className={`nav-link btn btn-link ${page === "blacklist" ? "active" : ""}`}
                onClick={() => navigate("blacklist")}
              >
                Blacklist
              </button>
            </li>
          </ul>
        </div>
      </nav>
      <div className="container-fluid pt-3">
        {page === "dashboard" ? (
          <Dashboard initialPlayerId={initial.playerId} />
        ) : (
          <Blacklist />
        )}
      </div>
    </div>
  );
}

export default App;
