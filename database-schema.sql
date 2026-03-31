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

-- Example: View recently seen players
-- SELECT * FROM seen_players ORDER BY last_seen_at DESC LIMIT 100;
