-- Super Recruiter Database Schema
-- PostgreSQL Database Tables

-- Table for tracking seen players
CREATE TABLE
IF NOT EXISTS seen_players
(
    id SERIAL PRIMARY KEY,
    character_name VARCHAR
(255) NOT NULL,
    realm VARCHAR
(255) NOT NULL,
    first_seen_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_seen_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT unique_player UNIQUE
(character_name, realm)
);

CREATE INDEX
IF NOT EXISTS idx_seen_players_lookup 
ON seen_players
(character_name, realm);

CREATE INDEX
IF NOT EXISTS idx_seen_players_last_seen 
ON seen_players
(last_seen_at);

-- Table for blacklisted players
CREATE TABLE
IF NOT EXISTS blacklisted_players
(
    id SERIAL PRIMARY KEY,
    character_name VARCHAR
(255) NOT NULL,
    realm VARCHAR
(255) NOT NULL,
    reason TEXT,
    blacklisted_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT unique_blacklisted_player UNIQUE
(character_name, realm)
);

CREATE INDEX
IF NOT EXISTS idx_blacklisted_players_lookup 
ON blacklisted_players
(character_name, realm);

-- Example: Add a player to the blacklist
-- INSERT INTO blacklisted_players (character_name, realm, reason)
-- VALUES ('PlayerName', 'RealmName', 'Reason for blacklisting');

-- Example: Remove a player from the blacklist
-- DELETE FROM blacklisted_players 
-- WHERE character_name = 'PlayerName' AND realm = 'RealmName';

-- Example: View all blacklisted players
-- SELECT * FROM blacklisted_players ORDER BY blacklisted_at DESC;

-- Example: View recently seen players
-- SELECT * FROM seen_players ORDER BY last_seen_at DESC LIMIT 100;
