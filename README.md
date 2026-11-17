# Frontend

Part of the **6-sem-musik-quiz** system. ASP.NET Core (.NET 10) Blazor Web App with interactive server rendering. Serves the UI for players and admins and calls the backend services over HTTP.

## Architecture

```
Web  ──►  Services  ──►  backend APIs (UserService, MusicService, ...)
  └─────────┴──►  Domain
```

| Project | Responsibility |
|---|---|
| `src/Frontend.Web` | Razor components, pages, layout, `Program.cs`. Pages inject API client interfaces, never `HttpClient` directly. |
| `src/Frontend.Services` | Typed `HttpClient`s, one per backend area. The only layer that knows backend URLs and routes. |
| `src/Frontend.Domain` | DTOs that mirror the backend contracts. No dependencies. |
| `tests/Frontend.Services.Tests` | Unit tests for API clients with a stubbed `HttpMessageHandler` (xUnit). |
| `tests/Frontend.Web.Tests` | Component tests with bUnit + NSubstitute, and integration tests using `WebApplicationFactory`. |

`Example*` files are placeholders showing the pattern end to end (`/examples` page → `IExampleApiClient` → UserService `GET /api/example`). Replace them with real pages and clients.

### Adding a new feature

1. Request/response DTOs in `Domain/DTOs` (same shape as the backend's)
2. `I<Name>ApiClient` in `Services/Interfaces` + implementation in `Services/ApiClients`
3. Register it in `ServiceLayerExtensions` with `AddHttpClient` and the right base URL from `ServiceUrlOptions`
4. Page or component in `Web/Components/Pages`, inject the interface
5. Tests: API client in `Frontend.Services.Tests`, page in `Frontend.Web.Tests` (bUnit)

## Configuration

Backend URLs live in the `Services` section of `appsettings.json` and default to the local ports below. Override with environment variables, e.g. `Services__UserService=http://userservice:8080`.

## Running locally

Start the backend services you need from their own repos first (`docker compose up --build` in each).

**Frontend from your IDE:**

```bash
dotnet run --project src/Frontend.Web
```

**Frontend in Docker** (reaches the backends on the host's ports):

```bash
docker compose up --build
```

- Frontend: http://localhost:5006
- Health: http://localhost:5006/health

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
