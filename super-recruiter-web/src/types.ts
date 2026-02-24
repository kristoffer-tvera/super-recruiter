export const PlayerStatus = {
  New: 0,
  Interested: 1,
  Contacted: 2,
  Declined: 3,
  Blacklisted: 4,
} as const;

export type PlayerStatus = (typeof PlayerStatus)[keyof typeof PlayerStatus];

export interface PlayerResponse {
  id: number;
  characterName: string;
  class: string;
  realm: string;
  realmSlug: string;
  itemLevel: number;
  lastUpdated: string;
  characterUrl: string;
  battleTag?: string;
  bio?: string;
  languages?: string;
  specsPlaying?: string;
  guildHistory: string[];
  raiderIoSummary?: string;
  warcraftLogsSummary?: string;
  geminiTake?: string;
  status: PlayerStatus;
  discordMessageId?: number;
  discordChannelId?: number;
  createdAt: string;
  updatedAt: string;
}

export interface BlacklistEntry {
  id: number;
  characterName: string;
  realm: string;
  reason?: string;
  blacklistedAt: string;
}
