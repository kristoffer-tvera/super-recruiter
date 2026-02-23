import {
  type PlayerResponse,
  PlayerStatus,
  type BlacklistEntry,
} from "./types";

const BASE = "/api";

export async function fetchPlayers(
  status?: PlayerStatus,
): Promise<PlayerResponse[]> {
  const params = new URLSearchParams();
  if (status !== undefined) params.set("status", String(status));
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

export async function fetchBlacklist(): Promise<BlacklistEntry[]> {
  const res = await fetch(`${BASE}/blacklist`);
  if (!res.ok) throw new Error("Failed to fetch blacklist");
  return res.json();
}

export async function addToBlacklist(
  characterName: string,
  realm: string,
  reason?: string,
): Promise<void> {
  const res = await fetch(`${BASE}/blacklist`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ characterName, realm, reason }),
  });
  if (!res.ok) throw new Error("Failed to add to blacklist");
}

export async function removeFromBlacklist(id: number): Promise<void> {
  const res = await fetch(`${BASE}/blacklist/${id}`, { method: "DELETE" });
  if (!res.ok) throw new Error("Failed to remove from blacklist");
}
