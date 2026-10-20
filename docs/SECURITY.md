# Security

This document records the foundations that are in place and the rules later features must follow. It does not enable authentication, photo storage, or tenant enforcement.

## Responses and logs

Unhandled exceptions return HTTP 500 with a fixed problem response: title "An unexpected error occurred." and detail "The request could not be completed." The response does not include the exception message, stack trace, or connection string.

The log entry records the HTTP method, request path, and exception type. It does not pass the exception object to the logger and does not include the exception message. Database connectivity failures log the exception type, or the PostgreSQL SQL state, and not the connection string.

`GET /api/v1/health` returns `{ "status", "timestamp" }` and does not open the database. `GET /api/v1/health/database` uses the same fields and returns HTTP 503 when the database cannot be reached. Neither response includes credentials.

## Hosts, CORS, and documentation

Allowed hosts and CORS behavior are described in `docs/CONFIGURATION.md`. Access was not broadened. Swagger UI and the OpenAPI document are available only in Development.

The local API listens on HTTP so the browser can call it without a development certificate. HTTPS redirection is not enabled. Authentication is not enabled.

## Photographs

Photographs are private by default. A photo is not public unless a later feature grants access. This repository has no photo table, upload endpoint, or public file URL. Do not add a public object URL or an unauthenticated photo response.

## Tenants

`Tenant` is a stored record. The application does not resolve a current tenant, and it does not filter queries by tenant. Multi-tenancy is not operational.

When tenant enforcement is implemented later:

- The tenant for a request comes from an authenticated membership. A client-supplied tenant id, slug, or header is not sufficient on its own.
- Every tenant-owned entity includes `TenantId` and an EF Core global query filter for the resolved tenant.
- `IgnoreQueryFilters` is limited to an explicit case with its own test.
- `Tenant.Slug` remains globally unique.

Those rules are not implemented in P0-04. No tenant middleware was added.
