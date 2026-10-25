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
src/backend/PhotoPlatform.Domain                Domain types, including Tenant and Photo
src/backend/PhotoPlatform.Application           Use cases and DTOs, including photo commands
src/backend/PhotoPlatform.Infrastructure        EF Core persistence. Object storage is not configured.
src/backend/PhotoPlatform.Api                   ASP.NET Core Web API host
src/frontend                                    Vite + React + TypeScript app
docs/ARCHITECTURE.md                            Project boundaries and reference rules
docs/DATABASE-DESIGN.md                         Tenant, membership, and photo schema, plus the test database
docs/DEVELOPMENT-STANDARDS.md                   Repository and testing standards
docs/CONFIGURATION.md                           Development and production configuration
docs/SECURITY.md                                Error, logging, host, and tenancy rules
```

`PhotoPlatform.Api` stays the HTTP host. Project references and responsibilities are recorded in `docs/ARCHITECTURE.md`. Configuration and security rules are in `docs/CONFIGURATION.md` and `docs/SECURITY.md`. Studio sign-in is local account membership. Authenticated photo metadata endpoints are described below. Upload and download endpoints are not implemented. A private local storage adapter exists and is not served over HTTP.

## Local URLs

| App | URL |
| --- | --- |
| API | http://localhost:5080 |
| Health check | http://localhost:5080/api/v1/health |
| Database health | http://localhost:5080/api/v1/health/database |
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

Leave this process running. The launch profile sets `ASPNETCORE_ENVIRONMENT=Development` and `http://localhost:5080`. The process health endpoint does not connect to PostgreSQL. The database health endpoint does, and it reports unhealthy when the database or connection string is unavailable.

## Create the first studio

The website has no registration page. A one-time command creates the first studio, its owner, and the membership that sign-in requires. It does not start the API. If any studio or any account already exists, it stops before it writes and creates nothing.

Apply the existing migrations to the target database first. This command does not apply them. The connection string is the same `ConnectionStrings__DefaultConnection` value the API already uses. Supply it through the environment or user secrets. Do not put it in a committed file.

Set these variables in the shell that runs the command. Replace every placeholder. Do not commit the values.

```bash
export PHOTO_PLATFORM_BOOTSTRAP_STUDIO_NAME="Studio name"
export PHOTO_PLATFORM_BOOTSTRAP_STUDIO_SLUG="studio-name"
export PHOTO_PLATFORM_BOOTSTRAP_OWNER_EMAIL="owner@example.com"
export PHOTO_PLATFORM_BOOTSTRAP_OWNER_PASSWORD="replace-with-a-secret"
dotnet run --project src/backend/PhotoPlatform.Api -- bootstrap
```

The slug uses lowercase letters, digits, and single hyphens. The password must satisfy the existing account policy: at least 12 characters, with an uppercase letter, a lowercase letter, a digit, and a symbol. The command does not choose a password.

On success it prints that bootstrap completed, plus the studio id, slug, and owner email, and exits 0. Remove the four variables from that shell immediately:

```bash
unset PHOTO_PLATFORM_BOOTSTRAP_STUDIO_NAME
unset PHOTO_PLATFORM_BOOTSTRAP_STUDIO_SLUG
unset PHOTO_PLATFORM_BOOTSTRAP_OWNER_EMAIL
unset PHOTO_PLATFORM_BOOTSTRAP_OWNER_PASSWORD
```

A later run refuses to create anything while those variables remain set, and it also refuses when a studio or account already exists. Sign in with the owner email in the existing form.

If the process prints `Bootstrap completed` and then reports a shutdown failure, the studio was saved. Sign in with the owner email and remove the four variables. The non-zero exit belongs to process shutdown.

If the process exits non-zero and did not print `Bootstrap completed`, the transaction was rolled back. Correct the configuration or database problem and run the command again. Do not delete rows.

If the process is killed after the database commits, the next run sees the existing studio or account and refuses. Sign in, then remove the variables.

Remove a bootstrap only after a read-only inspection, and only when every check below is true. If any check fails, stop and inspect manually. Do not run a delete without the ids from that inspection.

