import { useState } from "react";
import Dashboard from "./pages/Dashboard";
import Blacklist from "./pages/Blacklist";
import "./App.css";

type Page = "dashboard" | "blacklist";

function App() {
  const [page, setPage] = useState<Page>("dashboard");

  return (
    <div data-bs-theme="dark">
      <nav className="navbar navbar-expand navbar-dark bg-dark border-bottom border-secondary">
        <div className="container-fluid">
          <span className="navbar-brand mb-0 h1">Super Recruiter</span>
          <ul className="navbar-nav">
            <li className="nav-item">
              <button
                className={`nav-link btn btn-link ${page === "dashboard" ? "active" : ""}`}
                onClick={() => setPage("dashboard")}
              >
                Dashboard
              </button>
            </li>
            <li className="nav-item">
              <button
                className={`nav-link btn btn-link ${page === "blacklist" ? "active" : ""}`}
                onClick={() => setPage("blacklist")}
              >
                Blacklist
              </button>
            </li>
          </ul>
        </div>
      </nav>
      <div className="container-fluid mt-3">
        {page === "dashboard" ? <Dashboard /> : <Blacklist />}
      </div>
    </div>
  );
}

export default App;
