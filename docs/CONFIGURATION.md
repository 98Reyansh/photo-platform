# Configuration

No real password or production connection string belongs in source control. Placeholders such as `CHANGE_ME` and `YOUR_PASSWORD` in examples are not credentials.

## Development

The API reads the standard ASP.NET Core configuration order. For local credentials, use user secrets or an environment variable:

- User secret key: `ConnectionStrings:DefaultConnection`
- Environment variable: `ConnectionStrings__DefaultConnection`

`src/backend/PhotoPlatform.Api/connection-string.example.json` shows the shape only. The development database name used for the Tenant migration is `photoplatform`. The test database name is `photo_platform_test`. They are different databases.

`PHOTO_PLATFORM_TEST_CONNECTION` is an environment variable for the test process. Do not put it in `appsettings.json` or commit it.

## First studio bootstrap

The one-time command reads these environment variables. None of them has a default. Do not put the password in `appsettings.json`, user-secrets documentation, or a committed example.

- `PHOTO_PLATFORM_BOOTSTRAP_STUDIO_NAME`
- `PHOTO_PLATFORM_BOOTSTRAP_STUDIO_SLUG`
- `PHOTO_PLATFORM_BOOTSTRAP_OWNER_EMAIL`
- `PHOTO_PLATFORM_BOOTSTRAP_OWNER_PASSWORD`

The command also needs `ConnectionStrings__DefaultConnection` or the matching user secret. It does not apply migrations and it does not start the website. Unset the four bootstrap variables after it succeeds. Recovery starts with a read-only inspection of the one tenant, user, and membership. The repository README has that procedure. There is no bulk delete.

The frontend development URL is `VITE_API_BASE_URL` in `src/frontend/.env.development`. Override it with `.env.local`, which is gitignored.

## Production

Production settings come from environment variables, not from user secrets and not from a committed production file. This repository does not define a production host, deployment, or secret store.

Set these explicitly for a real host:

- `ConnectionStrings__DefaultConnection`
- `AllowedHosts` to that host name
- `Cors__AllowedOrigins__0` and any further origins the browser actually uses

Do not use `*` for `AllowedHosts` or for a CORS origin.

## Private photo storage

`PrivatePhotoStorage:RootPath` is optional. The environment variable is `PrivatePhotoStorage__RootPath`. When it is set, it must be an absolute directory outside this repository and outside any web root. The process does not create that directory, does not enable static files, and does not serve it. Leave the setting unset until a later upload endpoint needs the adapter. Local disk is not a store for more than one API instance. Do not put a storage root or a credential in `appsettings.json`.

## Private photo decode limits

`PrivatePhotoDecode:MaxWidth`, `PrivatePhotoDecode:MaxHeight`, and `PrivatePhotoDecode:MaxPixels` are optional. The environment variables are `PrivatePhotoDecode__MaxWidth`, `PrivatePhotoDecode__MaxHeight`, and `PrivatePhotoDecode__MaxPixels`. When unset, the limits are 12,000 pixels on each edge and 40,000,000 pixels. Those are also the hard ceilings in `PrivatePhotoContentPolicy`. A configured value below 1 or above its ceiling is replaced by the ceiling. The encoded-file ceiling remains 26,214,400 bytes and is not configurable. Decoding uses SkiaSharp 4.153.1. The API project references `SkiaSharp.NativeAssets.Linux.NoDependencies` 4.153.1 so a Linux host can load the native library without fontconfig. Render.com has not been used to verify that package.

## Allowed hosts

`appsettings.json` sets `AllowedHosts` to `localhost`, `127.0.0.1`, and `[::1]`. That matches the local launch URL `http://localhost:5080` and loopback on IPv4 and IPv6. The previous value was `*`. The narrower list does not add any host. A different host requires the `AllowedHosts` environment variable rather than a committed wildcard.

## CORS

`Cors:AllowedOrigins` is empty in `appsettings.json`. Development adds only `http://localhost:5173` and `http://127.0.0.1:5173`. A configured origin of `*` is rejected at startup. Those two origins may send any header and method, and the policy allows credentials so the browser can send the authentication cookie. No other origin is allowed. P0-05 does not add an origin.

## Swagger

OpenAPI and Swagger UI are registered only when the host environment is Development. Production does not need a documentation setting in this repository.
