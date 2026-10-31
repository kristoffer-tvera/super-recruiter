# Super Recruiter

Super Recruiter is a three-project .NET 10 solution plus a React frontend for discovering LFG players, enriching their profile data, storing records in PostgreSQL, and reviewing/managing them in a web UI.

## Architecture

- `SuperRecruiter.Api`: ASP.NET Core minimal API + Dapper + PostgreSQL
- `SuperRecruiter.Worker`: server discovery/Discord mode and local WoWProgress-only mode
- `SuperRecruiter.Shared`: DTOs/models shared by API and Worker
- `super-recruiter-web`: React 19 + TypeScript + Vite frontend

## What It Does

- Scrapes WoWProgress and Raider.IO for LFG players
- Collects feed fields and current-tier Raider.IO progression during discovery
- Fetches selected source JSON snapshots only when an officer requests it from the website
- Stores snapshots in PostgreSQL JSONB and derives the existing markdown summaries from them
- Tracks seen players and relisting timestamps
- Sends Discord messages for new/updated players
- Provides a web dashboard for status management and AI summary generation

## Security Model

- API routes under `/api/` require `X-Api-Key`; `/api/live`, `/api/health` and Scalar/OpenAPI documentation are public
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

For website-triggered source fetching, configure these on the **API**:

- `RaiderIO:ApiKey`
- `WarcraftLogs:ClientId` and `WarcraftLogs:ClientSecret`

The API never connects to WoWProgress or FlareSolverr. It queues WoWProgress
profile requests in memory for the local collector.

Use development settings, user secrets, or environment variables (for example,
`WarcraftLogs__ClientSecret`). Missing source configuration is reported individually
and does not discard other sources or existing snapshots.

### Worker (`SuperRecruiter.Worker`)

Run the same worker binary in two modes. On the droplet, use server mode:

```json
{
    "WorkerMode": "Server",
    "PollingIntervalMinutes": 30,
    "SuperRecruiterApi": {
        "BaseUrl": "http://localhost:5100/api/",
        "ApiKey": "your-api-key"
    },
    "Discord": {
        "BotToken": "...",
        "ChannelId": "..."
    },
    "RaiderIO": {
        "ApiKey": "..."
    }
}
```

On your home network, use the WoWProgress collector mode:

```json
{
    "WorkerMode": "WowProgress",
    "PollingIntervalMinutes": 30,
    "WowProgress": { "JobPollingIntervalSeconds": 10 },
    "SuperRecruiterApi": {
        "BaseUrl": "https://recruitment.amusedtodeath.eu/api/",
        "ApiKey": "your-api-key"
    },
    "FlareSolverrUrl": "http://localhost:8191/v1"
}
```

The local mode needs no Discord, Raider.IO, WarcraftLogs or database credentials.
It fetches WoWProgress listings and profiles, then uploads structured data over
authenticated HTTPS. No VPN, inbound connection, or public FlareSolverr port is
needed. Server mode owns Raider.IO discovery, filtering, persistence and Discord.
Configure the API URL with its trailing `/api/` in either mode.

### WoWProgress Queue

- The API singleton holds at most 1,000 jobs in `IMemoryCache`, deduplicated by
  player. Jobs expire after 24 hours; their three-minute leases allow another
  collector attempt after a crash. Only one job is claimed at a time per collector.
- Claims return `204` when idle. Claims, pending-status checks and listing reads
  never query PostgreSQL. Only an explicit refresh or result upload touches stored
  enrichment. Duplicate completion uploads are acknowledged without another write
  for ten minutes, unless a subsequent refresh has replaced that completed job.
- A collector keeps a completed result in process memory if upload fails and
  retries it before claiming another job. Expired/replaced leases return `409`;
  stale results are discarded. Source failures preserve the last good snapshot.
- Listings are uploaded every `PollingIntervalMinutes` and cached for two hours.
  The server merges that listing with Raider.IO using existing seen-player logic.
  Failed listing scrapes do not replace the previous cached listing.
- The website shows `pending` while the home collector is offline. It polls a
  memory-only status route while the modal is open and reloads stored details
  once the job finishes. Raider.IO and WarcraftLogs still refresh immediately.
