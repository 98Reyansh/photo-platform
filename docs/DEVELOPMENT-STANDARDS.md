# Development standards

Photography Portfolio is a modular monolith on .NET 10, React, TypeScript, and Vite. Persistence uses PostgreSQL and EF Core. These standards describe the current repository. They do not add a production deployment.

## Projects

- `PhotoPlatform.Domain` holds domain types and rules. It does not reference ASP.NET Core or EF Core.
- `PhotoPlatform.Application` is for use cases, DTOs, and service interfaces that a real use case needs. Do not add placeholder interfaces.
- `PhotoPlatform.Infrastructure` holds EF Core, the Npgsql provider, and future external-service implementations.
- `PhotoPlatform.Api` is the HTTP host. Controllers, OpenAPI, CORS, exception handling, and composition stay here.

Allowed references:

- Application references Domain.
- Infrastructure references Application and Domain.
- Api references Application and Infrastructure.
- Domain references no other project in this solution.

Controllers stay thin. Endpoints return explicit response contracts, not EF Core entities. Timestamps stored by the application are UTC.

The frontend calls `GET /api/v1/health` only. Its base URL is `VITE_API_BASE_URL`. Styling is the CSS in `src/frontend/src/index.css`. Tailwind is not part of this repository.

## Tests

Domain and EF model tests do not open PostgreSQL. Provider-specific tests use `PHOTO_PLATFORM_TEST_CONNECTION` and are skipped when that variable is unset. Before any migration or write, those tests parse the connection string with `NpgsqlConnectionStringBuilder` and continue only when the database name is exactly `photo_platform_test`.

Run from the repository root:

```bash
dotnet build src/backend/PhotoPlatform.slnx --configuration Release
dotnet test src/backend/PhotoPlatform.slnx --configuration Release
```

Frontend, from `src/frontend`:

```bash
npm run lint
npm run build
```

## Git

Keep changes reviewable. Do not commit secrets, user-secret files, `bin/`, `obj/`, `node_modules/`, or `dist/`. Do not rewrite published history. P0-02 and P0-03 are separate from later standards work and should be reviewed as their own changes.

Further detail is in `docs/ARCHITECTURE.md`, `docs/CONFIGURATION.md`, `docs/SECURITY.md`, and `docs/DATABASE-DESIGN.md`.
