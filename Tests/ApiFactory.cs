using GameNest_BackEnd.Data;
using GameNest_BackEnd.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GameNest_BackEnd.Tests;

/// <summary>
/// Hosts the API against an isolated in-memory SQLite database and a capturing email sender.
/// Rate limits are raised so that ordinary tests are never throttled; see <see cref="RateLimitedApiFactory"/>.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string JwtKey = "test-signing-key-that-is-long-enough-for-hmac-sha256";
    public const string JwtIssuer = "GameNest.Tests";
    public const string JwtAudience = "GameNest.Tests";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public List<(string Email, string Token)> SentConfirmations { get; } = [];

    /// <summary>When true, the test email sender throws <see cref="EmailDeliveryException"/>.</summary>
    public bool FailEmailDelivery { get; set; }

    public ApiFactory()
    {
        Environment.SetEnvironmentVariable("Jwt__Key", JwtKey);
        Environment.SetEnvironmentVariable("Jwt__Issuer", JwtIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", JwtAudience);
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "unused");
    }

    protected virtual IDictionary<string, string?> ExtraConfiguration => new Dictionary<string, string?>
    {
        ["RateLimiting:Auth:PermitLimit"] = "10000",
        ["RateLimiting:ResendConfirmation:PermitLimit"] = "10000"
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(c =>
        {
            c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = JwtKey,
                ["Jwt:Issuer"] = JwtIssuer,
                ["Jwt:Audience"] = JwtAudience,
                ["ConnectionStrings:DefaultConnection"] = "unused"
            });
            c.AddInMemoryCollection(ExtraConfiguration);
        });
        builder.ConfigureServices(services =>
        {
            foreach (var d in services.Where(d =>
                         d.ServiceType.Assembly.GetName().Name?.StartsWith("Npgsql", StringComparison.Ordinal) == true ||
                         d.ImplementationType?.Assembly.GetName().Name?.StartsWith("Npgsql", StringComparison.Ordinal) == true ||
                         d.ServiceType.GenericTypeArguments.Contains(typeof(GameNestDbContext)) ||
                         d.ServiceType.GenericTypeArguments.Contains(typeof(DbContextOptions<GameNestDbContext>))).ToList())
                services.Remove(d);
            services.RemoveAll<DbContextOptions<GameNestDbContext>>();
            services.RemoveAll<GameNestDbContext>();
            services.AddDbContext<GameNestDbContext>(o => o.UseSqlite(_connection));
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(new TestEmailSender(this));
        });
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<GameNestDbContext>().Database.EnsureCreatedAsync();
    }

    Task IAsyncLifetime.DisposeAsync()
    {
        _connection.Dispose();
        Dispose();
        return Task.CompletedTask;
    }

    private sealed class TestEmailSender(ApiFactory factory) : IEmailSender
    {
        public Task SendEmailConfirmationAsync(string email, string token, CancellationToken cancellationToken = default)
        {
            if (factory.FailEmailDelivery) throw new EmailDeliveryException("Simulated delivery failure.");
            factory.SentConfirmations.Add((email, token));
            return Task.CompletedTask;
        }
    }
}
