# Security

This document records the foundations that are in place and the rules later features must follow. Studio membership authentication and private photo metadata are implemented. Photo file storage is not.

## Responses and logs

Unhandled exceptions return HTTP 500 with a fixed problem response: title "An unexpected error occurred." and detail "The request could not be completed." The response does not include the exception message, stack trace, or connection string.

The log entry records the HTTP method, request path, and exception type. It does not pass the exception object to the logger and does not include the exception message. Database connectivity failures log the exception type, or the PostgreSQL SQL state, and not the connection string.

`GET /api/v1/health` returns `{ "status", "timestamp" }` and does not open the database. `GET /api/v1/health/database` uses the same fields and returns HTTP 503 when the database cannot be reached. Neither response includes credentials.

## Hosts, CORS, and documentation

Allowed hosts and CORS behavior are described in `docs/CONFIGURATION.md`. Access was not broadened. Swagger UI and the OpenAPI document are available only in Development.

The local API listens on HTTP so the browser can call it without a development certificate. HTTPS redirection is not enabled. Studio sign-in uses an HTTP-only cookie. The development CORS policy allows credentials for the two existing Vite origins only.

## Authentication

P0-05 implements local studio membership. ASP.NET Core Identity stores the account and hashes the password (`docs/adr/0001-aspnet-core-identity.md`). The browser session uses an HTTP-only cookie (`docs/adr/0002-cookie-authentication.md`).

The authentication cookie is `PhotoPlatform.Auth`. It is HTTP-only, `SameSite=Lax`, and not persistent. In Development its `Secure` policy is `SameAsRequest`, because the local API is HTTP. Every other environment uses `Secure=Always`. Production must not reuse the development HTTP cookie. `localhost:5173` and `localhost:5080` are the same site, so `Lax` sends the cookie on credentialed fetch between them. `127.0.0.1:5173` calling `localhost:5080` is cross-site; `Lax` does not send the cookie there, and `SameSite=None` would require `Secure`, which this HTTP API cannot provide. The frontend development address remains `localhost`.

CORS allows credentials only for the explicit origins already configured: `http://localhost:5173` and `http://127.0.0.1:5173`. A wildcard origin is still rejected. No origin was added. `AllowedHosts` remains the loopback hosts.

State-changing requests require the antiforgery header `X-CSRF-TOKEN`. `GET /api/v1/auth/csrf` returns the token in JSON and stores the matching `PhotoPlatform.Csrf` cookie as HTTP-only. The frontend keeps that token in memory and does not store it, the password, or the session in `localStorage`.

`POST /api/v1/auth/sign-in` returns 204 or a generic 401, "The email or password is incorrect." Unknown accounts, wrong passwords, lockout, and accounts without a membership use that same response. `POST /api/v1/auth/sign-out` requires the session and the CSRF header. `GET /api/v1/membership/current` requires the session and returns only that user's membership. Health endpoints stay anonymous. There is no public registration endpoint and no anonymous tenant creation.

Passwords, password hashes, tokens, and connection strings stay out of logs and ordinary client responses. There is no photo upload endpoint or public file URL.

## Photographs

Photographs are private by default. A photo is not public unless a later feature grants access. Metadata is stored in `Photos` by migration `20261024122355_AddPrivatePhotoMetadata`. There is no upload, object storage, storage key, or original-file metadata. Do not add a public object URL or an unauthenticated photo response.

Authenticated routes, all under the existing session cookie:

| Method | Route | Success |
| --- | --- | --- |
| `POST` | `/api/v1/photos` | 201 and the created metadata |
| `GET` | `/api/v1/photos` | 200 and one page of metadata |
| `GET` | `/api/v1/photos/{id}` | 200 and the metadata |
| `PUT` | `/api/v1/photos/{id}` | 200 and the updated metadata |
| `DELETE` | `/api/v1/photos/{id}` | 204 |

The response fields are the photo id, tenant id, title, description, creation timestamp, and update timestamp. Title is required, trimmed, and limited to 200 characters. Description is optional, trimmed, and limited to 2,000 characters. A blank description is stored as null. Those limits are enforced by the domain type when a command saves metadata. Request records do not declare them, and they do not accept a tenant id.

The tenant is the authenticated user's studio membership. The EF Core query filter returns `Photos` rows for that studio only. It returns no rows when the studio is unresolved. An unresolved studio receives HTTP 403 before photo data is read. A missing id and a photo owned by another studio both receive HTTP 404 with the detail "The photo was not found." `POST`, `PUT`, and `DELETE` still require the `X-CSRF-TOKEN` header. `GET` remains exempt.

`GET /api/v1/photos` reads the filtered rows in `CreatedAtUtc` descending order, then `Id` descending order. `limit` defaults to 20. A supplied limit must be from 1 through 50, and an invalid or malformed limit returns HTTP 400. The response contains at most that many items. The query reads one extra row to decide whether another page exists, and it does not count the studio's photos. `continuationCursor` is present only when another page exists. An empty studio returns HTTP 200 with an empty item list and a null cursor. The cursor's `v1` value encodes the last returned creation time, in UTC ticks, and the photo id. Clients treat it as opaque. It does not grant access, and a later page still passes through the resolved-studio filter. A malformed cursor, an unsupported cursor version, or an invalid cursor value returns HTTP 400. A client-supplied tenant id does not change the page.

Photo saves go through the context write boundary. An unresolved studio, a new photo for a different tenant, a change of tenant ownership, or a delete of another studio's photo is rejected and is not rewritten to the current tenant.

## Tenants

`Tenant` is a stored record. Membership and photo endpoints resolve one tenant from the authenticated user's membership. They do not accept a client-supplied tenant id, slug, or header as the tenant. `Photo` is the tenant-owned entity with an EF Core global query filter. Production photo code does not call `IgnoreQueryFilters`. `Tenant.Slug` remains globally unique.

When later tenant-owned data is added:

- The tenant for a request comes from an authenticated membership. A client-supplied tenant id, slug, or header is not sufficient on its own.
- Every tenant-owned entity includes `TenantId` and an EF Core global query filter for the resolved tenant.
- `Tenant.Slug` remains globally unique.
