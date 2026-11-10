# GameNest Backend

REST API for **GameNest** — a web application for browsing a video game catalog and managing personal game libraries.

- Frontend (Cloudflare Pages): `https://gamenest.pp.ua`
- API (Render, planned): `https://api.gamenest.pp.ua`

## Features

- **Sign-up and sign-in with Google** (Google Identity Services ID token → GameNest JWT) — the public flow for the MVP
- Email/password registration with mandatory email confirmation and login (kept, switched off by default in production)
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
- Resend REST API for transactional email (plain `HttpClient`, no SDK)
- Google.Apis.Auth for verifying Google ID tokens
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
├── Cli/                  # Operator command: admin promote
├── Services/             # Email senders (Resend, development), rate limiting, CORS
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
| `Email__ConfirmationUrlBase` | with Resend | no | Frontend confirmation page; `email` and `token` query parameters are appended |
| `Resend__ApiKey` | for email delivery | **yes** | Resend API key (sending access). All three `Resend__*` settings or none |
| `Resend__FromEmail` | for email delivery | no | Sender address on the domain verified in Resend, e.g. `no-reply@gamenest.pp.ua` |
| `Resend__FromName` | no | no | Sender display name, default `GameNest` |
| `Resend__TimeoutSeconds` | no | no | Timeout of a Resend request, default `15` |
| `Google__ClientId` | yes outside Development | no | OAuth 2.0 **Web** client ID (`….apps.googleusercontent.com`). No client secret is needed |
| `Auth__PasswordRegistrationEnabled` | no | no | `false` by default (`true` in `appsettings.Development.json`): register + resend-confirmation |
| `Auth__PasswordLoginEnabled` | no | no | `true` by default during the transition to Google (see "Email/password flow and the transition to Google") |
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
| `Google__ClientId` | `<id>.apps.googleusercontent.com` from Google Cloud Console (required) |

Optional — only if email/password registration is switched back on (`Auth__PasswordRegistrationEnabled=true`):

| Name | Value |
|---|---|
| `Email__ConfirmationUrlBase` | `https://gamenest.pp.ua/confirm-email` (required with Resend) |
| `Resend__ApiKey` | Resend API key, sending access — **secret** |
| `Resend__FromEmail` | `no-reply@gamenest.pp.ua` (on the verified domain) |
| `Resend__FromName` | `GameNest` |

Google-only sign-up does not need Resend. Without the `Resend__*` variables the API starts normally; only the
(disabled by default) email/password registration would return `503`.
`ConnectionStrings__DefaultConnection` is needed by the service itself; the admin command is run locally and needs nothing extra in Render.

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
`AuditDataIntegrity`, `AddExternalLogins`. A new, empty Neon database gets all of them.

`AddExternalLogins` (Google sign-in) is additive: it creates the `ExternalLogins` table, adds the nullable
`Users.AvatarUrl` column and makes `Users.PasswordHash` nullable. No data is removed. **Apply it before deploying
the version with Google sign-in**, otherwise that version fails on its first database query. Rolling it back
(`dotnet ef database update AuditDataIntegrity`) deletes all Google links and fails while Google-only users
(without a password) exist.

It does not create or change the unique indexes on `Users.Email` / `Users.Username` (they exist since
`FinalizeMvpModels`), so it cannot fail because of duplicates in existing data. Those indexes are case-sensitive,
however, and emails were not normalized before the email-confirmation release. Before deploying, run these
read-only checks against the target database (e.g. in the Neon SQL Editor); all of them should return no rows:

```sql
-- Emails that differ only in case or surrounding spaces (Google sign-in answers 409 duplicate_accounts_for_email)
SELECT lower(trim("Email")) AS email, count(*) FROM "Users" GROUP BY 1 HAVING count(*) > 1;
-- Usernames that differ only in case (allowed by the index, but confusing)
SELECT lower("Username") AS username, count(*) FROM "Users" GROUP BY 1 HAVING count(*) > 1;
-- Emails that are not normalized (still found by Google sign-in, but not by password login or the admin command)
SELECT "Id", "Email" FROM "Users" WHERE "Email" <> lower(trim("Email"));
-- Which migrations are applied
SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1;
```

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

## Google Authentication

Google confirms the user's identity; GameNest then issues **its own JWT** (the same token as password login).
The Google ID token is never used as a GameNest token, never stored and never logged.

