using GameNest_BackEnd.Services;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace GameNest_BackEnd.Tests;

public class CorsAndHealthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static HttpRequestMessage Preflight(string path, string origin, string method = "POST", string headers = "authorization,content-type")
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", headers);
        return request;
    }

    [Fact]
    public async Task Health_is_anonymous_and_returns_ok()
    {
        var response = await factory.CreateClient().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", (await response.ReadJsonAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Preflight_from_allowed_origin_is_accepted_without_token()
    {
        var response = await factory.CreateClient().SendAsync(Preflight("/api/Library", ApiFactory.AllowedOrigin, "GET"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(ApiFactory.AllowedOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        var allowedHeaders = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers")).ToLowerInvariant();
        Assert.Contains("authorization", allowedHeaders);
        Assert.Contains("content-type", allowedHeaders);
    }

    [Fact]
    public async Task Preflight_from_unknown_origin_gets_no_cors_headers()
    {
        var response = await factory.CreateClient().SendAsync(Preflight("/api/Auth/login", "https://evil.example"));

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Actual_requests_from_allowed_origin_carry_cors_headers_even_when_unauthorized()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/Library");
        request.Headers.Add("Origin", ApiFactory.AllowedOrigin);

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ApiFactory.AllowedOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task Preflight_does_not_consume_rate_limit_permits()
    {
        IAsyncLifetime limited = new RateLimitedApiFactory();
        await limited.InitializeAsync();
        try
        {
            var client = ((RateLimitedApiFactory)limited).CreateClient();

            for (var i = 0; i < RateLimitedApiFactory.AuthLimit * 3; i++)
                Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(Preflight("/api/Auth/login", ApiFactory.AllowedOrigin))).StatusCode);

            var login = await client.PostAsJsonAsync("/api/Auth/login", new { email = "nobody@test.local", password = "wrong-password" });
            Assert.NotEqual(HttpStatusCode.TooManyRequests, login.StatusCode);
        }
        finally
        {
            await limited.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("https://gamenest.pp.ua", new[] { "https://gamenest.pp.ua" })]
    [InlineData(" https://gamenest.pp.ua/ , http://localhost:3000 ", new[] { "https://gamenest.pp.ua", "http://localhost:3000" })]
    [InlineData("", new string[0])]
    public void ParseOrigins_accepts_comma_separated_origins(string value, string[] expected) =>
        Assert.Equal(expected, FrontendCors.ParseOrigins(value));

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.gamenest.pp.ua")]
    [InlineData("gamenest.pp.ua")]
    [InlineData("https://gamenest.pp.ua/app")]
    [InlineData("ftp://gamenest.pp.ua")]
    public void ParseOrigins_rejects_invalid_origins(string value) =>
        Assert.Throws<InvalidOperationException>(() => FrontendCors.ParseOrigins(value));
}