- Queue and listing state are intentionally volatile and single-API-instance.
  API restarts lose pending jobs; request another refresh to requeue them. The
  next successful local scan restores the listing. Stored snapshots are unaffected.
  Source failures are recorded, not automatically retried; use another refresh.

## Worker Database Activity

For execution-hour quotas, idle time matters more than the size of individual
queries. The Worker fetches the external feeds first and avoids database-backed
API calls when feeds are empty. Once seen-player data has loaded, unchanged scans
also make no database-backed API calls (apart from retrying a pending seen batch).

- Seen-player data loads once per Worker process. Completed discoveries are
  marked seen and sent in one deduplicated bulk write; failed ingestion remains
  eligible for retry, and a failed bulk write stays queued.
- The lightweight player-status cache refreshes only when an unseen/relisted
  candidate needs filtering and the cache has expired. Its default lifetime is
  four polling intervals: 120 minutes with a 30-minute poll. Set optional
  `PlayerCacheRefreshMinutes` to change it. A lower value applies new blacklists
  sooner at the expense of more reads; increasing it delays status changes.
- Seen-player cleanup runs at most once per 24 hours per Worker process, after a
  successful seen batch write, so maintenance does not wake an otherwise idle DB.
- Admin filters refresh on demand once per clock hour. Failed fetches back off for
  five minutes and keep any last good configuration instead of querying for every
  candidate. Feed requests run concurrently; player ingestion remains sequential.
- Routine container probes use database-free `/api/live`. `/api/health` still
  verifies database/schema readiness for deployment checks and manual diagnosis.
  External uptime monitors should use `/api/live`, not repeatedly poll readiness.

The ten-second probe previously issued up to 8,640 readiness queries per day per
probe, potentially preventing database auto-suspension. Actual execution-hour
savings depend on the provider's suspension policy and real recruitment traffic.
Deploy both the API image and Compose changes to switch routine probes.

Discord posting still precedes API persistence; a persistence failure can produce
a duplicate Discord message on retry. Exactly-once notification delivery is not
provided by the in-memory caches and would require a separate idempotency/outbox
change. Seen caches also reload after a process restart; run one scraper instance.

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

## On-Demand Data

Open a player and choose **Fetch Data** or **Refresh Data**. Opening the modal only
reads stored source status; it never contacts upstream services. AI evaluation is
also a separate action. Existing markdown-only records remain readable until an
explicit refresh; no automatic enrichment backfill is performed.

- Discovery requests only `raid_progression:current-tier` from Raider.IO. The
  upstream selector advances tiers automatically; counts cover all returned
  current-tier raids, not the whole expansion. Counts are last-observed snapshots,
  not live revalidation of all historical players at a tier rollover.
- WarcraftLogs queries run in the API on demand. WoWProgress detail scraping runs
  in the local collector after the API queues an explicit refresh.
  WoWProgress discoveries may initially lack bio, BattleTag, languages and history.
  Unknown languages are allowed; known non-English feed languages are filtered.
- `discovery_progression` stores the compact observation. `raiderio_data`,
  `warcraftlogs_data` and `wowprogress_data` store versioned source envelopes with
  `data`, successful fetch time, last attempt time and outcome. Native JSON fields
  are preserved; WoWProgress stores parsed details, not HTML. Only the latest good
  snapshot is retained. Failed refreshes keep old data and its original timestamp.
- Raider.IO snapshots contain `profile` (gear, current/previous expansion
  progression and achievements for up to 12 returned raids) and `currentTier`.
  WarcraftLogs retains the character/zoneRankings response, not full event reports.
- Discovery cannot erase richer details, summaries, recruitment status or Discord
  metadata. The existing summaries are regenerated for successful source updates.
  If a future source schema cannot be rendered, its raw JSON is still saved and
  the previous compatible summary is retained.
  Source changes mark existing AI evaluations stale and allow regeneration.
- `GET /api/players/{id}/enrichment` reads snapshots; `?includeData=false` reads
  only metadata. `POST` to the same route fetches data and returns compact source
  outcomes plus the updated player. All require `X-Api-Key`. Lists exclude raw data.
- Partial or complete source failure returns `200` with explicit source outcomes;
  missing players return `404`, duplicate refreshes `409`, and cooldown/capacity
  limits `429` with `Retry-After`. The cooldown is 60 seconds per player, persisted
  across restarts. Two refreshes may run concurrently in the single API instance.
  Multiple API replicas would require a distributed concurrency gate.
