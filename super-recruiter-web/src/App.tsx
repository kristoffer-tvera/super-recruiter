import { useMemo } from "react";
import Dashboard from "./pages/Dashboard";
import "./App.css";

function getInitialRoute(): { playerId?: number } {
  const path = window.location.pathname;
  const match = path.match(/^\/players\/(\d+)$/);
  if (match) return { playerId: Number(match[1]) };
  return {};
}

function App() {
  const initial = useMemo(() => getInitialRoute(), []);

  return (
    <div>
      <nav className="navbar navbar-expand border-bottom">
        <div className="container-fluid">
          <span className="navbar-brand mb-0 h1">Super Recruiter</span>
        </div>
      </nav>
      <div className="container-fluid pt-3">
        <Dashboard initialPlayerId={initial.playerId} />
      </div>
    </div>
  );
}

export default App;