```sql
SELECT "Id", "Name", "Slug" FROM "Tenants";
SELECT "Id", "Email" FROM "AspNetUsers";
SELECT "Id", "UserId", "TenantId" FROM "StudioMemberships";
SELECT COUNT(*) FROM "Photos";
SELECT COUNT(*) FROM "AspNetUserClaims";
SELECT COUNT(*) FROM "AspNetUserLogins";
SELECT COUNT(*) FROM "AspNetUserTokens";
```

Continue only when `Tenants` has exactly one row, `AspNetUsers` has exactly one row, and `StudioMemberships` has exactly one row; that membership's user id and tenant id are those two rows; `Photos` is zero; and the slug and email are the bootstrap you recognize. Claim, login, and token rows must belong only to that same user id. Any other row means the state is ambiguous: stop.

`StudioMemberships` and `Photos` reference `Tenants` with `ON DELETE RESTRICT`. `StudioMemberships` has no foreign key to `AspNetUsers`. Claim, login, and token rows reference `AspNetUsers` with `ON DELETE CASCADE`. Delete in this order, each statement limited to the id from the inspection. Stop if a statement affects any number of rows other than one.

```sql
DELETE FROM "StudioMemberships" WHERE "Id" = '<membership-id>';
DELETE FROM "AspNetUsers" WHERE "Id" = '<user-id>';
DELETE FROM "Tenants" WHERE "Id" = '<tenant-id>';
```

Deleting that user also removes claim, login, and token rows for that same user id. Do not delete from any other table, and do not omit the `WHERE` clause.

Running this once against a hosted database is a separate operational choice. Supply the secret connection string in that environment and run this same command. This repository does not assume a particular host provides a one-off job.

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

## Photo metadata

Authenticated clients can manage private photo metadata. The tenant comes from the signed-in user's studio membership. These routes do not accept a file:

| Method | Route |
| --- | --- |
| `POST` | `/api/v1/photos` |
| `GET` | `/api/v1/photos` |
| `GET` | `/api/v1/photos/{id}` |
| `PUT` | `/api/v1/photos/{id}` |
| `DELETE` | `/api/v1/photos/{id}` |

`GET /api/v1/photos` returns one page of the signed-in studio's metadata. `limit` defaults to 20 and accepts 1 through 50. `cursor` continues from the previous page. The cursor is an opaque position, and it does not select a tenant. Results are newest first. Stored fields are the photo id, tenant id, title, description, creation time, and update time. Title and description rules, CSRF, and the not-found behavior are in `docs/SECURITY.md`. The table and migration are in `docs/DATABASE-DESIGN.md`.

After a successful sign-in, the Lensora studio home shows the studio name from that membership and lists the studio's photo metadata. It offers Load more when another page exists. A photo count is shown only when the returned page is complete. Collections and studio settings are marked coming soon and are not links. The list sends the session cookie and does not send a tenant id or a CSRF header on this GET. It does not show image files. Reloading the page asks `GET /api/v1/membership/current` with that cookie and restores the studio home when the session is still present. Sign out requests a fresh CSRF token and then calls `POST /api/v1/auth/sign-out`. The password, CSRF token, and session are not written to web storage. API health stays on the page and does not mean the visitor is signed in.

## PostgreSQL

PostgreSQL is not required for the process health check. It is required to apply the Tenant migration and to receive a healthy database check.

Fedora 44 packages are `postgresql-server` and `postgresql` (PostgreSQL 18.6). From a shell that can use `sudo`:

```bash
sudo dnf install postgresql-server postgresql
sudo postgresql-setup --initdb
sudo systemctl enable --now postgresql
sudo -u postgres createuser --pwprompt photoplatform
sudo -u postgres createdb --owner=photoplatform photoplatform
```

`createuser --pwprompt` asks for a password. Choose one locally and do not commit it. Fedora's default `pg_hba.conf` often uses `ident` for `127.0.0.1`. For password login from the API, set a local rule in `/var/lib/pgsql/data/pg_hba.conf` and restart PostgreSQL:

```text
host photoplatform photoplatform 127.0.0.1/32 scram-sha-256
host photoplatform photoplatform ::1/128 scram-sha-256
```

Docker is an alternative. The Fedora packages are `moby-engine` and `docker-cli`. Replace `YOUR_PASSWORD` before running the container:

