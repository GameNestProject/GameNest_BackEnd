# GameNest Backend

Backend API for **GameNest** — a platform for browsing a game catalog and managing a personal game library.

## Tech Stack

- **C# / ASP.NET Core Web API** — backend application
- **PostgreSQL** — relational database
- **Entity Framework Core** — database access and migrations
- **JWT** — authentication and authorization
- **Swagger / OpenAPI** — API documentation and testing

## Current Features

- User registration and login
- Password hashing with BCrypt
- JWT-based authentication
- Email confirmation (development console link) and resending confirmation
- User profile and privacy settings
- Role-based authorization (`User` and `Admin`)
- Game catalog, personal library, user-created games
- Game submissions with admin moderation (approve, reject, edit pending)
- Authentication rate limiting
- Swagger UI for API testing

## Project Structure

```text
GameNest_BackEnd/
├── Controllers/    # API endpoints
├── Data/           # Database context
├── DTOs/            # Data transfer objects
├── Models/          # Data models
├── Services/        # Email senders, rate limiting
├── Migrations/      # EF Core database migrations
├── Properties/
├── scripts/         # One-time operational SQL scripts
├── Tests/           # xUnit integration and unit tests
├── appsettings.json
└── Program.cs
```

## Getting Started

### Requirements

- .NET SDK 10
- PostgreSQL
- Git

### Setup

1. Clone the repository:

   ```bash
   git clone <repository-url>
   cd GameNest_BackEnd
   ```

2. Configure the secrets as described in [Configuration](#configuration).

3. Apply database migrations:

   ```bash
   dotnet ef database update
   ```

4. Run the application:

   ```bash
   dotnet run
   ```

5. Open the Swagger UI using the URL shown in the console output, followed by `/swagger`.

### Running the tests

The tests use an isolated in-memory SQLite database and never connect to PostgreSQL:

```powershell
dotnet test .\Tests\GameNest_BackEnd.Tests.csproj
```

## Configuration

`appsettings.json` contains only non-secret settings (`Jwt:Issuer`, `Jwt:Audience`, logging).
Secrets are never committed. The application refuses to start if any of these settings is missing or empty:

| Setting | Secret | Description |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | yes | PostgreSQL connection string |
| `Jwt:Key` | yes | JWT signing key, at least 32 bytes (64 random bytes recommended) |
| `Jwt:Issuer` | no | Defined in `appsettings.json` |
| `Jwt:Audience` | no | Defined in `appsettings.json` |

### Development: User Secrets

Run from the project directory (PowerShell). Every developer generates their own JWT key; keys are never shared.

```powershell
# PostgreSQL connection string (replace the placeholders)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=gamenest;Username=<user>;Password=<password>"

# Generate and store a random 64-byte JWT key
$bytes = New-Object byte[] 64
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
dotnet user-secrets set "Jwt:Key" ([Convert]::ToBase64String($bytes))
Remove-Variable bytes
```

### Production: environment variables

User Secrets are loaded only in the `Development` environment. In Production, provide secrets as environment variables
(`__` replaces `:`), using the hosting provider's secret storage:

```text
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=<connection string>
Jwt__Key=<signing key>
```

Do not put secrets into `appsettings.json` or `appsettings.Production.json`.

### Optional settings

| Setting | Default | Description |
|---|---|---|
| `Email:ConfirmationUrlBase` | `http://localhost:3000/confirm-email` in Development | Page that confirms an email; `email` and `token` query parameters are appended |
| `RateLimiting:Auth:PermitLimit` / `WindowSeconds` | `10` / `60` | Limit for register, login and confirm-email |
| `RateLimiting:ResendConfirmation:PermitLimit` / `WindowSeconds` | `5` / `900` | Limit for resend-confirmation |

## Email confirmation

New accounts must confirm their email address before they can log in. Confirmation tokens are random,
stored only as SHA-256 hashes, valid for 24 hours and single-use.

**SMTP / email provider integration is not implemented yet.**

- **Development:** no email is sent. The confirmation link is written to the application console
  (`[Development email] Confirmation link for ...`). Open the link in the frontend, or call
  `POST /api/Auth/confirm-email` with the `email` and `token` shown in the same log line.
- **Other environments (Production, Staging, ...):** there is no email provider, so registration returns
  `503 email_service_unavailable` and the account is **not** created. Production registration therefore
  requires a real `IEmailSender` implementation (SMTP or an email API) before going live.
- `POST /api/Auth/resend-confirmation` issues a new token (the previous one stops working) for unconfirmed
  accounts. It always returns `202` with the same body, so it does not reveal whether an email is registered.

## First administrator

There is no API to assign roles. The first administrator is created once, directly in the database:

1. Register the future administrator through the API and confirm the email address.
2. Edit `scripts/bootstrap-first-admin.sql` locally and replace the placeholder email (do not commit that change).
3. Run it against the target database:

   ```powershell
   psql -h <host> -U <user> -d gamenest -v ON_ERROR_STOP=1 -f scripts/bootstrap-first-admin.sql
   ```

4. The user must log in again to receive a token with the `Admin` role.

The script runs in a transaction and aborts if an administrator already exists, the placeholder was not replaced,
the email does not match exactly one user, or that user's email is not confirmed.

## Security

- Passwords are stored as BCrypt hashes. New passwords need at least 8 characters, at most 72 UTF-8 bytes
  (the BCrypt limit) and must not be a very common password.
- Login responses and timing are the same for unknown emails and wrong passwords.
- Protected endpoints require authentication; administrative endpoints are restricted to the `Admin` role.
  Tokens are HS256-signed, valid for one hour, and must contain a valid user id.
- Authentication endpoints are rate limited per client IP and endpoint (`429 Too Many Requests` with `Retry-After`).
  The limiter is in-memory: limits apply per application instance and are not shared between instances.
  Behind a reverse proxy, configure the forwarded-headers middleware with the proxy as a known proxy;
  otherwise all clients share the proxy's IP address. Forwarded headers are not trusted by default.
- HTTPS redirection is not enabled in the application. In production, terminate TLS at the hosting
  provider or reverse proxy, or enable HTTPS redirection and HSTS in `Program.cs`.
- Secrets and credentials must not be committed to the repository.

## Project Status

GameNest Backend is under active development. Additional game management, personal library, moderation, and other features are planned.

## License

No license has been specified yet.
