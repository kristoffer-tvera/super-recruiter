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

export interface PlayerFilter {
    statuses?: PlayerStatus[];
    classes?: string[];
    minItemLevel?: number;
    minMythicKills?: number;
    limit?: number;
    offset?: number;
}

export interface AdminConfig {
    bossKills: number;
    acceptedClasses: string[];
}

export const WOW_CLASSES = ["Death Knight", "Demon Hunter", "Druid", "Evoker", "Hunter", "Mage", "Monk", "Paladin", "Priest", "Rogue", "Shaman", "Warlock", "Warrior"] as const;

export const ALL_STATUS_VALUES = [PlayerStatus.New, PlayerStatus.Interested, PlayerStatus.Contacted, PlayerStatus.Declined, PlayerStatus.Blacklisted] as const;
