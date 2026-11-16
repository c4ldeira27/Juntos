# Juntos

Brings a couple's (or a small group's) Google calendars together in a single, clean and modern view,
with each person in their own color. Read-only: nothing is ever changed in Google.

> "Juntos" means "together" in Portuguese. The UI is available in English and Brazilian Portuguese.

## Stack

| Layer | Technology |
|---|---|
| Front end | React 19 + Vite + TypeScript, TanStack Query, Tailwind CSS v4, FullCalendar 6, react-i18next |
| Back end | ASP.NET Core 10 (Minimal APIs), EF Core 10, Google SDK (Calendar v3, Tasks v1) |
| Database | SQL Server (Docker locally; Azure SQL in production) |
| Auth | Sign in with Google (OAuth 2.0) using the **BFF** pattern: tokens stay on the back end, the browser only gets an HttpOnly cookie |
| Deploy (M4) | A single container (API + static front end) on Azure Container Apps |

## Structure

```
src/Agenda.Api/
  Auth/            Google sign-in, session cookie, /auth/* endpoints
  GoogleApi/       refresh token → access token, Google SDK clients
  Schedule/        "what shows up and how" rules (AgendaMapper), agenda service, /api/me/* endpoints
  Groups/          groups, invites, sharing and the group agenda (/api/groups/*, /api/invites/*)
  Data/            EF Core: DbContext, entities, migrations
  Infrastructure/  error handling (ProblemDetails with a stable "code")
tests/Agenda.Api.Tests/
  *.cs             unit tests for the business rules
  Integration/     the real API against a throwaway SQL Server (Testcontainers)
web/
  src/i18n/        translations (en is the source of truth; pt-BR must match its keys)
  src/components/  screens, dialogs, calendar
docs/              guides (Google Cloud setup, Azure deploy)
Dockerfile         multi-stage build: React + .NET → one image
docker-compose.yml local SQL Server
```

## Running locally

Prerequisites: .NET SDK 10, Node 24, Docker Desktop.

1. **Google Cloud**: follow [docs/setup-google-cloud.md](docs/setup-google-cloud.md) (one time only).
2. **Database**: create `.env` from `.env.example` and start SQL Server:

   ```bash
   docker compose up -d
   ```

3. **Connection string** (same password as in `.env`):

   ```bash
   dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost,14333;Database=Agenda;User Id=sa;Password=YOUR_PASSWORD;TrustServerCertificate=True" --project src/Agenda.Api
   ```

4. **API** (applies migrations on startup in dev), on `http://localhost:5080`:

   ```bash
   dotnet run --project src/Agenda.Api
   ```

5. **Front end**, in another terminal:

   ```bash
   npm --prefix web install
   ```

   ```bash
   npm --prefix web run dev
   ```

6. Open <http://localhost:5173>.

## CI/CD

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) runs on every pull request and on pushes to `main`:

| Job | When | What |
|---|---|---|
| Back end (build + tests) | PRs and `main` | `dotnet build` + `dotnet test` (integration tests use Testcontainers) |
| Front end (lint + build) | PRs and `main` | `npm ci`, lint, type-check + Vite build |
| Docker image builds | PRs and `main` | builds the production image (no push) |
| Deploy to Azure | `main` only, after the three jobs pass | pushes `ghcr.io/c4ldeira27/juntos:<commit sha>`, updates the Container App, waits for the new revision and smoke-tests `/health` |

- `main` is protected by a ruleset: changes only through PRs, with the three checks green and the branch up to date.
- The deploy job runs in the `production` GitHub environment (only `main`, manual approval) and signs in to Azure with
  **OIDC**: a federated credential on the `id-juntos-deploy` managed identity trusts tokens from this repository's
  `production` environment, so there is no Azure secret stored in GitHub. That identity only has
  *Container Apps Contributor* on the resource group.
- The image is pushed with the run's temporary `GITHUB_TOKEN` (no personal token).

## Deploying

The first deploy was done by hand, step by step: see [docs/deploy-azure.md](docs/deploy-azure.md): Azure Container Apps + Azure SQL (free offer) + Key Vault,
with a managed identity and no passwords. Production settings (environment variables):

| Variable | Purpose |
|---|---|
| `ConnectionStrings__Default` | Azure SQL with `Authentication=Active Directory Managed Identity` |
| `Google__ClientId` / `Google__ClientSecret` | OAuth client (the secret comes from Key Vault) |
| `DataProtection__KeyVaultKeyId` | Key Vault key that wraps the Data Protection keys (optional locally) |
| `AZURE_CLIENT_ID` | Client id of the user-assigned managed identity |
| `Database__MigrateOnStartup` | `true` while there's a single replica (M4); migrations move to CI/CD in M5 |

