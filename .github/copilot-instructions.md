# Project Guidelines

## Architecture

Three-project .NET 10 solution with a React frontend:

- **SuperRecruiter.Api** — ASP.NET Core minimal API. PostgreSQL via Dapper (raw SQL). Singleton `PlayerDatabaseService` for all DB access. Endpoints defined in `EndpointMapper.cs` using static extension methods grouped by domain. API docs via Scalar at `/scalar/v1`.
- **SuperRecruiter.Worker** — .NET Generic Host background service. Scrapes player data from WoWProgress and Raider.IO on a polling interval, enriches with WarcraftLogs data, posts results to the API, and sends Discord notifications with interactive buttons. Uses `PlayerCacheService` (in-memory, refreshed periodically) to minimize read calls to the API.
- **SuperRecruiter.Shared** — Shared DTOs, models, enums, and helpers referenced by both Api and Worker. No business logic.
- **super-recruiter-web** — React 19 + TypeScript + Vite SPA. Bootstrap 5 (dark mode via `data-bs-theme="dark"`). Proxies `/api` to the .NET API in dev. No router library — uses manual `history.pushState` navigation.

## Code Style

### C# (.NET 10)

- Primary constructors for DI injection (no boilerplate fields)
- Minimal API pattern — no controllers, use `MapGet`/`MapPost`/etc. in `EndpointMapper.cs`
- Raw SQL with Dapper — no Entity Framework or ORMs
- `async`/`await` throughout, return `Task` from async methods
- Nullable reference types enabled (`<Nullable>enable</Nullable>`)

### TypeScript / React

- Functional components only, no class components
- `type` imports with `import { type Foo }` syntax (`verbatimModuleSyntax` enabled)
- Strict mode — no unused locals/parameters
- Bootstrap utility classes for layout and styling — minimal custom CSS
- Shared constants in `src/constants.ts`, types in `src/types.ts`, API calls in `src/api.ts`
- Components in `src/components/`, pages in `src/pages/`

## Build and Test

```bash
# API
cd SuperRecruiter.Api && dotnet run

# Worker
cd SuperRecruiter.Worker && dotnet run

# Frontend
cd super-recruiter-web && npm install && npm run dev

# Lint frontend
cd super-recruiter-web && npm run lint

# Build frontend for production
cd super-recruiter-web && npm run build
```

## Conventions

- The API is the single source of truth for data. The Worker writes through the API — it never accesses the database directly.
- The Worker uses `SuperRecruiterApiClient` (typed `HttpClient`) for all API communication.
- Player status is managed via the `PlayerStatus` enum (New, Interested, Contacted, Declined, Blacklisted) — there is no separate blacklist table.
- Discord bot runs as a hosted service inside the Worker process, not as a separate service.
- Database schema is created on API startup via `InitializeDatabaseAsync()` — no migrations framework.
- Frontend proxies API requests via Vite dev server config (`/api` → `http://localhost:5100`).
- Secrets (connection strings, API keys, bot tokens) go in `appsettings.Development.json` or user secrets — never committed.
