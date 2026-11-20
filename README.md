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

## Configuration

`Program.cs` points every API client at `<page host>/api/<service>/`. To call another host
(e.g. a local edgeservice), set `ApiBaseUrl` in `src/Frontend.Web/wwwroot/appsettings.Development.json`;
that host then needs CORS for the frontend's origin.

Login state (the JWT) is kept in the browser's `sessionStorage` and sent as `Authorization: Bearer`.
The backend services validate it; the frontend only reads its claims for display and routing.

## Running locally

```bash
dotnet run --project src/Frontend.Web       # dev server on http://localhost:5006
docker compose up --build                   # the production image: nginx on http://localhost:5006
```

Locally there is no edgeservice on the same host, so API calls fail (nginx answers `/api/*` with 404).
Pages and routing work.

## Hosting

The image is `nginxinc/nginx-unprivileged` with the published `wwwroot` and [`nginx.conf`](nginx.conf):
port 8080, user 101, `GET /health`, unknown paths fall back to `index.html`.
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