### Endpoint

`POST /api/Auth/google` — anonymous, rate limited like the other authentication endpoints (`429` with `Retry-After`).

```http
POST /api/Auth/google
Content-Type: application/json

{ "idToken": "<Google ID token: the `credential` returned by Google Identity Services>" }
```

`200 OK`:

```json
{
  "token": "<GameNest JWT, HS256, valid for 1 hour>",
  "isNewUser": true,
  "user": { "id": 42, "username": "Olena_Hravets", "email": "olena@gmail.com", "role": "User", "avatarUrl": "https://lh3.googleusercontent.com/..." }
}
```

Use the token like any other: `Authorization: Bearer <token>`. It contains the same claims as password login
(user id, username, role).

Errors (body `{"code": "..."}`, never stack traces):

| Status | Code | Meaning |
|---|---|---|
| `400` | (validation problem) | `idToken` missing, empty or longer than 8192 characters |
| `401` | `invalid_google_token` | Bad signature, wrong issuer, audience is not our client ID, expired, no `sub` or `email`, malformed |
| `401` | `google_email_not_verified` | `email_verified` is not `true` |
| `409` | `email_registered_unconfirmed` | An email/password account with this email exists but was never confirmed — not linked automatically |
| `409` | `account_linked_to_another_google_account` | The account with this email is already linked to a different Google account |
| `409` | `account_link_required` | An account with this email exists, but Google is not authoritative for the address (not Gmail, no matching Workspace `hd`): sign in with the password and link explicitly (`POST /api/Auth/google/link`) |
| `409` | `duplicate_accounts_for_email` | Several legacy accounts differ only in the case of this email; an administrator must merge/clean them up |
| `429` | `too_many_requests` | Rate limit |
| `503` | `google_auth_unavailable` | Google's signing certificates could not be downloaded; retry later |
| `503` | `google_auth_not_configured` | `Google__ClientId` is not set (possible only in Development) |
| `503` | `google_sign_in_busy` | Concurrent requests kept conflicting; retry |

### How it works

1. The token is verified with the official `Google.Apis.Auth` library (`GoogleJsonWebSignature.ValidateAsync`):
   RS256 signature against Google's published keys, issuer `accounts.google.com` / `https://accounts.google.com`,
   audience = `Google__ClientId`, expiry (30 s clock tolerance). GameNest then re-checks issuer, audience and expiry
   and requires a `sub`, a valid `email` and `email_verified == true`.
   Only these verified claims are used; any other fields in the request body are ignored.
2. The user is looked up by the Google account id (`sub`) in `ExternalLogins` — not by email, because a Google
   email can change. Found → sign in (role unchanged).
3. Not found, but a user with the same email exists (compared case-insensitively, so accounts created before email
   normalization are found too; several case variants → `409 duplicate_accounts_for_email`):
   - already linked to another Google account → `409 account_linked_to_another_google_account` (a linked `sub` is never replaced);
   - email not confirmed → `409 email_registered_unconfirmed` (prevents taking over an account someone registered
     with an address they do not own);
   - Google is **not authoritative** for the address → `409 account_link_required` (see the table below);
   - otherwise the Google identity is **linked** to that account. Role, password, email and `EmailConfirmed` are not changed.
4. Otherwise a new user is created: role `User`, **no password** (`PasswordHash = null`, so password login is impossible
   for it), username from the Google name (letters/digits, unique suffix on collisions; the email is never used as a
   public username), avatar from the Google picture (https only). `EmailConfirmed` depends on the email kind (below).

| Email kind | How it is detected | Auto-link to an existing confirmed account | `EmailConfirmed` for a new user |
|---|---|---|---|
| Gmail | address ends with `@gmail.com` or `@googlemail.com` | yes | `true` — Google operates the mailbox |
| Google Workspace | `email_verified` and the `hd` claim is present **and equals the email's domain** | yes | `true` — the domain's administrator controls the account |
| Other (Google account with an external address, or `hd` on a different domain) | everything else | **no** → `account_link_required` | `false` — Google checked the address once, but is not authoritative for its current owner |

A user of the "Other" kind can still sign up and sign in with Google (the account is found by `sub`); only the
email is not treated as confirmed. Consequences: such an account cannot be promoted with the admin command (it
requires a confirmed email) and its email does not prove ownership for any future email-based feature.

