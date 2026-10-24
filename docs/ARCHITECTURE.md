# Architecture

Photography Portfolio is a modular monolith. One ASP.NET Core host serves the HTTP API. Business code is split into projects so later features can grow without turning the host into the place where persistence, use cases, and domain rules accumulate.

This document records the structure introduced in P0-02. It does not define a production deployment.

## Projects

| Project | Responsibility |
| --- | --- |
| `PhotoPlatform.Domain` | Domain types and rules. This project stays independent of ASP.NET Core and EF Core. It currently defines `Tenant`, `TenantStatus`, and `Photo`. |
| `PhotoPlatform.Application` | Use cases, DTOs, and the service interfaces those use cases need. Interfaces are added with a real use case, not as placeholders. Photo commands and `PhotoResponse` live here. |
| `PhotoPlatform.Infrastructure` | Persistence implementations. It owns `ApplicationDbContext`, the Npgsql provider, and the Tenant, membership, and Photo mappings. The Photo query filter and write boundary live with the context. File or object storage is not configured. |
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

API endpoints return DTOs or other explicit response contracts. They do not return EF Core entities. `HealthResponse`, `DatabaseHealthResponse`, `CurrentMembershipResponse`, and `PhotoResponse` are the current API contracts. `Tenant`, `StudioMembership`, and `Photo` are domain and persistence models and are not returned by an endpoint.

## Tenancy and photographs

The product is multi-tenant. Photographs are private by default: a photo is not public unless access is granted. `GET /api/v1/membership/current` and the photo endpoints resolve the tenant from the signed-in user's studio membership. A client-supplied tenant id, slug, or header does not select a different tenant. Photo metadata is stored in `Photos`. The EF Core query filter returns rows only for the resolved studio and returns none when the studio is unresolved. Photo writes are rejected when that studio is unresolved or the photo's tenant does not match. Authenticated routes are `POST /api/v1/photos`, `GET /api/v1/photos/{id}`, `PUT /api/v1/photos/{id}`, and `DELETE /api/v1/photos/{id}`. The private-photo rule is recorded in `docs/SECURITY.md`. The table is recorded in `docs/DATABASE-DESIGN.md`. Development standards are in `docs/DEVELOPMENT-STANDARDS.md`. Configuration is in `docs/CONFIGURATION.md`.

## Authentication decisions

Accepted authentication decisions are separate from postponed photo and object storage. Accepting them does not authorize photograph storage, public file URLs, or an object store.

Two decisions are accepted:

- Local accounts and password hashing use ASP.NET Core Identity. See `docs/adr/0001-aspnet-core-identity.md`.
- The browser session uses an HTTP-only cookie, with explicit credentialed CORS, CSRF protection, and environment-specific cookie settings. See `docs/adr/0002-cookie-authentication.md`.

P0-05 implements those decisions for studio membership. P0-06 uses that membership to scope private photo metadata. Photograph file storage remains postponed.

## Postponed

Uploads, object storage, storage keys, original filenames, and public file URLs remain later tasks. They are not part of the accepted authentication decisions. This document does not define a production deployment.
