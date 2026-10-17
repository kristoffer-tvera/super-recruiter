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

- API routes under `/api/` require `X-Api-Key`; `/api/health` and Scalar/OpenAPI documentation are public
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

## Database Migrations

### Add Mythic Kill Count Column

If you have an existing database and need to add the `current_tier_mythic_kill_count` column, run this migration:

```sql
ALTER TABLE players
ADD COLUMN current_tier_mythic_kill_count INTEGER NOT NULL DEFAULT 0;
```

Run this directly in PostgreSQL via `psql`, pgAdmin, or any SQL client connected to your Super Recruiter database.

## Production Deployment

Production uses the GitHub Actions workflow in `.github/workflows/deploy.yml` and the shared droplet deployment helper. The Compose project/site ID is `super-recruiter`; the sole hostname is `recruitment.amusedtodeath.eu`. Host ports `18050` (web) and `18051` (API) are loopback-only. The scraper and Discord worker remain on Unraid and are not deployed to the droplet.

Before the first release:

- Provision the shared droplet and deployment helper, then confirm its Docker bridge can use `172.30.50.0/24` without overlapping the host, VPC, or other projects. If it cannot, change both the Compose subnet and the API's trusted forwarded-header network together.
- Point the hostname's A/AAAA records at the droplet and allow public HTTP/HTTPS traffic. Keep DNS pointed at the droplet for HTTP-01 certificate issuance and renewal.
- Allowlist the droplet's stable public IP at the existing external PostgreSQL provider. Supply a connection string that verifies TLS; do not move the database credentials into GitHub Actions.
- Verify the existing database schema before cutover. API startup creates missing tables but does not migrate existing tables; apply and verify the documented Mythic kill-count column migration separately if needed. Image rollback does not roll back database changes.
- Create the GitHub `production` environment, restrict it to `master`, and configure required reviewers. Add environment secrets `DEPLOY_HOST` (the droplet IPv4 address), `DEPLOY_SSH_KEY`, and `DEPLOY_KNOWN_HOSTS` (verified host-key entry). Set environment variable `DEPLOY_PUBLIC_CHECKS` explicitly to `true` for normal deployments. `false` is diagnostic mode and skips external smoke tests, not ACME issuance requirements.
- Authorize the droplet's registry identity to pull the private GHCR packages. Keep the API runtime file `/etc/sites/super-recruiter/runtime.env` root-only and populate required `ConnectionStrings__PostgreSQL` and `ApiKey` values; `Gemini__ApiKey` is optional. See `deploy/runtime.env.example` for variable names only.

After the new API is live, keep the Unraid worker's `SuperRecruiterApi:BaseUrl` set to `https://recruitment.amusedtodeath.eu/api/`. Frontend requests use same-origin `/api/` automatically. The first DNS/TLS cutover may be unavailable while the certificate and healthy containers are established; no live droplet deployment has been performed by this repository change.

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
