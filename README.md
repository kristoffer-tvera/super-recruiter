# Super Recruiter

Super Recruiter is a three-project .NET 10 solution plus a React frontend for discovering LFG players, enriching their profile data, storing records in PostgreSQL, and reviewing/managing them in a web UI.

## Architecture

- `SuperRecruiter.Api`: ASP.NET Core minimal API + Dapper + PostgreSQL
- `SuperRecruiter.Worker`: background scraper/enricher + Discord bot notifications
- `SuperRecruiter.Shared`: DTOs/models shared by API and Worker
- `super-recruiter-web`: React 19 + TypeScript + Vite frontend

## What It Does

- Scrapes WoWProgress and Raider.IO for LFG players
- Enriches player data with Raider.IO and WarcraftLogs summaries
- Stores and upserts enriched player records in PostgreSQL
- Tracks seen players and relisting timestamps
- Sends Discord messages for new/updated players
- Provides a web dashboard for status management and AI summary generation

## Security Model

- API routes under `/players` require `X-Api-Key`
- API key is configured in API config as `ApiKey`
- Worker sends key from `SuperRecruiterApi:ApiKey`
- Frontend stores key in browser localStorage via the lock button in the bottom-right corner

## Configuration

Configure local development values in each project's `appsettings.Development.json`.

### API (`SuperRecruiter.Api`)

Required:

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=...;Database=...;Username=...;Password=..."
  },
  "ApiKey": "your-api-key"
}
```

Optional:

- `Gemini:Url`
- `Gemini:ApiKey`

### Worker (`SuperRecruiter.Worker`)

Required:

```json
{
  "PollingIntervalMinutes": 30,
  "SuperRecruiterApi": {
    "BaseUrl": "http://localhost:5100",
    "ApiKey": "your-api-key"
  },
  "Discord": {
    "BotToken": "...",
    "ChannelId": "..."
  },
  "RaiderIO": {
    "ApiKey": "..."
  },
  "WarcraftLogs": {
    "ClientId": "...",
    "ClientSecret": "..."
  },
  "FlareSolverrUrl": "http://localhost:8191/v1"
}
```

## Running Locally

Run each service in its own terminal.

### 1) API

```bash
cd SuperRecruiter.Api
dotnet run
```

The API docs are available at:

- `http://localhost:5100/scalar/v1`

### 2) Worker

```bash
cd SuperRecruiter.Worker
dotnet run
```

### 3) Frontend

```bash
cd super-recruiter-web
npm install
npm run dev
```

On first load, click the lock icon in the bottom-right corner and paste your API key.

## Database Notes

- Database schema is initialized by `PlayerDatabaseService.InitializeDatabaseAsync()` on API startup
- Main tables:
  - `players`
  - `seen_players`
- Player status is stored on `players.status` (including blacklisted state)

## Useful Commands

```bash
# Build all projects
dotnet build

# Frontend lint
cd super-recruiter-web && npm run lint

# Frontend production build
cd super-recruiter-web && npm run build
```

## Important

- Do not commit secrets to source control.
- Keep API keys/tokens in development config or user secrets.
