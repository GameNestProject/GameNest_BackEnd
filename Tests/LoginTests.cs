using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace GameNest_BackEnd.Tests;

public class LoginTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "seeded-password-1";

    [Fact]
    public async Task Valid_credentials_return_a_token_accepted_by_protected_endpoints()
    {
        var user = await factory.SeedUserAsync(password: Password);
        var token = await factory.CreateClient().LoginAsync(user.Email.ToUpperInvariant(), Password);

        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClientWithToken(token).GetAsync("/api/Profile")).StatusCode);
    }

    [Fact]
    public async Task Unknown_email_and_wrong_password_return_the_same_response()
    {
        var user = await factory.SeedUserAsync(password: Password);
        var client = factory.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync("/api/Auth/login", new { email = user.Email, password = "not-the-password" });
        var unknownEmail = await client.PostAsJsonAsync("/api/Auth/login", new { email = $"nobody{TestHelpers.UniqueSuffix()}@test.local", password = "not-the-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(await wrongPassword.Content.ReadAsStringAsync(), await unknownEmail.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unconfirmed_user_with_correct_password_is_told_to_confirm_the_email()
    {
        var user = await factory.SeedUserAsync(password: Password, emailConfirmed: false);
        var response = await factory.CreateClient().PostAsJsonAsync("/api/Auth/login", new { email = user.Email, password = Password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("email_not_confirmed", (await response.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unknown_email_takes_comparable_time_to_a_wrong_password()
    {
        var user = await factory.SeedUserAsync(password: Password);
        var client = factory.CreateClient();
        async Task<double> MeasureAsync(string email)
        {
            var sw = Stopwatch.StartNew();
            await client.PostAsJsonAsync("/api/Auth/login", new { email, password = "not-the-password" });
            return sw.Elapsed.TotalMilliseconds;
        }

        // Warm up (JIT, lazy dummy hash).
        await MeasureAsync("warmup@test.local");
        await MeasureAsync(user.Email);

        double unknown = 0, known = 0;
        for (var i = 0; i < 3; i++)
        {
            unknown += await MeasureAsync($"nobody{i}@test.local");
            known += await MeasureAsync(user.Email);
        }

        // Before the fix unknown emails were ~100x faster (no BCrypt work). The bound is loose to stay stable on slow machines.
        Assert.True(unknown >= known * 0.3, $"Unknown email took {unknown / 3:0.0} ms on average vs {known / 3:0.0} ms for a wrong password.");
    }
}