```bash
sudo dnf install moby-engine docker-cli
sudo systemctl enable --now docker
sudo docker run --name photoplatform-postgres \
  -e POSTGRES_USER=photoplatform \
  -e POSTGRES_PASSWORD=YOUR_PASSWORD \
  -e POSTGRES_DB=photoplatform \
  -p 5432:5432 \
  -d postgres:18
```

Store the connection string in user secrets or an environment variable. `src/backend/PhotoPlatform.Api/connection-string.example.json` shows the shape with `Password=CHANGE_ME`. That value is a placeholder.

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=photoplatform;Username=photoplatform;Password=YOUR_PASSWORD" --project src/backend/PhotoPlatform.Api
```

Or, for one shell:

```bash
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=photoplatform;Username=photoplatform;Password=YOUR_PASSWORD"
```

The EF Core CLI is the local tool `dotnet-ef` 10.0.13, declared in `.config/dotnet-tools.json`. Restore it from the repository root, then create or apply migrations:

```bash
dotnet tool restore
dotnet tool run dotnet-ef -- migrations add MigrationName \
  --project src/backend/PhotoPlatform.Infrastructure/PhotoPlatform.Infrastructure.csproj \
  --startup-project src/backend/PhotoPlatform.Api/PhotoPlatform.Api.csproj \
  --output-dir Persistence/Migrations
dotnet tool run dotnet-ef -- database update \
  --project src/backend/PhotoPlatform.Infrastructure/PhotoPlatform.Infrastructure.csproj \
  --startup-project src/backend/PhotoPlatform.Api/PhotoPlatform.Api.csproj
```

`Microsoft.EntityFrameworkCore.Design` is a development-time package on the API project. Its assets are private and are not published with the app.

After `database update`, inspect the schema:

```bash
psql "host=localhost port=5432 dbname=photoplatform user=photoplatform" -c '\d "Tenants"'
```

The initial migration is `20261020135016_InitialTenants`. It creates the `Tenants` table, the primary key, the status check, and the unique `IX_Tenants_Slug` index. That migration is already applied to the local development database `photoplatform`. `20261024122355_AddPrivatePhotoMetadata` creates the `Photos` table. Guarded tests apply pending migrations only when the database name is exactly `photo_platform_test`. Do not point `database update` at another database to recreate it.

`GET /api/v1/health/database` returns HTTP 200 when PostgreSQL accepts a connection and HTTP 503 otherwise. The body uses `healthy` or `unhealthy` and does not include the connection string.

Provider-specific tests run only when `PHOTO_PLATFORM_TEST_CONNECTION` is set. The database name in that connection string must be exactly `photo_platform_test`. The tests skip when the variable is unset, and they reject any other database before migrating or writing. See `docs/DATABASE-DESIGN.md`.

`AllowedHosts` allows `localhost`, `127.0.0.1`, and `[::1]`. CORS allows only the two local Vite origins listed above. Details are in `docs/CONFIGURATION.md`.

## Troubleshooting

- **`dotnet` is not found or is not .NET 10.** Install the .NET 10 SDK (`dotnet-sdk-10.0` on Fedora). `global.json` will not fall forward to .NET 8 or 9.
- **The frontend says the API address is not configured.** `VITE_API_BASE_URL` is missing. Check `src/frontend/.env.development` or `.env.local`, then restart Vite.
- **The frontend says the API could not be reached.** Start the API with the command above and confirm `curl` works. Confirm `VITE_API_BASE_URL` is `http://localhost:5080` with no `/api` suffix.
- **The browser console shows a CORS error.** The page origin must match `Cors:AllowedOrigins` in `appsettings.Development.json`. Vite's default origin is `http://localhost:5173`. A different port, or `127.0.0.1` versus `localhost`, is a different origin.
- **Port 5080 or 5173 is already in use.** Stop the other process or change the API `applicationUrl` in `Properties/launchSettings.json`, `VITE_API_BASE_URL`, and the matching CORS origin together.
- **Swagger UI does not load.** The API must be running in Development. Use http://localhost:5080/swagger.
- **Database health is unhealthy.** PostgreSQL is stopped, or `ConnectionStrings:DefaultConnection` is missing. Set the user secret or `ConnectionStrings__DefaultConnection`, restart the API, and confirm `psql` can connect. The response does not include the password.
- **Environment changes seem ignored.** Restart `npm run dev` after editing Vite env files. Restart `dotnet run` after editing `appsettings*.json`.
