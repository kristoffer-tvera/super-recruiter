import { PlayerStatus } from "./types";

export const STATUS_LABELS: Record<PlayerStatus, string> = {
  [PlayerStatus.New]: "New",
  [PlayerStatus.Interested]: "Interested",
  [PlayerStatus.Contacted]: "Contacted",
  [PlayerStatus.Declined]: "Declined",
  [PlayerStatus.Blacklisted]: "Blacklisted",
};

export const STATUS_BADGE: Record<PlayerStatus, string> = {
  [PlayerStatus.New]: "bg-primary",
  [PlayerStatus.Interested]: "bg-success",
  [PlayerStatus.Contacted]: "bg-info",
  [PlayerStatus.Declined]: "bg-secondary",
  [PlayerStatus.Blacklisted]: "bg-danger",
};

export const CLASS_COLORS: Record<string, string> = {
  "death knight": "#C41F3B",
  "demon hunter": "#A330C9",
  druid: "#FF7D0A",
  evoker: "#33937F",
  hunter: "#ABD473",
  mage: "#69CCF0",
  monk: "#00FF96",
  paladin: "#F58CBA",
  priest: "#FFFFFF",
  rogue: "#FFF569",
  shaman: "#0070DE",
  warlock: "#9482C9",
  warrior: "#C79C6E",
};

export const CLASS_ICONS: Record<string, string> = {
  "death knight":
    "https://render.worldofwarcraft.com/eu/icons/56/classicon_deathknight.jpg",
  deathknight:
    "https://render.worldofwarcraft.com/eu/icons/56/classicon_deathknight.jpg",
  "demon hunter":
    "https://render.worldofwarcraft.com/eu/icons/56/classicon_demonhunter.jpg",
  druid: "https://render.worldofwarcraft.com/eu/icons/56/classicon_druid.jpg",
  evoker: "https://render.worldofwarcraft.com/eu/icons/56/classicon_evoker.jpg",
  hunter: "https://render.worldofwarcraft.com/eu/icons/56/classicon_hunter.jpg",
  mage: "https://render.worldofwarcraft.com/eu/icons/56/classicon_mage.jpg",
  monk: "https://render.worldofwarcraft.com/eu/icons/56/classicon_monk.jpg",
  paladin:
    "https://render.worldofwarcraft.com/eu/icons/56/classicon_paladin.jpg",
  priest: "https://render.worldofwarcraft.com/eu/icons/56/classicon_priest.jpg",
  rogue: "https://render.worldofwarcraft.com/eu/icons/56/classicon_rogue.jpg",
  shaman: "https://render.worldofwarcraft.com/eu/icons/56/classicon_shaman.jpg",
  warlock:
    "https://render.worldofwarcraft.com/eu/icons/56/classicon_warlock.jpg",
  warrior:
    "https://render.worldofwarcraft.com/eu/icons/56/classicon_warrior.jpg",
};
