using GameNest_BackEnd.Data;
using GameNest_BackEnd.Migrations;
using GameNest_BackEnd.Models;
using GameNest_BackEnd.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace GameNest_BackEnd.Tests;

/// <summary>Email normalization on save and the case-insensitive unique index.</summary>
public sealed class EmailUniquenessTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbContextOptions<GameNestDbContext> _options;

    public EmailUniquenessTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<GameNestDbContext>().UseSqlite(_connection).Options;
        using var db = new GameNestDbContext(_options);
        db.Database.EnsureCreated();
        // Same index as migration AddUsersEmailLowerUniqueIndex (SQLite supports expression indexes too).
        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX "IX_Users_Email_Lower" ON "Users" (lower("Email"));""");
    }

    public void Dispose() => _connection.Dispose();

    private static User NewUser(string email, string username) =>
        new() { Username = username, Email = email, PasswordHash = null, Role = "User", EmailConfirmed = true };

    private void InsertRaw(string email, string username) =>
        new GameNestDbContext(_options).Database.ExecuteSqlRaw(
            """INSERT INTO "Users" ("Username","Email","Role","EmailConfirmed","IsProfilePublic","IsLibraryPublic") VALUES ({0},{1},'User',1,1,1)""", username, email);

    [Fact]
    public void New_users_are_saved_with_a_trimmed_lower_case_email()
    {
        using (var db = new GameNestDbContext(_options))
        {
            db.Users.Add(NewUser("  Mixed.Case@Example.COM ", "mixed"));
            db.SaveChanges();
        }
        using var check = new GameNestDbContext(_options);
        Assert.Equal("mixed.case@example.com", check.Users.Single().Email);
    }

    [Fact]
    public async Task Explicit_email_changes_are_normalized_but_unrelated_updates_leave_legacy_values_alone()
    {
        InsertRaw("Legacy@Example.com", "legacy");
        await using (var db = new GameNestDbContext(_options))
        {
            var user = await db.Users.SingleAsync();
            user.AvatarUrl = "https://example.test/a.png"; // unrelated change, e.g. while linking Google
            await db.SaveChangesAsync();
        }
        await using (var db = new GameNestDbContext(_options))
            Assert.Equal("Legacy@Example.com", (await db.Users.SingleAsync()).Email);

        await using (var db = new GameNestDbContext(_options))
        {
            var user = await db.Users.SingleAsync();
            user.Email = "New@Example.COM";
            await db.SaveChangesAsync();
        }
        await using (var db = new GameNestDbContext(_options))
            Assert.Equal("new@example.com", (await db.Users.SingleAsync()).Email);
    }

    [Fact]
    public void Database_rejects_case_variant_duplicates_even_when_normalization_is_bypassed()
    {
        InsertRaw("player@example.com", "p1");

        var ex = Assert.Throws<SqliteException>(() => InsertRaw("Player@Example.com", "p2"));
        Assert.Contains("UNIQUE", ex.Message);
    }

    [Fact]
    public void Saving_a_case_variant_through_ef_fails_with_DbUpdateException()
    {
        InsertRaw("Player@Example.com", "legacy"); // legacy, not normalized
        using var db = new GameNestDbContext(_options);
        db.Users.Add(NewUser("player@example.com", "fresh"));

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void Migration_creates_and_drops_the_lower_email_unique_index()
    {
        var migration = new AddUsersEmailLowerUniqueIndex();

        var up = string.Join("\n", migration.UpOperations.OfType<SqlOperation>().Select(o => o.Sql));
        var down = string.Join("\n", migration.DownOperations.OfType<SqlOperation>().Select(o => o.Sql));

        Assert.Contains("""CREATE UNIQUE INDEX "IX_Users_Email_Lower" ON "Users" (lower("Email"))""", up);
        Assert.Contains("RAISE EXCEPTION", up); // explicit failure instead of a cryptic error when duplicates exist
        Assert.Contains("""DROP INDEX IF EXISTS "IX_Users_Email_Lower""", down);
        Assert.DoesNotContain(migration.UpOperations, o => o is not SqlOperation); // no other schema changes
    }
}

