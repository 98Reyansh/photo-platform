# Architecture

Photography Portfolio is a modular monolith. One ASP.NET Core host serves the HTTP API. Business code is split into projects so later features can grow without turning the host into the place where persistence, use cases, and domain rules accumulate.

This document records the structure introduced in P0-02. It does not define a production deployment.

## Projects

| Project | Responsibility |
| --- | --- |
| `PhotoPlatform.Domain` | Domain types and rules. This project stays independent of ASP.NET Core and EF Core. It has no entities yet. |
| `PhotoPlatform.Application` | Future use cases, DTOs, and the service interfaces those use cases need. Interfaces are added with a real use case, not as placeholders. |
| `PhotoPlatform.Infrastructure` | Future persistence and external-service implementations. No database provider is referenced yet. |
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

API endpoints return DTOs or other explicit response contracts. They do not return persistence entities. `HealthResponse` is the current API contract.

## Tenancy and photographs

The product is planned as multi-tenant. Photographs are private by default: a photo is not public unless access is granted. Tenant resolution, authorization, and storage are later tasks.

## Postponed

ASP.NET Core Identity, PostgreSQL, and storage implementations wait for later tasks. This document does not define a production deployment.
