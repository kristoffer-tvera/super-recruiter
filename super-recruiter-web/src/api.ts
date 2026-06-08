import { type PlayerResponse, PlayerStatus, type PlayerFilter } from "./types";

const BASE = "/api";
export const API_KEY_STORAGE_KEY = "superRecruiterApiKey";

export function getStoredApiKey(): string {
  return localStorage.getItem(API_KEY_STORAGE_KEY)?.trim() ?? "";
}

export function setStoredApiKey(value: string): void {
  const trimmed = value.trim();
  if (!trimmed) {
    localStorage.removeItem(API_KEY_STORAGE_KEY);
    return;
  }

  localStorage.setItem(API_KEY_STORAGE_KEY, trimmed);
}

function withApiKey(headers?: HeadersInit): Headers {
  const merged = new Headers(headers);
  const apiKey = getStoredApiKey();
  if (apiKey) {
    merged.set("X-Api-Key", apiKey);
  }
  return merged;
}

export async function fetchPlayers(
  filter?: PlayerFilter,
): Promise<PlayerResponse[]> {
  const params = new URLSearchParams();
  if (filter?.status !== undefined) params.set("status", String(filter.status));
  if (filter?.playerClass) params.set("playerClass", filter.playerClass);
  if (filter?.limit) params.set("limit", String(filter.limit));
  if (filter?.offset) params.set("offset", String(filter.offset));

  const res = await fetch(`${BASE}/players?${params}`, {
    headers: withApiKey(),
  });
  if (!res.ok) throw new Error("Failed to fetch players");
  return res.json();
}

export async function fetchPlayer(id: number): Promise<PlayerResponse> {
  const res = await fetch(`${BASE}/players/${id}`, {
    headers: withApiKey(),
  });
  if (!res.ok) throw new Error("Failed to fetch player");
  return res.json();
}

export async function fetchPlayerByCharacterAndRealm(
  realmSlug: string,
  characterName: string,
): Promise<PlayerResponse> {
  const res = await fetch(
    `${BASE}/players/lookup/${encodeURIComponent(realmSlug)}/${encodeURIComponent(characterName)}`,
    {
      headers: withApiKey(),
    },
  );
  if (!res.ok) throw new Error("Failed to fetch player");
  return res.json();
}

export async function updatePlayerStatus(
  id: number,
  status: PlayerStatus,
): Promise<PlayerResponse> {
  const res = await fetch(`${BASE}/players/${id}/status`, {
    method: "PUT",
    headers: withApiKey({ "Content-Type": "application/json" }),
    body: JSON.stringify({ status }),
  });
  if (!res.ok) throw new Error("Failed to update player status");
  return res.json();
}

export async function requestAiSummary(id: number): Promise<PlayerResponse> {
  const res = await fetch(`${BASE}/players/${id}/ai-summary`, {
    method: "POST",
    headers: withApiKey(),
  });
  if (!res.ok) throw new Error("Failed to generate AI summary");
  return res.json();
}