## Useful commands

```bash
dotnet test
```

```bash
dotnet ef migrations add MigrationName --project src/Agenda.Api --output-dir Data/Migrations
```

```bash
docker build -t agenda:local .
```

## Endpoints

| Method | Route | Description |
|---|---|---|
| GET | `/auth/login?returnUrl=&consent=` | Starts Google sign-in (`consent=true` forces a new authorization) |
| POST | `/auth/logout` | Ends the session |
| GET | `/api/me` | Signed-in user + Google connection status |
| GET | `/api/me/sources` | The account's calendars and task lists |
| GET | `/api/me/agenda?from=&to=&source=` | Normalized items in the range (`source` repeated: `cal:{id}`, `tasks:{id}`) |
| GET | `/api/groups` | The user's groups, for the group switcher (empty = personal agenda only) |
| POST | `/api/groups` | Creates a group; the creator is its owner (up to 10 groups per person) |
| GET / PATCH / DELETE | `/api/groups/{id}` | Group details · rename (owner, admin) · delete (owner) |
| POST | `/api/groups/{id}/invites` | Generates an invite link (single use, 48h; owner, admin) |
| DELETE | `/api/groups/{id}/members/me` | Leaves the group (owner leaves → longest-standing admin, else member, takes over; last one leaves → group deleted) |
| DELETE | `/api/groups/{id}/members/{userId}` | Removes someone (owner: anyone else; admin: regular members) |
| PUT | `/api/groups/{id}/members/{userId}/role` | Makes someone `admin` or `member` (owner only) |
| GET/PUT | `/api/groups/{id}/sharing` | My calendars and how each one shows in **this** group (Details / Busy only / Hidden) |
| GET | `/api/groups/{id}/agenda?from=&to=` | Unified agenda, read with each member's token and filtered by each one's privacy choices |
| GET | `/api/invites/{token}` | Invite preview |
| POST | `/api/invites/{token}/accept` | Accepts the invite (idempotent) |
| GET | `/health` | Health check |

### Groups and roles

A person can be in up to 10 groups and picks which one the calendar shows (or "My calendar", their own
calendars only). What each person shares is chosen **per group**. Non-members get `404` on every group route;
members without permission get `403` with `code: "forbidden"`.

| Action | Owner | Admin | Member |
|---|---|---|---|
| Rename, invite | ✅ | ✅ | ❌ |
| Remove someone | anyone else | regular members | ❌ |
| Make / unmake admin | ✅ | ❌ | ❌ |
| Delete the group | ✅ | ❌ | ❌ |
| Leave, choose what to share | ✅ | ✅ | ✅ |

The rules live in `GroupRules` (back end, unit-tested) and are mirrored in `web/src/lib/permissions.ts`
only to decide which buttons to show.

The API returns no UI text: errors carry a stable `code` (e.g. `google_reconnect_required`)
and untitled items come with `title: null`, so the front end shows them in the user's language.

## Internationalization

- `web/src/i18n/en.ts` is the source of truth. `pt-BR.ts` is typed against it, so a missing
  or extra key fails the build, and `t('some.key')` only accepts keys that exist.
- The language follows the browser (`pt*` → `pt-BR`, anything else → `en`) and can be switched
  manually in the sidebar or on the sign-in screens (saved in `localStorage`).
- FullCalendar's locale and time format follow along (24h in pt-BR, 12h in English).
- To add a language: create `xx.ts` typed like `pt-BR.ts`, then register it in `LANGUAGES` and `resources` in `i18n/index.ts`.

## Tests

- **Unit**: display rules (`AgendaMapper`), colors, invite tokens.
- **Integration** (`tests/.../Integration`): the API runs in memory (`WebApplicationFactory`) against a
  throwaway SQL Server created by **Testcontainers**, so Docker must be running.
  Google sign-in is replaced by a test scheme (`X-Test-User` header).

## Roadmap

- [x] **M1**: Google sign-in, connection, listing and viewing your **own** calendars
- [x] **M2**: groups, invite links, per-calendar sharing (Details / Busy only)
- [x] **M3**: unified group agenda, one color per person
- [x] i18n (English + Brazilian Portuguese)
- [ ] **M4**: manual deploy to Azure with a custom domain
- [ ] **M5**: Bicep, CI/CD (GitHub Actions + OIDC), observability, privacy policy / account deletion
