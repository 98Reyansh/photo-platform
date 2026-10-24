# ADR 0002: Use HTTP-only cookie authentication

- Status: Accepted
- Date: 2026-10-24

## Context

The local browser application and the API are different origins: Vite uses `http://localhost:5173` or `http://127.0.0.1:5173`, and the API uses `http://localhost:5080`. The current CORS policy allows those origins to call the API, and it does not allow credentials. The API listens on HTTP so local development does not require a certificate.

The browser needs a way to retain a signed-in session. A token stored where page scripts can read it would expose that session to script access.

## Decision

Use an HTTP-only cookie for browser authentication.

Cookie, credentialed-CORS, and CSRF protections are all required before this transport is enabled. They are configured explicitly, and the cookie settings are specific to the host environment. This decision, by itself, did not change the CORS policy.

## Rationale

An HTTP-only cookie is not readable by page scripts. Because the browser attaches that cookie to requests, the API must defend against cross-site request forgery on state-changing requests. Because the local frontend and API origins differ, the browser will not send the cookie unless CORS explicitly allows credentials for a named origin. A wildcard origin cannot be combined with credentials.

Local HTTP and production HTTPS cannot share one cookie definition. A `Secure` cookie is not stored or sent on the current local HTTP API. Production must not inherit a development cookie that was relaxed for HTTP.

## Consequences

- The authentication cookie is HTTP-only.
- Credentialed CORS, when implemented, lists explicit origins only. `*` remains forbidden. The current two local Vite origins are the development set. No origin is added by this record.
- State-changing requests that rely on the cookie require CSRF protection.
- Development cookie flags are documented separately from production. Production requires `Secure`. `SameSite` is chosen for the environment and is not left at a framework default without review.
- `AllowedHosts` is not broadened.
- The health endpoints remain anonymous. No sign-in endpoint is added by this record.

P0-05 later implemented the cookie, credentialed CORS for the two existing development origins, and CSRF for studio membership. Development uses `SameSite=Lax` and `Secure=SameAsRequest`. Other environments use `Secure=Always`.
