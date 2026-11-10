# GameNest Backend

REST API for **GameNest** — a web application for browsing a video game catalog and managing personal game libraries.

- Frontend (Cloudflare Pages): `https://gamenest.pp.ua`
- API (Render, planned): `https://api.gamenest.pp.ua`

## Features

- User registration with mandatory email confirmation, login, resending the confirmation
- Password hashing with BCrypt, JWT authentication (HS256, 1 hour)
- Role-based authorization (`User` and `Admin`)
- Game catalog, game titles and versions
- Personal library (status, purchased flag) and user-created games
- Game submissions with admin moderation (approve, reject, edit pending)
- User profile and privacy settings
- Authentication rate limiting
- Swagger UI (Development only), health check endpoint

## Tech Stack

- C# / ASP.NET Core Web API on **.NET 10**
- **PostgreSQL** via Entity Framework Core 10 + Npgsql
- JWT Bearer authentication, BCrypt.Net-Next
- Swagger / OpenAPI (Swashbuckle)
- xUnit integration tests (in-memory SQLite)
- Docker (multi-stage build) for deployment

## Project Structure

```text
GameNest_BackEnd/
├── Controllers/          # API endpoints (Auth, Games, GameTitles, Library, Profile, Submissions, UserGames)
├── Data/                 # GameNestDbContext
├── DTOs/                 # Request/response models and validation
├── Migrations/           # EF Core migrations (do not edit or delete)
├── Models/               # Entities
├── Services/             # Email senders, rate limiting, CORS
├── Properties/           # launchSettings.json (local profiles)
├── scripts/              # One-time operational SQL scripts
├── Tests/                # xUnit tests (not part of the API build)
├── appsettings.json      # Non-secret settings only
├── appsettings.Development.json
├── Dockerfile            # Production image
├── .dockerignore
├── .env.example          # Environment variable names with placeholders
└── Program.cs            # Configuration, middleware pipeline, health endpoint
```

## Local Development

### Requirements

- .NET SDK 10
- PostgreSQL (local install or container)
- `dotnet-ef` tool: `dotnet tool install --global dotnet-ef`

### PostgreSQL

Create an empty database, for example:

```powershell
psql -U postgres -c "CREATE DATABASE gamenest;"
```

### Secrets (User Secrets)

Run from the repository root (PowerShell). Every developer generates their own JWT key; keys are never shared.

```powershell
# PostgreSQL connection string (replace the placeholders)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=gamenest;Username=<user>;Password=<password>"

# Generate and store a random 64-byte JWT key
$bytes = New-Object byte[] 64
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
dotnet user-secrets set "Jwt:Key" ([Convert]::ToBase64String($bytes))
Remove-Variable bytes
```

### Run

```powershell
dotnet ef database update
dotnet run --launch-profile http
```

The API listens on `http://localhost:5096`; Swagger UI is at `http://localhost:5096/swagger`.
CORS in Development allows `http://localhost:3000` (from `appsettings.Development.json`); to use another frontend port,
set `CORS_ALLOWED_ORIGINS`, e.g. `$env:CORS_ALLOWED_ORIGINS="http://localhost:5173"`.

### Tests

The tests use an isolated in-memory SQLite database and never connect to PostgreSQL:

```powershell
dotnet test .\Tests\GameNest_BackEnd.Tests.csproj
```

## Configuration and Environment Variables

`appsettings.json` contains only non-secret settings. Secrets are never committed. Environment variables override
configuration files; `__` replaces `:` in names. The application **refuses to start** if a required setting is missing
or invalid (it never falls back to a built-in key or connection string).

| Variable | Required | Secret | Description |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | — | no | `Production` on Render (also the Docker image default). `Development` enables Swagger and the console email sender |
| `ConnectionStrings__DefaultConnection` | yes | **yes** | PostgreSQL connection string (Npgsql keyword format) |
| `Jwt__Key` | yes | **yes** | JWT signing key, at least 32 bytes; 64 random bytes recommended |
| `Jwt__Issuer` | yes* | no | Defaults to `GameNestAPI` from `appsettings.json` |
| `Jwt__Audience` | yes* | no | Defaults to `GameNestClients` from `appsettings.json` |
| `CORS_ALLOWED_ORIGINS` | yes outside Development | no | Comma-separated allowed origins, e.g. `https://gamenest.pp.ua` |
| `Email__ConfirmationUrlBase` | no | no | Frontend confirmation page; `email` and `token` query parameters are appended |
| `PORT` | no | no | Port to listen on (all interfaces). Set automatically by Render |
| `RateLimiting__Auth__PermitLimit` / `__WindowSeconds` | no | no | Default `10` / `60`: register, login, confirm-email |
| `RateLimiting__ResendConfirmation__PermitLimit` / `__WindowSeconds` | no | no | Default `5` / `900` |