- API source deadlines are 20 seconds for Raider.IO/WCL with a 25-second overall
  bound. The collector has a separate 65-second solver deadline and bounded
  responses. API-source rate-limit backoff is honored; no fire-and-forget tasks
  are used. WoWProgress reports rate limits as a source outcome.

Deploy the API/schema first, then both worker modes and the website. Schema
upgrades run idempotently on API startup. Keep WCL credentials on the API and
FlareSolverr configuration only on the local collector. The versioned nginx API
route allows 90 seconds. Do not run a second Discord/server worker at home.

## Verification

```bash
dotnet test SuperRecruiter.Tests/SuperRecruiter.Tests.csproj
npm run lint --prefix super-recruiter-web
npm run build --prefix super-recruiter-web
```

Database and API integration tests run when `SUPER_RECRUITER_TEST_POSTGRES` points
to an isolated disposable PostgreSQL database; otherwise they are explicitly
skipped. Never point this variable at the application or production database.

## Database Migrations

### Add Mythic Kill Count Column

If you have an existing database and need to add the `current_tier_mythic_kill_count` column, run this migration:

```sql
ALTER TABLE players
ADD COLUMN current_tier_mythic_kill_count INTEGER NOT NULL DEFAULT 0;
```

Run this directly in PostgreSQL via `psql`, pgAdmin, or any SQL client connected to your Super Recruiter database.

## Production Deployment

Production uses the GitHub Actions workflow in `.github/workflows/deploy.yml` and the shared droplet deployment helper. The Compose project/site ID is `super-recruiter`; the sole hostname is `recruitment.amusedtodeath.eu`. Host ports `18050` (web) and `18051` (API) are loopback-only. Web, API and the server worker use digest-pinned images on the droplet. Only the WoWProgress collector and FlareSolverr run at home. The worker image is built from the checked-out release, not a second Git clone.

Before the first release:

- Provision the shared droplet and deployment helper, then confirm its Docker bridge can use `172.30.50.0/24` without overlapping the host, VPC, or other projects. If it cannot, change both the Compose subnet and the API's trusted forwarded-header network together.
- Point the hostname's A/AAAA records at the droplet and allow public HTTP/HTTPS traffic. Keep DNS pointed at the droplet for HTTP-01 certificate issuance and renewal.
- Allowlist the droplet's stable public IP at the existing external PostgreSQL provider. Supply a connection string that verifies TLS; do not move the database credentials into GitHub Actions.
- Verify the existing database schema before cutover. API startup creates missing tables but does not migrate existing tables; apply and verify the documented Mythic kill-count column migration separately if needed. Image rollback does not roll back database changes.
- Create the GitHub `production` environment, restrict it to `master`, and configure required reviewers. Add environment secrets `DEPLOY_HOST` (the droplet IPv4 address), `DEPLOY_SSH_KEY`, and `DEPLOY_KNOWN_HOSTS` (verified host-key entry). Set environment variable `DEPLOY_PUBLIC_CHECKS` explicitly to `true` for normal deployments. `false` is diagnostic mode and skips external smoke tests, not ACME issuance requirements.
- Authorize the droplet's registry identity to pull the private GHCR packages. Keep the API runtime file `/etc/sites/super-recruiter/runtime.env` root-only and populate required `ConnectionStrings__PostgreSQL` and `ApiKey` values; `Gemini__ApiKey` is optional. See `deploy/runtime.env.example` for variable names only.
- Before deploying this worker split, create a root-only `/etc/sites/super-recruiter/worker.env` using `deploy/worker.env.example`: supply the API key, Raider.IO discovery key, and Discord settings. Do not put database credentials in this file. Compose sets server mode and the internal API URL; the API accepts its private `api` hostname. Ensure the deployment helper accepts the added worker service and `WORKER_IMAGE` release variable.

After the new API is live, set the Unraid worker's `WorkerMode` to `WowProgress` and its `SuperRecruiterApi:BaseUrl` to `https://recruitment.amusedtodeath.eu/api/`. Remove its Discord and Raider.IO credentials. Stop the old combined worker before starting server mode on the droplet to avoid duplicate bot sessions. Frontend requests use same-origin `/api/` automatically. No live droplet deployment has been performed by this repository change.

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
