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

## P0-03 — PostgreSQL and the Tenant model

Status: Complete.

- Add EF Core and the Npgsql provider to `PhotoPlatform.Infrastructure`.
- Add the `Tenant` domain entity and its EF Core mapping, including a unique index on the normalized slug.
- Create the `InitialTenants` migration. Applying it requires a local PostgreSQL database.
- Read `ConnectionStrings:DefaultConnection` from configuration. Local credentials belong in user secrets or environment variables.
- Add `GET /api/v1/health/database`, separate from the process health endpoint.
- Do not add authentication, Identity, photo uploads, comments, likes, subscriptions, or tenant authorization.

Identity and storage remain later tasks. Storing a Tenant row does not make multi-tenancy operational.

## P0-04 — Standards and safety foundations

Status: Implemented and verified.

- Document development standards, configuration, and security rules.
- Keep client error responses generic and keep exception messages out of logs.
- Restrict PostgreSQL integration tests to the database name `photo_platform_test` before migration or writes.
- Limit `AllowedHosts` to local loopback hosts. Leave the explicit CORS origins unchanged.
- Do not add authentication, tenant middleware, photo endpoints, storage, packages, or migrations.

## P0-05 — Authenticated studio membership

Status: Implemented in code. A previously reported backend run passed 49 tests, with 0 failed and 0 skipped: 20 domain tests and 29 infrastructure tests. That report says the guarded PostgreSQL tests ran and exercised `20261024110924_AddStudioMembership` against `photo_platform_test`. The development database `photoplatform` was not migrated during the final read-only review. `PHOTO_PLATFORM_TEST_CONNECTION` was unset in that review environment, so those tests were not repeated then and are not rerun by this documentation cleanup.

- ASP.NET Core Identity stores local accounts and password hashes (`docs/adr/0001-aspnet-core-identity.md`).
- The browser session is the HTTP-only cookie `PhotoPlatform.Auth` (`docs/adr/0002-cookie-authentication.md`).
- Development CORS allows credentials for the two existing Vite origins only. No origin was added.
- State-changing requests require the `X-CSRF-TOKEN` header. The token is returned by `GET /api/v1/auth/csrf` and kept in frontend memory.
- `POST /api/v1/auth/sign-in`, `POST /api/v1/auth/sign-out`, and `GET /api/v1/membership/current` are the membership API. Tenant selection comes from the authenticated membership.
- Photo metadata, uploads, object storage, and public file URLs are not part of this milestone.

The migration `20261024110924_AddStudioMembership` is generated. The previously reported guarded tests exercised it against `photo_platform_test`. It was not applied to `photoplatform` during the final read-only review, and this change does not run `database update`.
