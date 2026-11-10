using GameNest_BackEnd.Cli;
using GameNest_BackEnd.Data;
using GameNest_BackEnd.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GameNest_BackEnd.Tests;

public sealed class AdminCommandTests : IDisposable
{
    private const string Target = "test database";
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbContextOptions<GameNestDbContext> _options;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public AdminCommandTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<GameNestDbContext>().UseSqlite(_connection).Options;
        using var db = new GameNestDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private User Seed(string username, string email, bool confirmed = true, string role = "User")
    {
        using var db = new GameNestDbContext(_options);
        var user = new User { Username = username, Email = email, PasswordHash = "hash-of-" + username, Role = role, EmailConfirmed = confirmed };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private User Load(int id)
    {
        using var db = new GameNestDbContext(_options);
        return db.Users.AsNoTracking().Single(u => u.Id == id);
    }

    private async Task<int> PromoteAsync(string email, string? typed, bool additional = false)
    {
        await using var db = new GameNestDbContext(_options);
        return await AdminCommand.PromoteAsync(db, new AdminCommand.PromoteRequest(email, additional), Target,
            new StringReader(typed is null ? "" : typed + Environment.NewLine), _output, _error);
    }

    // ---------- argument parsing ----------

    [Theory]
    [InlineData("admin|promote|--email|Admin@Example.com", "admin@example.com", false)]
    [InlineData("admin|promote|--email=admin@example.com|--additional", "admin@example.com", true)]
    [InlineData("admin|promote|--additional|--email| admin@example.com ", "admin@example.com", true)]
    public void Valid_arguments_are_parsed(string args, string email, bool additional)
    {
        var request = AdminCommand.Parse(args.Split('|'), _error);

        Assert.NotNull(request);
        Assert.Equal(email, request.Email);
        Assert.Equal(additional, request.AllowAdditional);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("admin|promte|--email|a@example.com")]
    [InlineData("admin|promote")]
    [InlineData("admin|promote|--email")]
    [InlineData("admin|promote|--email|--additional")]
    [InlineData("admin|promote|--email|a@example.com|--email|b@example.com")]
    [InlineData("admin|promote|--email|a@example.com|extra")]
    [InlineData("admin|promote|--email|a@example.com|--yes")]
    [InlineData("admin|promote|--email|a@example.com,b@example.com")]
    [InlineData("admin|promote|--email|Admin <a@example.com>")]
    [InlineData("admin|promote|--email|*@example.com")]
    [InlineData("admin|promote|--email|not-an-email")]
    [InlineData("admin|promote|--email=")]
    public void Invalid_arguments_are_rejected(string args) =>
        Assert.Null(AdminCommand.Parse(args.Split('|'), _error));

    [Fact]
    public async Task Invalid_arguments_never_touch_the_database()
    {
        var user = Seed("alice", "alice@example.com");
        var config = new ConfigurationBuilder().Build(); // no connection string: reaching the database would fail differently

        var code = await AdminCommand.RunAsync(["admin", "promote", "--email", "alice@example.com", "--force"], config, new StringReader("alice\n"), _output, _error);

        Assert.Equal(AdminCommand.UsageError, code);
        Assert.Equal("User", Load(user.Id).Role);
    }

    [Fact]
    public async Task Missing_connection_string_is_a_configuration_error()
    {
        var code = await AdminCommand.RunAsync(["admin", "promote", "--email", "alice@example.com"], new ConfigurationBuilder().Build(),
            new StringReader("alice\n"), _output, _error);

        Assert.Equal(AdminCommand.ConfigurationError, code);
        Assert.Contains("ConnectionStrings__DefaultConnection", _error.ToString());
    }

    // ---------- promotion ----------

    [Fact]
    public async Task Confirmed_user_is_promoted_after_typing_the_username()
    {
        var alice = Seed("alice", "alice@example.com");
        var bob = Seed("bob", "bob@example.com");

        var code = await PromoteAsync("alice@example.com", "alice");

        Assert.Equal(AdminCommand.Success, code);
        var promoted = Load(alice.Id);
        Assert.Equal("Admin", promoted.Role);
        Assert.Equal("hash-of-alice", promoted.PasswordHash);
        Assert.True(promoted.EmailConfirmed);
        Assert.Equal("User", Load(bob.Id).Role);
        Assert.Contains("is now an administrator", _output.ToString());
        Assert.Contains(Target, _output.ToString());
    }

    [Fact]
    public async Task Unknown_email_changes_nothing()
    {
        var alice = Seed("alice", "alice@example.com");

        Assert.Equal(AdminCommand.UserNotFound, await PromoteAsync("alice@example.org", "alice"));
        Assert.Equal("User", Load(alice.Id).Role);
        Assert.Contains("Nothing was changed", _error.ToString());
    }

    [Fact]
    public async Task Unconfirmed_email_changes_nothing()
    {
        var carol = Seed("carol", "carol@example.com", confirmed: false);

        Assert.Equal(AdminCommand.EmailNotConfirmed, await PromoteAsync("carol@example.com", "carol"));
        Assert.Equal("User", Load(carol.Id).Role);
    }

    [Theory]
    [InlineData("bob")]        // someone else's username
    [InlineData("Alice")]      // case matters
    [InlineData("yes")]
    [InlineData("")]
    [InlineData(null)]         // end of input
    public async Task Wrong_or_missing_confirmation_changes_nothing(string? typed)
    {
        var alice = Seed("alice", "alice@example.com");
        Seed("bob", "bob@example.com");

        Assert.Equal(AdminCommand.Aborted, await PromoteAsync("alice@example.com", typed));
        Assert.Equal("User", Load(alice.Id).Role);
    }

    [Fact]
    public async Task Running_the_command_again_is_a_harmless_no_op()
    {
        var alice = Seed("alice", "alice@example.com");
        Assert.Equal(AdminCommand.Success, await PromoteAsync("alice@example.com", "alice"));

        Assert.Equal(AdminCommand.Success, await PromoteAsync("alice@example.com", null));

        Assert.Equal("Admin", Load(alice.Id).Role);
        Assert.Contains("already an administrator", _output.ToString());
    }

    [Fact]
    public async Task Second_administrator_requires_the_additional_flag()
    {
        Seed("root", "root@example.com", role: "Admin");
        var alice = Seed("alice", "alice@example.com");

        Assert.Equal(AdminCommand.AdminAlreadyExists, await PromoteAsync("alice@example.com", "alice"));
        Assert.Equal("User", Load(alice.Id).Role);

        Assert.Equal(AdminCommand.Success, await PromoteAsync("alice@example.com", "alice", additional: true));
        Assert.Equal("Admin", Load(alice.Id).Role);
    }
}
