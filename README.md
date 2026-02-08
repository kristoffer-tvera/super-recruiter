# Super Recruiter

A .NET Worker Service that monitors WoWProgress for players looking for guilds and sends detailed notifications to Discord with RaiderIO and WarcraftLogs data.

## Features

- Scrapes WoWProgress using PuppeteerSharp (bypasses Cloudflare protection)
- Fetches player data from RaiderIO and WarcraftLogs APIs
- PostgreSQL database for player tracking and blacklist
- Discord webhook notifications with rich player stats
- Tracks player re-listings based on update timestamps

## Quick Start

1. Configure `appsettings.Development.json`:

   ```json
   {
     "ConnectionStrings": {
       "PostgreSQL": "your-postgres-connection-string"
     },
     "Discord": {
       "WebhookUrl": "your-discord-webhook-url"
     },
     "RaiderIO": {
       "ApiKey": "your-raiderio-key"
     },
     "WarcraftLogs": {
       "ClientId": "your-client-id",
       "ClientSecret": "your-client-secret"
     }
   }
   ```

2. Run:
   ```bash
   dotnet run
   ```

## Database

Tables are created automatically on startup:

- `seen_players` - Tracks players and their last update time
- `blacklisted_players` - Players to ignore

Add to blacklist:

```sql
INSERT INTO blacklisted_players (character_name, realm, reason)
VALUES ('PlayerName', 'RealmName', 'Optional reason');
```

## Configuration

- `PollingIntervalMinutes` - Scan interval (default: 10)

## Project Structure

```
├── Models/           # Data models (Player, RaidProgression, etc.)
├── Services/         # WoWProgress scraper, API clients, Discord webhook
├── Converter/        # JSON converters for API responses
└── Worker.cs         # Main polling loop
```