\* Required, but already provided by `appsettings.json`.

User Secrets are loaded only in `Development`. Production never reads them or `appsettings.Development.json`
(that file contains no secrets and is excluded from the Docker image).
`.env.example` lists the variables with placeholders; copy it to `.env` (ignored by Git) for `docker run --env-file`.
The application itself does not read `.env` files.

### Generating a production JWT key

```powershell
$bytes = New-Object byte[] 64
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
[Convert]::ToBase64String($bytes) | Set-Clipboard   # paste into the Render Dashboard, do not save it to a file
Remove-Variable bytes
```

Use a new key for production; never reuse a development key or a key that was ever committed.

## Docker

The `Dockerfile` uses a multi-stage build: `mcr.microsoft.com/dotnet/sdk:10.0` restores, builds and publishes;
the final image is `mcr.microsoft.com/dotnet/aspnet:10.0` (runtime only, no SDK) and runs as the non-root `app` user.
`Tests/`, local settings, `.env` files and build output are excluded by `.dockerignore`.

Port handling:

- If `PORT` is set (Render), the API listens on `0.0.0.0:$PORT`.
- Otherwise the image listens on `8080` (`ASPNETCORE_HTTP_PORTS=8080`, all interfaces).
- `ASPNETCORE_URLS`, if set explicitly, takes precedence over `PORT`.

Build and run locally (`host.docker.internal` reaches PostgreSQL on the host machine):

```powershell
docker build -t gamenest-api .
docker run --rm -p 8080:8080 --env-file .env gamenest-api
```

Then open `http://localhost:8080/api/health`. Swagger is not available in the container because it runs in `Production`.

## Deployment on Render

### Service parameters

| Parameter | Value |
|---|---|
| Service Type | Web Service |
| Runtime | Docker |
| Repository | `GameNestProject/GameNest_BackEnd` |
| Branch | `main` |
| Root Directory | *(empty)* |
| Dockerfile Path | `./Dockerfile` |
| Instance Type | Free |
| Health Check Path | `/api/health` |

### Environment variables to add in the Render Dashboard

| Name | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__DefaultConnection` | Neon connection string (see below) — **secret** |
| `Jwt__Key` | Newly generated 64-byte key — **secret** |
| `Jwt__Issuer` | `GameNestAPI` (optional: same as the default) |
| `Jwt__Audience` | `GameNestClients` (optional: same as the default) |
| `CORS_ALLOWED_ORIGINS` | `https://gamenest.pp.ua` |
| `Email__ConfirmationUrlBase` | `https://gamenest.pp.ua/confirm-email` (optional, effective only once an email provider exists; check the frontend route) |

Do **not** set `PORT` — Render provides it.

### Custom domain `api.gamenest.pp.ua`

1. Render → service → Settings → Custom Domains → add `api.gamenest.pp.ua`.
2. Cloudflare DNS → add a `CNAME` record `api` → `<service-name>.onrender.com`.
   Keep it **DNS only** (grey cloud) until Render has verified the domain and issued the certificate.
3. If you enable the Cloudflare proxy afterwards, set SSL/TLS mode to **Full (strict)**.
4. TLS is terminated by Render/Cloudflare; the application itself serves plain HTTP inside the container.

## Neon PostgreSQL

Neon shows a URI like `postgresql://<user>:<password>@<endpoint>.<region>.aws.neon.tech/<db>?sslmode=require`.
Npgsql does **not** accept this URI format; convert it to the keyword format:

```text
Host=<endpoint>.<region>.aws.neon.tech;Port=5432;Database=<db>;Username=<user>;Password=<password>;SSL Mode=VerifyFull
```

- `SSL Mode=VerifyFull` encrypts the connection and validates Neon's certificate (use `Require` only if validation fails).
- Use the **direct** endpoint (without `-pooler` in the host name) for migrations. The application can use either.
- Store the connection string only in the Render Dashboard and, temporarily, in your shell session.

## Applying EF Core Migrations

Migrations are **not** applied automatically at startup: a migration should run once, deliberately, before the new
version starts, and never concurrently from several instances. Apply them manually when a release contains new migrations.

Current migrations (oldest first): `InitialCreate`, `AddUsers`, `FinalizeMvpModels`, `AddP1ApiFeatures`,
`AuditDataIntegrity`. A new, empty Neon database gets all of them.

### Option A — `dotnet ef database update` from your computer

Run from the repository root in PowerShell. The command builds the app in the `Development` environment, so the
JWT settings come from your User Secrets; only the connection string is overridden for this shell session:

```powershell
# Make sure the environment is not Production in this shell
Remove-Item Env:ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue

# Paste the Neon connection string at the prompt (not stored in history or in files)
$env:ConnectionStrings__DefaultConnection = Read-Host "Neon connection string"

dotnet ef migrations list          # shows which migrations are applied / pending
dotnet ef database update          # applies the pending migrations

# Remove the connection string from the session
Remove-Item Env:ConnectionStrings__DefaultConnection
```

