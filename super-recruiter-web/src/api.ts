import { type PlayerResponse, PlayerStatus, type PlayerFilter } from "./types";

const BASE = "/api";

export async function fetchPlayers(
  filter?: PlayerFilter,
): Promise<PlayerResponse[]> {
  const params = new URLSearchParams();
  if (filter?.status !== undefined) params.set("status", String(filter.status));
  if (filter?.playerClass) params.set("playerClass", filter.playerClass);
  if (filter?.limit) params.set("limit", String(filter.limit));
  if (filter?.offset) params.set("offset", String(filter.offset));

  const res = await fetch(`${BASE}/players?${params}`);
  if (!res.ok) throw new Error("Failed to fetch players");
  return res.json();
}

export async function fetchPlayer(id: number): Promise<PlayerResponse> {
  const res = await fetch(`${BASE}/players/${id}`);
  if (!res.ok) throw new Error("Failed to fetch player");
  return res.json();
}

export async function updatePlayerStatus(
  id: number,
  status: PlayerStatus,
): Promise<PlayerResponse> {
  const res = await fetch(`${BASE}/players/${id}/status`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ status }),
  });
  if (!res.ok) throw new Error("Failed to update player status");
  return res.json();
}

export async function requestAiSummary(id: number): Promise<PlayerResponse> {
  const res = await fetch(`${BASE}/players/${id}/ai-summary`, {
    method: "POST",
  });
  if (!res.ok) throw new Error("Failed to generate AI summary");
  return res.json();
}
