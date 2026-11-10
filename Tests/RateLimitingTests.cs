using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace GameNest_BackEnd.Tests;

/// <summary>An API host with deliberately small authentication rate limits.</summary>
public class RateLimitedApiFactory : ApiFactory
{
    public const int AuthLimit = 3;
    public const int ResendLimit = 2;

    protected override IDictionary<string, string?> ExtraConfiguration => new Dictionary<string, string?>
    {
        ["RateLimiting:Auth:PermitLimit"] = AuthLimit.ToString(),
        ["RateLimiting:Auth:WindowSeconds"] = "300",
        ["RateLimiting:ResendConfirmation:PermitLimit"] = ResendLimit.ToString(),
        ["RateLimiting:ResendConfirmation:WindowSeconds"] = "300"
    };
}

// Each test uses a different endpoint, because limits are partitioned by client IP and endpoint path.
public class RateLimitingTests(RateLimitedApiFactory factory) : IClassFixture<RateLimitedApiFactory>
{
    private async Task AssertLimitedAfterAsync(int limit, Func<HttpClient, int, Task<HttpResponseMessage>> send)
    {
        var client = factory.CreateClient();
        for (var i = 0; i < limit; i++)
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await send(client, i)).StatusCode);

        var limited = await send(client, limit);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("too_many_requests", (await limited.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero, "429 responses must include Retry-After.");
    }

    [Fact]
    public Task Login_is_rate_limited() =>
        AssertLimitedAfterAsync(RateLimitedApiFactory.AuthLimit, (c, i) =>
            c.PostAsJsonAsync("/api/Auth/login", new { email = "nobody@test.local", password = "wrong-password-" + i }));

    [Fact]
    public Task Register_is_rate_limited() =>
        AssertLimitedAfterAsync(RateLimitedApiFactory.AuthLimit, (c, i) =>
        {
            var s = TestHelpers.UniqueSuffix();
            return c.PostAsJsonAsync("/api/Auth/register", new { username = "rl" + s, email = $"rl{s}@test.local", password = "rate-limit-password" });
        });

    [Fact]
    public Task Confirm_email_is_rate_limited() =>
        AssertLimitedAfterAsync(RateLimitedApiFactory.AuthLimit, (c, i) =>
            c.PostAsJsonAsync("/api/Auth/confirm-email", new { email = "nobody@test.local", token = "guess-" + i }));

    [Fact]
    public Task Resend_confirmation_has_a_stricter_limit() =>
        AssertLimitedAfterAsync(RateLimitedApiFactory.ResendLimit, (c, _) =>
            c.PostAsJsonAsync("/api/Auth/resend-confirmation", new { email = "nobody@test.local" }));
}

// Separate fixture, so its login budget is not shared with RateLimitingTests.
public class RateLimitPartitionTests(RateLimitedApiFactory factory) : IClassFixture<RateLimitedApiFactory>
{
    [Fact]
    public async Task Path_casing_does_not_bypass_the_limit_and_other_endpoints_are_not_affected()
    {
        var client = factory.CreateClient();
        // Same endpoint with different casing shares one partition.
        var paths = new[] { "/api/auth/login", "/API/AUTH/LOGIN", "/api/Auth/Login", "/api/AUTH/login" };
        var codes = new List<HttpStatusCode>();
        foreach (var path in paths)
            codes.Add((await client.PostAsJsonAsync(path, new { email = "case@test.local", password = "wrong-password" })).StatusCode);

        Assert.All(codes.Take(RateLimitedApiFactory.AuthLimit), c => Assert.Equal(HttpStatusCode.Unauthorized, c));
        Assert.Equal(HttpStatusCode.TooManyRequests, codes[^1]);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/Games")).StatusCode);
    }
}