### Option B — review an SQL script first

```powershell
dotnet ef migrations script --idempotent -o migrate.sql
```

Review `migrate.sql`, then run it in the Neon SQL Editor or with `psql`. The idempotent script skips migrations that
are already recorded in `__EFMigrationsHistory`. Do not commit `migrate.sql`.

Back up (or create a Neon branch of) the production database before applying migrations to a database with data.

## CORS

- Allowed origins come from `CORS_ALLOWED_ORIGINS` (comma-separated, exact origins: scheme + host + optional port,
  no paths, no wildcards). Invalid values stop the application at startup.
- Production value: `https://gamenest.pp.ua`. Add `https://www.gamenest.pp.ua` or a Cloudflare Pages preview origin
  only if the frontend is actually served from there.
- Allowed methods: `GET`, `POST`, `PUT`, `DELETE`; allowed headers: `Authorization`, `Content-Type`;
  exposed headers: `Retry-After`, `Location`. Credentials (cookies) are not used: the JWT is sent in the `Authorization` header.
- The CORS middleware runs before rate limiting and authentication, so preflight `OPTIONS` requests are answered
  without a token and do not consume rate-limit permits, and `401`/`429` responses still carry CORS headers.

## Health Check

`GET /api/health` → `200 {"status":"ok","service":"GameNest Backend"}`.
It requires no authentication, returns no internal data and does not touch the database, so it reports that the
process is up, not that PostgreSQL is reachable.

## Email Confirmation

New accounts must confirm their email address before they can log in. Confirmation tokens are random,
stored only as SHA-256 hashes, valid for 24 hours and single-use.

**No email provider (SMTP or email API) is integrated yet.** The code has an `IEmailSender` abstraction with two implementations:

- **Development** — `DevelopmentEmailSender` writes the confirmation link to the console log
  (`[Development email] Confirmation link for ...`); confirm in the frontend or via `POST /api/Auth/confirm-email`.
- **Every other environment** — `UnavailableEmailSender` always fails, so registration returns
  `503 email_service_unavailable` and the account is **not** created. It never pretends an email was sent.

`POST /api/Auth/resend-confirmation` always returns `202` with the same body, so it does not reveal whether an email is registered.

To enable registration in production, a real `IEmailSender` implementation (for example an HTTP email API provider)
must be added and registered in `Services/EmailServiceCollectionExtensions.cs`, together with its configuration
(API key as a secret environment variable, sender address, `Email__ConfirmationUrlBase`).

## First Administrator

There is no API to assign roles. The first administrator is created once, directly in the database:

1. Register the future administrator through the API and confirm the email address.
2. Edit `scripts/bootstrap-first-admin.sql` locally and replace the placeholder email (do not commit that change).
3. Run it against the target database:

   ```powershell
   psql "<connection string>" -v ON_ERROR_STOP=1 -f scripts/bootstrap-first-admin.sql
   ```

4. The user must log in again to receive a token with the `Admin` role.

The script runs in a transaction and aborts if an administrator already exists, the placeholder was not replaced,
the email does not match exactly one user, or that user's email is not confirmed.

## Security

- Passwords are stored as BCrypt hashes (8+ characters, at most 72 UTF-8 bytes, common passwords rejected).
- Login responses and timing are the same for unknown emails and wrong passwords.
- Tokens are HS256-signed, valid for one hour, and must contain a valid user id; detailed token errors are shown only in Development.
- Authentication endpoints are rate limited per client IP and endpoint (`429` with `Retry-After`).
- Swagger UI and OpenAPI JSON are available only in Development.
- HTTPS redirection is not enabled in the application; TLS is terminated by Render / Cloudflare.
- Secrets must never be committed. Any secret that has ever been committed must be considered compromised and rotated.

## Known Production Limitations

1. **Registration does not work in production** until an email provider is integrated (`503 email_service_unavailable`).
   Consequently no user can log in on a fresh database, the first administrator cannot be created, and the catalog
   (which has no seed data) stays empty.
2. **Rate limiting behind Render's proxy**: the limiter uses the direct connection IP. Forwarded headers are not trusted,
   so on Render all clients may share the proxy's address and therefore one limit (10 auth requests per minute in total
   by default). Trusting `X-Forwarded-For` safely requires knowing the proxy chain; this is not configured yet.
3. The rate limiter is in-memory and per instance; limits reset when the service restarts.
4. **Render Free** spins the service down after inactivity; the first request afterwards can take up to about a minute.
   **Neon Free** suspends the compute when idle, which adds latency to the first database query.
5. The health check does not verify database connectivity.
6. Migrations must be applied manually (see above).

## License

See [LICENSE](LICENSE).