/// <summary>Auth switches taken only from appsettings.json, i.e. the production defaults.</summary>
public class ProductionDefaultsApiFactory : GoogleApiFactory
{
    protected override bool EnablePasswordAuth => false;
}

public class ProductionDefaultsTests(ProductionDefaultsApiFactory factory) : IClassFixture<ProductionDefaultsApiFactory>
{
    [Fact]
    public async Task Password_login_and_registration_are_off_by_default_and_google_works()
    {
        var client = factory.CreateClient();
        var seeded = await factory.SeedUserAsync(password: "seeded-password-1");

        var login = await client.PostAsJsonAsync("/api/Auth/login", new { email = seeded.Email, password = "seeded-password-1" });
        var register = await client.PostAsJsonAsync("/api/Auth/register", new { username = "u" + TestHelpers.UniqueSuffix(), email = $"u{TestHelpers.UniqueSuffix()}@test.local", password = "some-password-123" });
        var google = await client.PostAsJsonAsync("/api/Auth/google", new
        {
            idToken = factory.Google.Issue(new GoogleIdentity("sub-" + Guid.NewGuid().ToString("N"), $"g{TestHelpers.UniqueSuffix()}@gmail.com", "Player", null))
        });

        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Equal("password_login_disabled", (await login.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, register.StatusCode);
        Assert.Equal("password_registration_disabled", (await register.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, google.StatusCode);
    }
}

/// <summary>What a Google user with an external address (EmailConfirmed = false) can do.</summary>
public class UnconfirmedGoogleUserAccessTests(GoogleApiFactory factory) : IClassFixture<GoogleApiFactory>
{
    private async Task<(int UserId, HttpClient Client, GoogleIdentity Identity)> SignUpThirdPartyAsync()
    {
        var identity = new GoogleIdentity("sub-" + Guid.NewGuid().ToString("N"), $"ext{TestHelpers.UniqueSuffix()}@outlook.test", "External User", null);
        var response = await factory.CreateClient().PostAsJsonAsync("/api/Auth/google", new { idToken = factory.Google.Issue(identity) });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        return (body.GetProperty("user").GetProperty("id").GetInt32(), factory.CreateClientWithToken(body.GetProperty("token").GetString()!), identity);
    }

    private async Task<int> SeedGameAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameNestDbContext>();
        var game = new Game { Title = "Access Test " + TestHelpers.UniqueSuffix(), Description = "d" };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return game.Id;
    }

    [Fact]
    public async Task Unconfirmed_google_user_can_use_the_regular_features()
    {
        var (_, client, _) = await SignUpThirdPartyAsync();
        var gameId = await SeedGameAsync();

        var profile = await client.GetAsync("/api/Profile");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.False((await profile.ReadJsonAsync()).GetProperty("emailConfirmed").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/Games")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/Games/{gameId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/Library/{gameId}", new { status = "Playing", purchased = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/Library")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/Submissions", new { type = "NewGame", title = "Suggested " + TestHelpers.UniqueSuffix() })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/UserGames", new { title = "My homebrew" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/Profile/privacy", new { isProfilePublic = false, isLibraryPublic = false })).StatusCode);
    }

    [Fact]
    public async Task Unconfirmed_google_user_gets_no_admin_rights()
    {
        var (_, client, _) = await SignUpThirdPartyAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/Profile/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/Games", new { title = "Nope" })).StatusCode);
    }

    [Fact]
    public async Task Repeated_google_sign_in_never_changes_the_stored_email_or_confirmation()
    {
        var (userId, _, identity) = await SignUpThirdPartyAsync();
        var changed = identity with { Email = $"changed{TestHelpers.UniqueSuffix()}@gmail.com" }; // now even a Gmail address

        var response = await factory.CreateClient().PostAsJsonAsync("/api/Auth/google", new { idToken = factory.Google.Issue(changed) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<GameNestDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        Assert.Equal(identity.Email, user.Email);
        Assert.False(user.EmailConfirmed);
    }
}
