export enum PlayerStatus {
  New = 0,
  Interested = 1,
  Contacted = 2,
  Declined = 3,
  Blacklisted = 4,
}

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
  raiderIoDataJson?: string;
  warcraftLogsDataJson?: string;
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
