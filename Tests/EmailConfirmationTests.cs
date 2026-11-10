using GameNest_BackEnd.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace GameNest_BackEnd.Tests;

public class EmailConfirmationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "correct-horse-battery";

    private async Task<(string Email, string Token)> RegisterAsync(HttpClient client)
    {
        var s = TestHelpers.UniqueSuffix();
        var email = $"reg{s}@test.local";
        var response = await client.PostAsJsonAsync("/api/Auth/register", new { username = "reg" + s, email, password = Password });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (email, factory.SentConfirmations.Last(x => x.Email == email).Token);
    }

    private async Task ExpireTokenAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameNestDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        user.EmailConfirmationTokenExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Registration_stores_only_a_token_hash_with_a_24_hour_expiry()
    {
        var (email, token) = await RegisterAsync(factory.CreateClient());

        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<GameNestDbContext>().Users.AsNoTracking().SingleAsync(u => u.Email == email);
        Assert.False(user.EmailConfirmed);
        Assert.Equal("User", user.Role);
        Assert.NotNull(user.EmailConfirmationTokenHash);
        Assert.NotEqual(token, user.EmailConfirmationTokenHash);
        Assert.InRange(user.EmailConfirmationTokenExpiresAt!.Value, DateTime.UtcNow.AddHours(23.9), DateTime.UtcNow.AddHours(24.1));
    }

    [Fact]
    public async Task Confirmed_user_can_log_in_and_the_token_cannot_be_reused()
    {
        var client = factory.CreateClient();
        var (email, token) = await RegisterAsync(client);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/Auth/confirm-email", new { email, token })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/Auth/confirm-email", new { email, token })).StatusCode);
        var jwt = await client.LoginAsync(email, Password);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClientWithToken(jwt).GetAsync("/api/Profile")).StatusCode);
    }

    [Fact]
    public async Task Expired_confirmation_token_is_rejected()
    {
        var client = factory.CreateClient();
        var (email, token) = await RegisterAsync(client);
        await ExpireTokenAsync(email);

        var response = await client.PostAsJsonAsync("/api/Auth/confirm-email", new { email, token });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_or_expired_confirmation_token", (await response.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Email_delivery_failure_returns_503_and_does_not_keep_the_account()
    {
        var client = factory.CreateClient();
        var s = TestHelpers.UniqueSuffix();
        var email = $"fail{s}@test.local";
        factory.FailEmailDelivery = true;
        HttpResponseMessage response;
        try { response = await client.PostAsJsonAsync("/api/Auth/register", new { username = "fail" + s, email, password = Password }); }
        finally { factory.FailEmailDelivery = false; }

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("email_service_unavailable", (await response.ReadJsonAsync()).GetProperty("code").GetString());
        using (var scope = factory.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<GameNestDbContext>().Users.AnyAsync(u => u.Email == email));

        // The same email and username can be registered again once delivery works.
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/Auth/register", new { username = "fail" + s, email, password = Password })).StatusCode);
    }

    [Fact]
    public async Task Resend_issues_a_new_token_and_invalidates_the_previous_one()
    {
        var client = factory.CreateClient();
        var (email, oldToken) = await RegisterAsync(client);
        await ExpireTokenAsync(email);

        var response = await client.PostAsJsonAsync("/api/Auth/resend-confirmation", new { email = email.ToUpperInvariant() });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var newToken = factory.SentConfirmations.Last(x => x.Email == email).Token;
        Assert.NotEqual(oldToken, newToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/Auth/confirm-email", new { email, token = oldToken })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/Auth/confirm-email", new { email, token = newToken })).StatusCode);
    }

    [Fact]
    public async Task Resend_returns_the_same_response_for_unknown_confirmed_and_unconfirmed_emails()
    {
        var client = factory.CreateClient();
        var (unconfirmed, _) = await RegisterAsync(client);
        var confirmed = (await factory.SeedUserAsync()).Email;
        var unknown = $"nobody{TestHelpers.UniqueSuffix()}@test.local";

        var bodies = new List<string>();
        foreach (var email in new[] { unknown, confirmed, unconfirmed })
        {
            var response = await client.PostAsJsonAsync("/api/Auth/resend-confirmation", new { email });
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            bodies.Add(await response.Content.ReadAsStringAsync());
        }

        Assert.Single(bodies.Distinct());
        Assert.DoesNotContain(factory.SentConfirmations, x => x.Email == unknown || x.Email == confirmed);
        Assert.Equal(2, factory.SentConfirmations.Count(x => x.Email == unconfirmed));
        Assert.All(bodies, b => Assert.DoesNotContain("token", b, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Failed_resend_keeps_the_previous_token_valid_and_still_returns_the_generic_response()
    {
        var client = factory.CreateClient();
        var (email, token) = await RegisterAsync(client);

        factory.FailEmailDelivery = true;
        HttpResponseMessage response;
        try { response = await client.PostAsJsonAsync("/api/Auth/resend-confirmation", new { email }); }
        finally { factory.FailEmailDelivery = false; }

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/Auth/confirm-email", new { email, token })).StatusCode);
    }
}
