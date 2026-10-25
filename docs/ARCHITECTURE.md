# Architecture

Photography Portfolio is a modular monolith. One ASP.NET Core host serves the HTTP API. Business code is split into projects so later features can grow without turning the host into the place where persistence, use cases, and domain rules accumulate.

This document records the structure introduced in P0-02. It does not define a production deployment.

## Projects

| Project | Responsibility |
| --- | --- |
| `PhotoPlatform.Domain` | Domain types and rules. This project stays independent of ASP.NET Core and EF Core. It currently defines `Tenant`, `TenantStatus`, and `Photo`. |
| `PhotoPlatform.Application` | Use cases, DTOs, and the service interfaces those use cases need. Interfaces are added with a real use case, not as placeholders. Photo commands and `PhotoResponse` live here. |
| `PhotoPlatform.Infrastructure` | Persistence implementations. It owns `ApplicationDbContext`, the Npgsql provider, and the Tenant, membership, and Photo mappings. The Photo query filter and write boundary live with the context. `LocalPrivatePhotoStore` writes an original under a configured absolute directory. That directory is not served, and the adapter is not a multi-instance store. |
| `PhotoPlatform.Api` | HTTP host. Controllers, OpenAPI, CORS, exception handling, and dependency-injection composition stay here. |

`PhotoPlatform.Api` still owns the health endpoint, Swagger/OpenAPI in Development, and the configured CORS policy. `PhotoPlatform.Api.Infrastructure` is the namespace for that host middleware. It is not the `PhotoPlatform.Infrastructure` project.

## Project references

Allowed references:

- `PhotoPlatform.Application` references `PhotoPlatform.Domain`.
- `PhotoPlatform.Infrastructure` references `PhotoPlatform.Application` and `PhotoPlatform.Domain`.
- `PhotoPlatform.Api` references `PhotoPlatform.Application` and `PhotoPlatform.Infrastructure`.
- `PhotoPlatform.Domain` references no other project in this solution.

Dependency direction, from the host inward:

```text
PhotoPlatform.Api
  -> PhotoPlatform.Application -> PhotoPlatform.Domain
  -> PhotoPlatform.Infrastructure
       -> PhotoPlatform.Application
       -> PhotoPlatform.Domain
```

`PhotoPlatform.Api` does not reference `PhotoPlatform.Domain` directly. `PhotoPlatform.Application` does not reference `PhotoPlatform.Infrastructure` or `PhotoPlatform.Api`. `PhotoPlatform.Infrastructure` does not reference `PhotoPlatform.Api`. `PhotoPlatform.Domain` does not reference ASP.NET Core or EF Core.

## HTTP boundaries

Controllers stay thin. They translate HTTP into an application operation and translate the result back to HTTP. The health endpoint is the current exception: it reports process liveness and has no application use case.

API endpoints return DTOs or other explicit response contracts. They do not return EF Core entities. `HealthResponse`, `DatabaseHealthResponse`, `CurrentMembershipResponse`, `PhotoResponse`, and `PhotoListResponse` are the current API contracts. `Tenant`, `StudioMembership`, and `Photo` are domain and persistence models and are not returned by an endpoint.

## Tenancy and photographs

The product is multi-tenant. Photographs are private by default: a photo is not public unless access is granted. `GET /api/v1/membership/current` and the photo endpoints resolve the tenant from the signed-in user's studio membership. A client-supplied tenant id, slug, or header does not select a different tenant. Photo metadata is stored in `Photos`. The EF Core query filter returns rows only for the resolved studio and returns none when the studio is unresolved. Photo writes are rejected when that studio is unresolved or the photo's tenant does not match. Authenticated routes are `POST /api/v1/photos`, `GET /api/v1/photos`, `GET /api/v1/photos/{id}`, `PUT /api/v1/photos/{id}`, and `DELETE /api/v1/photos/{id}`. The collection route returns one page, newest first, with `Id` descending when creation times match. `limit` defaults to 20 and accepts 1 through 50. The continuation cursor is an opaque position for that order. It is not an authorization token, and the query filter still selects the resolved studio. The private-photo rule is recorded in `docs/SECURITY.md`. The table is recorded in `docs/DATABASE-DESIGN.md`. Development standards are in `docs/DEVELOPMENT-STANDARDS.md`. Configuration is in `docs/CONFIGURATION.md`.

## Authentication decisions

Accepted authentication decisions are separate from postponed photo and object storage. Accepting them does not authorize photograph storage, public file URLs, or an object store.

Two decisions are accepted:

- Local accounts and password hashing use ASP.NET Core Identity. See `docs/adr/0001-aspnet-core-identity.md`.
- The browser session uses an HTTP-only cookie, with explicit credentialed CORS, CSRF protection, and environment-specific cookie settings. See `docs/adr/0002-cookie-authentication.md`.

P0-05 implements those decisions for studio membership. P0-06 uses that membership to scope private photo metadata. P0-07 lists that metadata one page at a time. P0-08 shows that metadata in the browser after sign-in. A private local storage adapter can keep an original for a server-derived tenant id and photo id. There is no upload or download route and no cloud store. Signature recognition does not mean the file was decoded. `IPrivatePhotoDecoder` is the application contract. `SkiaPrivatePhotoDecoder` is the Infrastructure adapter and is the only type that references SkiaSharp. It accepts JPEG, PNG, and WebP after a real decode, with the encoded-size and dimension ceilings in `PrivatePhotoContentPolicy`. `ImageDecodeVerified` is true only for that successful decode.

## First studio bootstrap

`IStudioBootstrap` is the application contract. `StudioBootstrap` in Infrastructure creates the tenant with the domain rules, the owner with `UserManager<ApplicationUser>.CreateAsync`, and the `StudioMembership`. The API runs that command only when the process arguments are exactly `bootstrap`. The command does not start the website, does not add a route, and does not apply migrations. Tenant creation, the Identity save, and the membership save share one read-committed transaction on `ApplicationDbContext`. On PostgreSQL that transaction takes `pg_advisory_xact_lock` on its own connection before the final existence check and holds the lock until commit or rollback. SQLite tests do not take that lock.

## Postponed

HTTP upload and download, Cloudflare R2, signed URLs, storage keys in API responses, original filenames, public file URLs, schema changes for file metadata, and production deployment remain later tasks. Static files are not enabled. The bounded decoder is in place for a later upload endpoint: 25 MiB encoded, 40 megapixels, and a long edge of 12,000 pixels. This document does not define a production deployment. The Linux native Skia asset has not been run on Render.com.
