# Implementation plan

## P0-01 — Running API and frontend

Status: Complete.

- ASP.NET Core Web API on `net10.0`, with `GET /api/v1/health`.
- Development OpenAPI document and Swagger UI.
- CORS limited to the configured local frontend origins.
- React, TypeScript, and Vite frontend that calls the health endpoint.
- Solution file: `src/backend/PhotoPlatform.slnx`. SDK pin: `global.json` (`10.0.112`, `latestFeature`).

## P0-02 — Backend project structure

Status: Complete.

- Add `PhotoPlatform.Domain`, `PhotoPlatform.Application`, and `PhotoPlatform.Infrastructure` beside `PhotoPlatform.Api`.
- Apply the project references and dependency direction in `docs/ARCHITECTURE.md`.
- Leave the health endpoint, Swagger/OpenAPI setup, CORS setup, and launch instructions in place.
