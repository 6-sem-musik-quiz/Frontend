# Frontend

Part of the **6-sem-musik-quiz** system. Blazor WebAssembly (.NET 10). The whole app runs in the browser; in production nginx serves it as static files. It calls the backend services over HTTP through edgeservice (YARP) on the same host: `https://quiz.munkcloud.dk/api/<service>/...`.

## Architecture

```
Web  ──►  Services  ──►  /api/<service>/  ──►  edgeservice (YARP)  ──►  UserService, MusicService, ...
  └─────────┴──►  Domain
```

| Project | Responsibility |
|---|---|
| `src/Frontend.Web` | Razor components, pages, layout, `Program.cs`, `wwwroot/index.html`. Pages inject API client interfaces, never `HttpClient` directly. |
| `src/Frontend.Services` | Typed `HttpClient`s, one per backend area. The only layer that knows backend URLs and routes. |
| `src/Frontend.Domain` | DTOs that mirror the backend contracts. No dependencies. |
| `tests/Frontend.Services.Tests` | Unit tests for API clients with a stubbed `HttpMessageHandler` (xUnit). |
| `tests/Frontend.Web.Tests` | Component and auth tests with bUnit + NSubstitute. |

`Example*` files are placeholders showing the pattern end to end (`/examples` page → `IExampleApiClient` → UserService `GET /api/example`). Replace them with real pages and clients.

### Adding a new feature

1. Request/response DTOs in `Domain/DTOs` (same shape as the backend's)
2. `I<Name>ApiClient` in `Services/Interfaces` + implementation in `Services/ApiClients`
3. Register it in `ServiceLayerExtensions` with `AddHttpClient` and the right base URL from `ServiceUrlOptions`. Routes are relative to the service, without `api/` (e.g. `users`): the base URL is already `/api/userservice/`, and edgeservice turns `/api/userservice/users` into userservice's `/api/users`.
4. Page or component in `Web/Components/Pages`, inject the interface
5. Tests: API client in `Frontend.Services.Tests`, page in `Frontend.Web.Tests` (bUnit)

### Real-time (SignalR)

Live updates do not go through the typed `HttpClient`s but through hub clients in `Frontend.Web`,
each behind an interface so bUnit tests can use a fake:

| Interface | Hub | |
|---|---|---|
| `ILobbyHub` (`Lobbies/LobbyHub.cs`) | `/api/lobbyservice/hubs/lobby` | Lobby updates (SCRUM-100) |
| `IGameHubClient` (`Games/GameHubClient.cs`) | `/api/gameservice/hubs/game` | The game: `JoinGame`, `Ready`, `SubmitAnswer`; events `Prefetch`, `RoundStarted`, `PlayerAnswered`, `RoundEnded`, `GameEnded`, plus `ConnectionLost`/`Rejoined` (SCRUM-147) |

**"Spil igen" (SCRUM-172).** The lobby page opens the game as `spil/{gameId}?lobby={lobbyId}`. On the end screen, "Spil igen" calls LobbyService `POST /api/lobbies/{lobbyId}/play-again` and goes back to `lobby/{lobbyId}` (same code, players and settings; nobody is ready). LobbyService answers `409` until GameService's `GameFinished` has reached it, so the page tries again every 0.5 s for up to 10 s. "Forlad" goes to the front page; the player is then simply not in the reopened lobby. Without `?lobby=` the end screen only links to the front page.

Game DTOs (`Domain/DTOs/Games`) mirror GameService's hub messages. Hub errors become `GameHubException`
with a Danish message (`Games/GameMessages.cs`). Songs are downloaded and played by `IGameAudio`
(`wwwroot/js/gameAudio.js`): it fetches from the CDN into a blob URL, so the CDN must allow CORS for
the frontend's origin.

## Configuration

`Program.cs` points every API client at `<page host>/api/<service>/`. To call another host
(e.g. a local edgeservice), set `ApiBaseUrl` in `src/Frontend.Web/wwwroot/appsettings.Development.json`;
that host then needs CORS for the frontend's origin.

`Grafana:DashboardUrl` in `src/Frontend.Web/wwwroot/appsettings.json` is the dashboard the admin page links to
("Drift og metrics", SCRUM-117). The app runs in the browser and reads that file, not environment variables;
leave the value out to hide the card.

Login state (the JWT) is kept in the browser's `sessionStorage` and sent as `Authorization: Bearer`.
The backend services validate it; the frontend only reads its claims for display and routing.

## Running locally

**Against the real backend** (recommended):

```bash
docker compose up --build        # http://localhost:5006
```

This runs the production image, but with [`nginx/api-proxy.conf`](nginx/api-proxy.conf) mounted:
nginx forwards `/api/*` to `https://quiz.munkcloud.dk` (edgeservice in the cluster). The browser
only talks to localhost, so there is no CORS. **This is the live data**: whatever you create or
delete is real. After a code change, run `docker compose up --build` again.

**Against a local edgeservice** (to test a backend feature end to end):

```bash
docker compose -f docker-compose.yml -f docker-compose.local-edge.yml up --build
```

nginx then forwards `/api/*` to the edge on `http://localhost:5000` instead
([`nginx/api-local-edge.conf`](nginx/api-local-edge.conf)). Start the edge and your service first:
see "Testing a feature locally" in the EdgeService README.

**Dev server** (hot reload, no backend):

```bash
dotnet run --project src/Frontend.Web       # http://localhost:5006
```

API calls fail here, since there is no edgeservice on the same host. To use one, set `ApiBaseUrl`
in `src/Frontend.Web/wwwroot/appsettings.Development.json`; that host then needs CORS.

## Hosting

The image is `nginxinc/nginx-unprivileged` with the published `wwwroot` and [`nginx/default.conf`](nginx/default.conf):
port 8080, user 101, `GET /health`, unknown paths fall back to `index.html`, and `/api/*` answers 404
([`nginx/api-404.conf`](nginx/api-404.conf); in the cluster `/api/*` never reaches this pod).
CI pushes it to `ghcr.io/6-sem-musik-quiz/frontend` on every push to `main`, and Flux deploys it
(see `6-sem-musik-quiz/gitops`, `apps/frontend.yaml`). Cloudflare Tunnel sends
`https://quiz.munkcloud.dk/api/*` to edgeservice and everything else here.

## Tests

```bash
dotnet test
```

## Service ports

| Service | API | Postgres |
|---|---|---|
| UserService | 5001 | 5441 |
| MusicService | 5002 | 5442 |
| StatisticService | 5003 | 5443 |
| LobbyService | 5004 | 5444 |
| GameService | 5005 | 5445 |
| **Frontend** | **5006** | – |
