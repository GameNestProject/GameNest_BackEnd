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
- User profile endpoint
- Role-based authorization (`User` and `Admin`)
- Basic game catalog endpoints
- Swagger UI for API testing

## Project Structure

```text
GameNest_BackEnd/
├── Controllers/    # API endpoints
├── Data/           # Database context
├── DTOs/            # Data transfer objects
├── Models/          # Data models
├── Migrations/      # EF Core database migrations
├── Properties/
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

2. Configure the database connection string and JWT settings using .NET User Secrets or environment variables.

3. Apply database migrations:

   ```bash
   dotnet ef database update
   ```

4. Run the application:

   ```bash
   dotnet run
   ```

5. Open the Swagger UI using the URL shown in the console output, followed by `/swagger`.

## Security

- Passwords are stored as BCrypt hashes.
- Protected endpoints require authentication.
- Access to administrative endpoints is restricted by role.
- Secrets and credentials must not be committed to the repository.

## Project Status

GameNest Backend is under active development. Additional game management, personal library, moderation, and other features are planned.

## License

No license has been specified yet.