### Explicit linking: `POST /api/Auth/google/link`

Recovery path for `account_link_required` and for any existing password account (the emails do not need to match):

```http
POST /api/Auth/google/link
Authorization: Bearer <GameNest JWT from POST /api/Auth/login>
Content-Type: application/json

{ "idToken": "<Google ID token>", "password": "<current account password>" }
```

The JWT proves the session, the password re-confirms the account owner (a stolen JWT alone cannot attach an
attacker's Google account), and the ID token proves control of the Google account. Responses:
`200 google_account_linked` / `200 google_account_already_linked`, `401 invalid_credentials` (wrong password),
`401 invalid_google_token` / `google_email_not_verified`, `409 account_linked_to_another_google_account` (this user
already has a different Google account), `409 google_account_linked_to_another_user`, `409 account_has_no_password`
(Google-only account — nothing to link). Rate limited like the other authentication endpoints. Role, password,
email and `EmailConfirmed` are not changed.
5. Unique indexes (`ExternalLogins(Provider, ProviderSubject)`, `ExternalLogins(UserId, Provider)`, `Users(Email)`,
   `Users(Username)`) make concurrent requests safe; a conflicting insert is retried from the start (up to 3 times).

Roles never come from Google or from the request: new users are `User`, administrators are promoted only with the
admin command (see "First Administrator").

### Google Cloud Console setup (manual)

1. <https://console.cloud.google.com/> → create or select a project.
2. **Google Auth Platform** (formerly *OAuth consent screen*): app name `GameNest`, support email, audience
   **External**; add the authorized domain `gamenest.pp.ua`, developer contact, privacy policy / terms URLs when
   available. Scopes: only `openid`, `email`, `profile` (non-sensitive, no verification review needed).
   Publish the app (status *In production*) — in *Testing* only listed test users can sign in.
3. **Clients** (*Credentials*) → **Create client** → type **Web application**:
   - **Authorized JavaScript origins:** `https://gamenest.pp.ua` and, for local frontend development,
     `http://localhost:3000` (also `http://localhost` — Google requires it for localhost with Google Identity Services).
   - Redirect URIs are not needed for the ID-token (popup / One Tap) flow.
4. Copy the **Client ID** (`….apps.googleusercontent.com`) into Render as `Google__ClientId` and into the frontend
   configuration. The **client secret is not used** — do not put it anywhere.

### Email/password flow and the transition to Google

The email/password endpoints, email confirmation and Resend are kept. New public accounts are created only through
Google; password **login** stays on by default so that existing accounts never lose access:

| Setting | Default (production) | Endpoints | When disabled |
|---|---|---|---|
| `Auth__PasswordRegistrationEnabled` | `false` | `POST /api/Auth/register`, `POST /api/Auth/resend-confirmation` | `403 {"code":"password_registration_disabled"}` |
| `Auth__PasswordLoginEnabled` | `true` (transition) | `POST /api/Auth/login` | `403 {"code":"password_login_disabled"}` |

`POST /api/Auth/confirm-email` stays available (it only completes links that were already sent).
In Development both flags are `true` (`appsettings.Development.json`).

What happens to existing accounts:

| Existing account | Password login on | Password login off |
|---|---|---|
| Confirmed, Gmail / matching Workspace | password, or Google (auto-linked) | Google (auto-linked) |
| Confirmed, other address (e.g. `@gamenest.pp.ua` without Workspace, Outlook) | password; links Google via `/google/link` | **only if Google was linked before** — otherwise locked out |
| Not confirmed | cannot log in (unchanged) | cannot log in |
| Admin | as above for its email kind | as above — make sure every admin has Google linked first |

Transition plan:

1. Deploy with the defaults (registration off, login on) and apply the `AddExternalLogins` migration.
2. Ask every existing user — administrators first — to sign in and link Google (automatic for Gmail/Workspace,
   `POST /api/Auth/google/link` otherwise; the frontend needs a "Link Google account" action in the profile).
3. Check who is still unlinked (read-only):

   ```sql
   SELECT u."Id", u."Username", u."Email", u."Role"
   FROM "Users" u
   WHERE u."PasswordHash" IS NOT NULL AND u."EmailConfirmed"
     AND NOT EXISTS (SELECT 1 FROM "ExternalLogins" l WHERE l."UserId" = u."Id" AND l."Provider" = 'Google');
   ```

4. When nobody (or nobody who matters) is left, set `Auth__PasswordLoginEnabled=false` in Render. If someone is
   locked out later, set it back to `true` temporarily (configuration only, no code change), let them link, and switch it off again.

### Testing locally

Google Identity Services needs a real Client ID and an authorized JavaScript origin, so an end-to-end test requires
the frontend (or a small HTML page on `http://localhost:3000`) that obtains a token and posts it to the API.
The automated tests use a fake validator and never contact Google.

## Email Confirmation and Resend

New accounts must confirm their email address before they can log in. Confirmation tokens are random,
stored only as SHA-256 hashes, valid for 24 hours and single-use.

### How emails are sent

All emails go through the `IEmailSender` abstraction. The implementation is chosen at startup
(`Services/EmailServiceCollectionExtensions.cs`):

| Configuration | Sender | Behaviour |
|---|---|---|
| All `Resend__*` settings and `Email__ConfirmationUrlBase` set (any environment) | `ResendEmailSender` | Sends a real email through the Resend REST API |
| No `Resend__*` settings, `Development` | `DevelopmentEmailSender` | Writes the confirmation link to the console log |
| No `Resend__*` settings, any other environment | `UnavailableEmailSender` | Registration returns `503 email_service_unavailable`; a warning is logged at startup |
| Some `Resend__*` settings, or Resend without a valid `Email__ConfirmationUrlBase` | — | The application **refuses to start** and names the missing setting |

`ResendEmailSender` calls `POST https://api.resend.com/emails` with `HttpClient` (no SDK, no SMTP):

- headers `Authorization: Bearer <key>` and `User-Agent: GameNest-Backend/1.0` (Resend rejects requests without a User-Agent);
- body with `from` (`<FromName> <FromEmail>`), `to`, `subject`, and both an HTML and a plain-text part (in Ukrainian);
- success only for a 2xx response. HTTP errors, network failures and timeouts (`Resend__TimeoutSeconds`, default 15)
  raise `EmailDeliveryException`; a cancelled request (client disconnected) is not reported as a delivery failure;
- logs contain the HTTP status, Resend's error name and a sanitized message (recipient, token, link and key removed) —
  never the API key, the confirmation token, the link or the recipient address.

### Confirmation link

The link is `Email__ConfirmationUrlBase` plus `?email=<url-encoded email>&token=<url-encoded token>`
(query parameters already present in the base URL are kept). Production example:

```text
https://gamenest.pp.ua/confirm-email?email=user%40example.com&token=...
```

The frontend page must read `email` and `token` from the query string and send them unchanged to
`POST /api/Auth/confirm-email` with the JSON body `{"email": "...", "token": "..."}`.

> **Frontend status (checked 2026-11-10):** the current `GameNest_FrontEnd` does not call this API yet (it keeps its data
> in `localStorage`) and has no `/confirm-email` page. Until that page exists, links in the emails lead nowhere.
> Cloudflare must also serve that path (a `confirm-email` page or a single-page-app fallback).

### Behaviour of the endpoints

- `POST /api/Auth/register` → `201` only after Resend accepted the email. If sending fails, the new account is deleted
  again and the API returns `503 email_service_unavailable`; the same email/username can register again later.
  Unique indexes on email and username prevent duplicates.
- `POST /api/Auth/resend-confirmation` → always `202` with the same body (does not reveal whether an email is registered).
  A new token is stored only if the email was sent; otherwise the previous link stays valid.
- `POST /api/Auth/confirm-email` → `200 email_confirmed`, or `400 invalid_or_expired_confirmation_token`.
- An email address is never marked as confirmed because of a send attempt.

Edge case: if Resend accepts the email but its response is lost (timeout), the account is removed and the delivered
link then fails with `400`; the user simply registers again.

### Setting up Resend (manual steps)

1. **Account:** sign up at <https://resend.com>. Check the current free-plan quotas (daily and monthly) in the dashboard.
2. **Domain:** Resend → **Domains** → **Add Domain** → `gamenest.pp.ua` (pick a region close to the users, e.g. EU).
   Resend then shows the DNS records to create — typically:
   - a `TXT` record `resend._domainkey` with the DKIM public key;
   - an `MX` record and a `TXT` SPF record (`v=spf1 include:... ~all`) on the `send` subdomain (bounce handling);
   - optionally a `TXT` record `_dmarc`, e.g. `v=DMARC1; p=none;`.

   Always copy the exact names and values from the Resend dashboard; they are specific to the domain and region.
3. **Cloudflare DNS** (dashboard → `gamenest.pp.ua` → **DNS** → **Records** → **Add record**), for each record from Resend:
   - the same type (`TXT` / `MX`), the name without the zone (e.g. `resend._domainkey`, `send`), the value pasted exactly,
     and the MX priority shown by Resend;
   - proxy status **DNS only** (TXT/MX records are never proxied);
   - these names (`send`, `resend._domainkey`) do not affect existing records of the root domain. If an SPF record already
     exists on the same name, merge the values into one record instead of adding a second one.

   Back in Resend, click **Verify DNS Records** and wait until the domain is **Verified** (propagation can take a while).
4. **API key:** Resend → **API Keys** → **Create API Key**, permission **Sending access**, restricted to `gamenest.pp.ua`.
   The key is shown once: paste it straight into the Render Dashboard as `Resend__ApiKey`. Never store it in files,
   the repository, chats or issues; revoke and replace it if it is ever exposed.
5. **Sender:** `Resend__FromEmail=no-reply@gamenest.pp.ua` and `Resend__FromName=GameNest` → emails come from
   `GameNest <no-reply@gamenest.pp.ua>`. The address must belong to the verified domain.

Until the domain is verified, Resend only allows test sends to your own account address; registering other users then
fails with `503` (Resend answers `403 validation_error`).

### Testing Resend locally (optional)

Put the settings into User Secrets (never into `appsettings*.json`) and register through Swagger:

```powershell
dotnet user-secrets set "Resend:ApiKey" "<key>"
dotnet user-secrets set "Resend:FromEmail" "no-reply@gamenest.pp.ua"
```

`Email:ConfirmationUrlBase` already comes from `appsettings.Development.json`. Remove the secrets again
(`dotnet user-secrets remove "Resend:ApiKey"`, …) to return to the console sender.
The automated tests never contact Resend: they use a fake `HttpMessageHandler`.

### Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Startup fails: `Resend email configuration is incomplete or invalid: ...` | Only some `Resend__*` settings are set, `Resend__FromEmail` is not a plain address, or `Email__ConfirmationUrlBase` is missing / not an absolute URL |
| Startup warning `No email provider is configured`, register → `503` | No `Resend__*` settings outside Development |
| Register → `503`, log `Resend rejected ... HTTP 401` | Invalid, deleted or inactive API key |
| Register → `503`, log `HTTP 403 ... validation_error` | Domain not verified, `FromEmail` not on the verified domain, or test-mode restrictions |
| Register → `503`, log `HTTP 429` | Rate limit or daily/monthly quota exceeded |
| Register → `503`, log `Resend is unreachable` / `did not respond within` | Network/DNS problem or Resend outage; retry later |
| Link opens a 404 page | The frontend `/confirm-email` page does not exist yet |
| `400 invalid_or_expired_confirmation_token` | Link older than 24 h, already used, replaced by a newer resend, or altered |

## First Administrator

There is no HTTP endpoint to assign roles, and users cannot change their own role. The first administrator is
promoted once with a local command built into the application:

```text
dotnet run --no-launch-profile -- admin promote --email <email> [--additional]
```

How it works:

- it runs **instead of** the web server and needs only `ConnectionStrings__DefaultConnection` (no JWT, CORS or Resend settings);
- `--no-launch-profile` keeps the default `Production` environment, so User Secrets are **not** loaded and the database
  is exactly the one from the environment variable;
- the user is looked up by the exact (normalized) email address. Arguments are parsed strictly: unknown arguments,
  several emails, `Name <email>` forms and wildcards are rejected;
- it refuses (and changes nothing) if the user does not exist, the email is not confirmed, or another administrator
  already exists (`--additional` deliberately allows a further administrator);
- it shows the database host/name and the user's id, username, email and role, and asks you to **type the username** to confirm;
- it changes only the `Role` column from `User` to `Admin`, in a transaction (password and other data untouched);
- running it again for a user who is already an administrator is a harmless no-op.

Exit codes: `0` success / already admin, `1` invalid arguments, `2` user not found, `3` email not confirmed,
`4` confirmation did not match, `5` an administrator already exists, `6` missing/invalid connection string, `7` database error.

### Step by step (production, Neon)

1. The future administrator signs up with Google through the frontend (this also confirms the email address).
   With email/password registration enabled, registering and opening the confirmation link works too.
2. Use the same email address in the command below.
3. On your computer, from the repository root (PowerShell):

   ```powershell
   Remove-Item Env:ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
   $env:ConnectionStrings__DefaultConnection = Read-Host "Neon connection string"

   dotnet run --no-launch-profile -- admin promote --email admin@example.com

   Remove-Item Env:ConnectionStrings__DefaultConnection
   ```

   Example session:

   ```text
   About to grant the Admin role:
     database : host=ep-xxxx.eu-central-1.aws.neon.tech, port=5432, database=neondb, user=neondb_owner
     user id  : 1
     username : admin_user
     email    : admin@example.com
     role     : User -> Admin
   Type the username 'admin_user' to confirm (anything else aborts): admin_user
   Done: user 'admin_user' (admin@example.com, id 1) is now an administrator.
   Existing tokens keep the old role until they expire (up to 1 hour): the user must log in again.
   ```

4. The user logs in again to receive a token with the `Admin` role.

No bootstrap password or admin credentials exist anywhere: the administrator keeps signing in with Google
(or the password they registered with).
Do not add anything to Render for this — the command is run locally only.

`scripts/bootstrap-first-admin.sql` remains as an equivalent manual alternative
(`psql "<connection string>" -v ON_ERROR_STOP=1 -f scripts/bootstrap-first-admin.sql` after replacing the placeholder email locally).

## Security

- Passwords are stored as BCrypt hashes (8+ characters, at most 72 UTF-8 bytes, common passwords rejected).
- Login responses and timing are the same for unknown emails and wrong passwords.
- Tokens are HS256-signed, valid for one hour, and must contain a valid user id; detailed token errors are shown only in Development.
- Authentication endpoints are rate limited per client IP and endpoint (`429` with `Retry-After`).
- Roles can only be changed with direct database access (the admin command or SQL script), never through the API.
- Swagger UI and OpenAPI JSON are available only in Development.
- HTTPS redirection is not enabled in the application; TLS is terminated by Render / Cloudflare.
- Secrets (database password, JWT key, Resend API key) must never be committed. Any secret that has ever been
  committed must be considered compromised and rotated.

## Known Production Limitations

1. **The frontend is not connected to the API yet**: it has no Google sign-in button and does not call
   `POST /api/Auth/google`. Until it does, nobody can sign up in production, the first administrator cannot be
   promoted, and the catalog stays empty (there is no seed data).
2. **Google sign-in has only been tested with a fake validator** (and the real library rejecting malformed tokens);
   a sign-in with a real Google account must be checked once the Client ID and the frontend exist.
3. The Google ID-token flow needs Google's signing certificates; if they cannot be downloaded, sign-in returns `503`.
4. Email/password registration (disabled by default) depends on Resend: with it enabled, the domain must be verified
   and all `Resend__*` variables set, otherwise it returns `503`. The `/confirm-email` frontend page does not exist yet.
5. **Rate limiting behind Render's proxy**: the limiter uses the direct connection IP. Forwarded headers are not trusted,
   so on Render all clients may share the proxy's address and therefore one limit (10 auth requests per minute in total
   by default). Trusting `X-Forwarded-For` safely requires knowing the proxy chain; this is not configured yet.
6. The rate limiter is in-memory and per instance; limits reset when the service restarts.
7. **Render Free** spins the service down after inactivity; the first request afterwards can take up to about a minute.
   **Neon Free** suspends the compute when idle, which adds latency to the first database query.
8. The health check does not verify database connectivity.
9. Migrations must be applied manually (see above).
10. Users cannot unlink Google or set a password; there is no account-deletion endpoint.
11. An unconfirmed email/password account blocks Google sign-up for that address (`409 email_registered_unconfirmed`)
    until an administrator deletes the stale account; nothing is taken over automatically.
12. A Google account with an external address (kind "Other") occupies that email in GameNest; if the real mailbox
    owner later signs in with a different Google account, they get `409 account_linked_to_another_google_account`
    and an administrator has to resolve it.

## License

See [LICENSE](LICENSE).
