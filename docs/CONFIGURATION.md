# Configuration

No real password or production connection string belongs in source control. Placeholders such as `CHANGE_ME` and `YOUR_PASSWORD` in examples are not credentials.

## Development

The API reads the standard ASP.NET Core configuration order. For local credentials, use user secrets or an environment variable:

- User secret key: `ConnectionStrings:DefaultConnection`
- Environment variable: `ConnectionStrings__DefaultConnection`

`src/backend/PhotoPlatform.Api/connection-string.example.json` shows the shape only. The development database name used for the Tenant migration is `photoplatform`. The test database name is `photo_platform_test`. They are different databases.

`PHOTO_PLATFORM_TEST_CONNECTION` is an environment variable for the test process. Do not put it in `appsettings.json` or commit it.

The frontend development URL is `VITE_API_BASE_URL` in `src/frontend/.env.development`. Override it with `.env.local`, which is gitignored.

## Production

Production settings come from environment variables, not from user secrets and not from a committed production file. This repository does not define a production host, deployment, or secret store.

Set these explicitly for a real host:

- `ConnectionStrings__DefaultConnection`
- `AllowedHosts` to that host name
- `Cors__AllowedOrigins__0` and any further origins the browser actually uses

Do not use `*` for `AllowedHosts` or for a CORS origin.

## Allowed hosts

`appsettings.json` sets `AllowedHosts` to `localhost`, `127.0.0.1`, and `[::1]`. That matches the local launch URL `http://localhost:5080` and loopback on IPv4 and IPv6. The previous value was `*`. The narrower list does not add any host. A different host requires the `AllowedHosts` environment variable rather than a committed wildcard.

## CORS

`Cors:AllowedOrigins` is empty in `appsettings.json`. Development adds only `http://localhost:5173` and `http://127.0.0.1:5173`. A configured origin of `*` is rejected at startup. Those two origins may send any header and method, and the policy allows credentials so the browser can send the authentication cookie. No other origin is allowed. P0-05 does not add an origin.

## Swagger

OpenAPI and Swagger UI are registered only when the host environment is Development. Production does not need a documentation setting in this repository.
