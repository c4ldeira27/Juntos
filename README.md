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
docs/              guides (Google Cloud setup)
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
| GET | `/api/groups/current` | The user's group (204 if they're not in one) |
| POST | `/api/groups` | Creates a group (MVP rule: 1 group per person) |
| POST | `/api/groups/{id}/invites` | Generates an invite link (single use, 48h) |
| DELETE | `/api/groups/{id}/members/me` | Leaves the group (owner leaves → oldest member takes over; last one leaves → group deleted) |
| GET/PUT | `/api/groups/{id}/sharing` | My calendars and how each one shows in the group (Details / Busy only / Hidden) |
| GET | `/api/groups/{id}/agenda?from=&to=` | Unified agenda, read with each member's token and filtered by each one's privacy choices |
| GET | `/api/invites/{token}` | Invite preview |
| POST | `/api/invites/{token}/accept` | Accepts the invite (idempotent) |
| GET | `/health` | Health check |

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
