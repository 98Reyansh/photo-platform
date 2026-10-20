# Photography Portfolio

Minimal local full-stack application for the Photography Portfolio platform. The API exposes a health check, and the React app shows whether that check succeeds.

## Required tools

| Tool | Version used here |
| --- | --- |
| Git | 2.56+ |
| .NET SDK | 10.0 (`net10.0`). This workspace uses SDK 10.0.112, pinned in `global.json` with `latestFeature` roll-forward inside the 10.0 band. |
| Node.js | 22+ (verified with 22.23.1) |
| npm | 10+ (verified with 10.9.8) |

Do not substitute another .NET major version. If `dotnet --version` does not report a 10.0 SDK, install `dotnet-sdk-10.0` before continuing.

## Repository layout

```text
src/backend/PhotoPlatform.slnx                 Solution for the backend projects
src/backend/Directory.Build.props               Shared project defaults for backend projects
src/backend/PhotoPlatform.Domain                Domain types and rules
src/backend/PhotoPlatform.Application           Future use cases and DTOs
src/backend/PhotoPlatform.Infrastructure        Future persistence and external services
src/backend/PhotoPlatform.Api                   ASP.NET Core Web API host
src/frontend                                    Vite + React + TypeScript app
docs/ARCHITECTURE.md                            Project boundaries and reference rules
```

`PhotoPlatform.Api` stays the HTTP host. Project references and responsibilities are recorded in `docs/ARCHITECTURE.md`. This task does not add a database, authentication, or file uploads.

## Local URLs

| App | URL |
| --- | --- |
| API | http://localhost:5080 |
| Health check | http://localhost:5080/api/v1/health |
| OpenAPI document | http://localhost:5080/openapi/v1.json |
| Swagger UI | http://localhost:5080/swagger |
| Frontend | http://localhost:5173 |

The API listens on HTTP so the browser can call it without a development certificate. Swagger UI and the OpenAPI document are available when `ASPNETCORE_ENVIRONMENT` is `Development`.

## Configure the API base URL

The frontend reads `VITE_API_BASE_URL`. The committed development value is `http://localhost:5080` in `src/frontend/.env.development`. `src/frontend/.env.example` documents the same variable.

To override it locally, copy the example and edit the copy:

```bash
cp src/frontend/.env.example src/frontend/.env.local
```

Restart `npm run dev` after changing any Vite environment file. `npm run dev` reads `.env.development`. `npm run build` and `npm run preview` use production mode, which does not read `.env.development`. Set `VITE_API_BASE_URL` in the shell or in `.env.production` before building for a deployed environment. `.env`, `.env.local`, and other `*.local` files are gitignored. Do not put credentials in these files.

The API allows browser calls only from origins listed in `Cors:AllowedOrigins`. Development configuration allows `http://localhost:5173` and `http://127.0.0.1:5173`. Add another origin there if the frontend runs on a different address. Do not use `*`.

## Start the API

From the repository root:

```bash
dotnet run --project src/backend/PhotoPlatform.Api
```

Leave this process running. The launch profile sets `ASPNETCORE_ENVIRONMENT=Development` and `http://localhost:5080`.

## Start the frontend

In a second terminal, from the repository root:

```bash
cd src/frontend
npm install
npm run dev
```

Open http://localhost:5173. The page should show **The API is running.**

## Test the health endpoint

With the API running:

```bash
curl -i http://localhost:5080/api/v1/health
```

A successful response is HTTP 200 and JSON like:

```json
{
  "status": "healthy",
  "timestamp": "2026-10-20T12:00:00Z"
}
```

`timestamp` is UTC. `status` is `healthy` when the process is serving requests.

Stop the API and use **Retry** on the frontend. The page should show an error and should not report a successful connection. Start the API again and press **Retry**; the status should return to running.

## Troubleshooting

- **`dotnet` is not found or is not .NET 10.** Install the .NET 10 SDK (`dotnet-sdk-10.0` on Fedora). `global.json` will not fall forward to .NET 8 or 9.
- **The frontend says the API address is not configured.** `VITE_API_BASE_URL` is missing. Check `src/frontend/.env.development` or `.env.local`, then restart Vite.
- **The frontend says the API could not be reached.** Start the API with the command above and confirm `curl` works. Confirm `VITE_API_BASE_URL` is `http://localhost:5080` with no `/api` suffix.
- **The browser console shows a CORS error.** The page origin must match `Cors:AllowedOrigins` in `appsettings.Development.json`. Vite's default origin is `http://localhost:5173`. A different port, or `127.0.0.1` versus `localhost`, is a different origin.
- **Port 5080 or 5173 is already in use.** Stop the other process or change the API `applicationUrl` in `Properties/launchSettings.json`, `VITE_API_BASE_URL`, and the matching CORS origin together.
- **Swagger UI does not load.** The API must be running in Development. Use http://localhost:5080/swagger.
- **Environment changes seem ignored.** Restart `npm run dev` after editing Vite env files. Restart `dotnet run` after editing `appsettings*.json`.
