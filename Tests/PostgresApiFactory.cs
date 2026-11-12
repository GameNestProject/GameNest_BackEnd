using GameNest_BackEnd.Data;
using GameNest_BackEnd.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace GameNest_BackEnd.Tests;

/// <summary>
/// Opt-in tests against a real PostgreSQL server, for behaviour SQLite cannot reproduce (row locks, concurrent writes).
/// Enabled by GAMENEST_TEST_POSTGRES = a connection string to a server the tests may create databases on, e.g.
/// "Host=localhost;Port=5432;Username=postgres;Password=…". Never point it at a production server.
/// Without it the tests are reported as skipped.
/// </summary>
public static class PostgresTestSettings
{
    public const string Variable = "GAMENEST_TEST_POSTGRES";
    public static string? ServerConnectionString => Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } v ? v : null;
    public const string SkipReason = "Set " + Variable + " to a PostgreSQL server connection string to run the PostgreSQL concurrency tests.";
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (PostgresTestSettings.ServerConnectionString is null) Skip = PostgresTestSettings.SkipReason;
    }
}

/// <summary>
/// The real API (Testing environment) on a fresh, uniquely named PostgreSQL database with all EF Core migrations applied.
/// The database is dropped when the fixture is disposed.
/// </summary>
public sealed class PostgresApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string? _server = PostgresTestSettings.ServerConnectionString;
    private readonly string _database = "gamenest_test_" + Guid.NewGuid().ToString("N")[..12];

    public PostgresApiFactory()
    {
        // Program reads these before the test host's configuration is applied (same values as ApiFactory).
        Environment.SetEnvironmentVariable("Jwt__Key", ApiFactory.JwtKey);
        Environment.SetEnvironmentVariable("Jwt__Issuer", ApiFactory.JwtIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", ApiFactory.JwtAudience);
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "unused");
        Environment.SetEnvironmentVariable("CORS_ALLOWED_ORIGINS", ApiFactory.AllowedOrigin);
        Environment.SetEnvironmentVariable("Google__ClientId", ApiFactory.GoogleClientId);
    }

    public bool Enabled => _server is not null;

    /// <summary>Connection string of the temporary test database.</summary>
    public string ConnectionString => new NpgsqlConnectionStringBuilder(_server) { Database = _database, Pooling = true, MaxPoolSize = 50 }.ConnectionString;

    private string AdminConnectionString => new NpgsqlConnectionStringBuilder(_server) { Database = "postgres", Pooling = false }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = ApiFactory.JwtKey,
            ["Jwt:Issuer"] = ApiFactory.JwtIssuer,
            ["Jwt:Audience"] = ApiFactory.JwtAudience,
            ["CORS_ALLOWED_ORIGINS"] = ApiFactory.AllowedOrigin,
            ["Google:ClientId"] = ApiFactory.GoogleClientId
        }));
        builder.ConfigureServices(services =>
        {
            // Program registered the DbContext with the connection string it read at startup; use the test database instead.
            foreach (var d in services.Where(d => d.ServiceType.GenericTypeArguments.Contains(typeof(GameNestDbContext))).ToList())
                services.Remove(d); // incl. IDbContextOptionsConfiguration<GameNestDbContext>
            services.RemoveAll<DbContextOptions<GameNestDbContext>>();
            services.RemoveAll<GameNestDbContext>();
            services.AddDbContext<GameNestDbContext>(o => o.UseNpgsql(ConnectionString));
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender, NoEmailSender>();
        });
    }

    public async Task InitializeAsync()
    {
        if (!Enabled) return;
        await using (var admin = new NpgsqlConnection(AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        await using var db = new GameNestDbContext(new DbContextOptionsBuilder<GameNestDbContext>().UseNpgsql(ConnectionString).Options);
        await db.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (!Enabled) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(AdminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Holds a row lock on a submission (SELECT … FOR UPDATE in an open transaction) so that requests sent meanwhile
    /// queue up on that row in PostgreSQL. Disposing the gate commits and releases them.
    /// </summary>
    public async Task<RowLockGate> LockSubmissionAsync(int submissionId)
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(ConnectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        await using (var select = new NpgsqlCommand("""SELECT 1 FROM "GameSubmissions" WHERE "Id" = @id FOR UPDATE""", connection, transaction))
        {
            select.Parameters.AddWithValue("id", submissionId);
            await select.ExecuteScalarAsync();
        }
        return new RowLockGate(connection, transaction);
    }

    /// <summary>Number of other sessions on the test database currently waiting for a lock.</summary>
    public async Task<int> SessionsWaitingForLocksAsync()
    {
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(ConnectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'""", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    /// <summary>Waits until exactly <paramref name="count"/> sessions are blocked on locks (fails after a timeout).</summary>
    public async Task WaitForBlockedSessionsAsync(int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (await SessionsWaitingForLocksAsync() >= count) return;
            await Task.Delay(25);
        }
        throw new TimeoutException($"Expected {count} request(s) to be blocked on the row lock.");
    }

    public sealed class RowLockGate(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await transaction.CommitAsync();
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class NoEmailSender : IEmailSender
    {
        public Task SendEmailConfirmationAsync(string email, string token, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
