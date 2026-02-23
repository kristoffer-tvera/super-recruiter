import { useState } from "react";
import Dashboard from "./pages/Dashboard";
import Blacklist from "./pages/Blacklist";
import "./App.css";

type Page = "dashboard" | "blacklist";

function App() {
  const [page, setPage] = useState<Page>("dashboard");

  return (
    <div>
      <nav
        style={{
          display: "flex",
          gap: "1rem",
          padding: "0.75rem 1rem",
          borderBottom: "1px solid #333",
          background: "#0a0a0a",
        }}
      >
        <button
          onClick={() => setPage("dashboard")}
          style={{ fontWeight: page === "dashboard" ? "bold" : "normal" }}
        >
          Dashboard
        </button>
        <button
          onClick={() => setPage("blacklist")}
          style={{ fontWeight: page === "blacklist" ? "bold" : "normal" }}
        >
          Blacklist
        </button>
      </nav>
      {page === "dashboard" ? <Dashboard /> : <Blacklist />}
    </div>
  );
}

export default App;
