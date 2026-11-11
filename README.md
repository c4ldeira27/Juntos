# Juntos (nome provisório)

Une as agendas do Google de um casal (ou de um grupo pequeno) em uma visualização única,
limpa e moderna, com cada pessoa em uma cor. Somente leitura: nada é alterado no Google.

## Stack

| Camada | Tecnologia |
|---|---|
| Front | React 19 + Vite + TypeScript, TanStack Query, Tailwind CSS v4, FullCalendar 6 |
| Back | ASP.NET Core 10 (Minimal APIs), EF Core 10, SDK do Google (Calendar v3, Tasks v1) |
| Banco | SQL Server (local via Docker; Azure SQL em produção) |
| Auth | Login com Google (OAuth 2.0) no padrão **BFF**: tokens ficam no backend, o navegador só recebe um cookie HttpOnly |
| Deploy (M4) | Um container (API + front estático) no Azure Container Apps |

## Estrutura

```
src/Agenda.Api/
  Auth/            login com Google, cookie de sessão, endpoints /auth/*
  GoogleApi/       refresh token → access token, clientes do SDK do Google
  Schedule/        regras de "o que aparece e como" (AgendaMapper) e endpoints /api/me/*
  Data/            EF Core: DbContext, entidades, migrations
  Infrastructure/  tratamento de erros (ProblemDetails com "code")
tests/Agenda.Api.Tests/   testes das regras de negócio
web/                      React (em dev o Vite faz proxy de /api, /auth, /signin-google para a API)
docs/                     guias (setup do Google Cloud)
Dockerfile                build multi-stage: React + .NET → uma imagem
docker-compose.yml        SQL Server local
```

## Como rodar (dev)

Pré-requisitos: .NET SDK 10, Node 24, Docker Desktop.

1. **Google Cloud**: siga [docs/setup-google-cloud.md](docs/setup-google-cloud.md) (uma vez só).
2. **Banco**: crie o `.env` a partir do `.env.example` e suba o SQL Server:

   ```bash
   docker compose up -d
   ```

3. **Connection string** (mesma senha do `.env`):

   ```bash
   dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost,14333;Database=Agenda;User Id=sa;Password=SUA_SENHA;TrustServerCertificate=True" --project src/Agenda.Api
   ```

4. **API** (aplica as migrations sozinha em dev), em `http://localhost:5080`:

   ```bash
   dotnet run --project src/Agenda.Api
   ```

5. **Front**, em outro terminal:

   ```bash
   npm --prefix web install
   ```

   ```bash
   npm --prefix web run dev
   ```

6. Abra <http://localhost:5173>.

## Comandos úteis

```bash
dotnet test
```

```bash
dotnet ef migrations add NomeDaMigration --project src/Agenda.Api --output-dir Data/Migrations
```

```bash
docker build -t agenda:local .
```

## Endpoints

| Método | Rota | Descrição |
|---|---|---|
| GET | `/auth/login?returnUrl=&consent=` | Inicia o login no Google (`consent=true` força nova autorização) |
| POST | `/auth/logout` | Encerra a sessão |
| GET | `/api/me` | Usuário logado + status da conexão com o Google |
| GET | `/api/me/sources` | Agendas e listas de tarefas da conta |
| GET | `/api/me/agenda?from=&to=&source=` | Itens normalizados no intervalo (`source` repetido: `cal:{id}`, `tasks:{id}`) |
| GET | `/api/groups/current` | Grupo do usuário (204 se não estiver em nenhum) |
| POST | `/api/groups` | Cria um grupo (regra do MVP: 1 grupo por pessoa) |
| POST | `/api/groups/{id}/invites` | Gera link de convite (uso único, 48h) |
| DELETE | `/api/groups/{id}/members/me` | Sai do grupo (dono sai → o membro mais antigo assume; último sai → grupo apagado) |
| GET/PUT | `/api/groups/{id}/sharing` | Minhas agendas e como cada uma aparece no grupo (Detalhes / Só ocupado / Oculta) |
| GET | `/api/groups/{id}/agenda?from=&to=` | Agenda unificada, lida com o token de cada membro e filtrada pela privacidade de cada um |
| GET | `/api/invites/{token}` | Prévia do convite |
| POST | `/api/invites/{token}/accept` | Aceita o convite (idempotente) |
| GET | `/health` | Health check |

## Testes

- **Unidade**: regras de exibição (`AgendaMapper`), cores, tokens de convite.
- **Integração** (`tests/.../Integration`): a API roda em memória (`WebApplicationFactory`) contra um
  SQL Server descartável criado pelo **Testcontainers**, então o Docker precisa estar aberto.
  O login do Google é trocado por um esquema de teste (header `X-Test-User`).

## Roadmap

- [x] **M1**: login com Google, conexão, listagem e visualização das **próprias** agendas
- [x] **M2**: grupos, convite por link, compartilhamento por agenda (Detalhes / Só ocupado)
- [x] **M3**: agenda unificada do grupo, com cor por pessoa
- [ ] **M4**: deploy manual no Azure com domínio
- [ ] **M5**: Bicep, CI/CD (GitHub Actions + OIDC), observabilidade, privacidade/exclusão de conta
