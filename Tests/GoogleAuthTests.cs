using GameNest_BackEnd.Data;
using GameNest_BackEnd.Models;
using GameNest_BackEnd.Services;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Xunit;

namespace GameNest_BackEnd.Tests;

/// <summary>Maps fake ID token strings to verified identities or validation failures. Never talks to Google.</summary>
public sealed class FakeGoogleTokenValidator : IGoogleTokenValidator
{
    private readonly Dictionary<string, Func<GoogleIdentity>> _tokens = new();

    public string Issue(GoogleIdentity identity)
    {
        var token = "fake-google-id-token-" + Guid.NewGuid().ToString("N");
        _tokens[token] = () => identity;
        return token;
    }

    public string IssueFailure(Exception exception)
    {
        var token = "fake-google-id-token-" + Guid.NewGuid().ToString("N");
        _tokens[token] = () => throw exception;
        return token;
    }

    public Task<GoogleIdentity> ValidateAsync(string idToken, CancellationToken cancellationToken = default) =>
        _tokens.TryGetValue(idToken, out var result)
            ? Task.FromResult(result())
            : throw new GoogleTokenRejectedException(GoogleTokenRejectedException.InvalidToken, "Unknown test token.");
}

public class GoogleApiFactory : ApiFactory
{
    public FakeGoogleTokenValidator Google { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IGoogleTokenValidator>();
            services.AddSingleton<IGoogleTokenValidator>(Google);
        });
    }
}

/// <summary>The MVP configuration: email/password registration and login switched off.</summary>
public class GoogleOnlyApiFactory : GoogleApiFactory
{
    protected override IDictionary<string, string?> ExtraConfiguration => new Dictionary<string, string?>
    {
        ["RateLimiting:Auth:PermitLimit"] = "10000",
        ["RateLimiting:ResendConfirmation:PermitLimit"] = "10000",
        ["Auth:PasswordRegistrationEnabled"] = "false",
        ["Auth:PasswordLoginEnabled"] = "false"
    };
}

public class GoogleAuthEndpointTests(GoogleApiFactory factory) : IClassFixture<GoogleApiFactory>
{
    private static GoogleIdentity NewIdentity(string? name = "Test Player", string? email = null) =>
        new("google-sub-" + Guid.NewGuid().ToString("N"), email ?? $"g{TestHelpers.UniqueSuffix()}@gmail.test", name, "https://lh3.googleusercontent.test/a/photo");

    private async Task<(HttpResponseMessage Response, JsonElement Body)> PostGoogleAsync(object body)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/Auth/google", body);
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(string.IsNullOrEmpty(text) ? "{}" : text);
        return (response, doc.RootElement.Clone());
    }

    private async Task<T> WithDbAsync<T>(Func<GameNestDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<GameNestDbContext>());
    }

    [Fact]
    public async Task New_google_user_is_created_with_role_User_and_receives_a_valid_GameNest_jwt()
    {
        var identity = NewIdentity("Олена Гравець");

        var (response, body) = await PostGoogleAsync(new { idToken = factory.Google.Issue(identity) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("isNewUser").GetBoolean());
        var userJson = body.GetProperty("user");
        Assert.Equal("User", userJson.GetProperty("role").GetString());
        Assert.Equal(identity.Email, userJson.GetProperty("email").GetString());
        Assert.Equal("Олена_Гравець", userJson.GetProperty("username").GetString());
        Assert.Equal(identity.PictureUrl, userJson.GetProperty("avatarUrl").GetString());

        var user = await WithDbAsync(db => db.Users.Include(u => u.ExternalLogins).AsNoTracking().SingleAsync(u => u.Email == identity.Email));
        Assert.True(user.EmailConfirmed);
        Assert.Null(user.PasswordHash);
        var login = Assert.Single(user.ExternalLogins);
        Assert.Equal(ExternalLogin.GoogleProvider, login.Provider);
        Assert.Equal(identity.Subject, login.ProviderSubject);

        // Same claims as password login; the token is accepted by the API's own JWT validation.
        var token = body.GetProperty("token").GetString()!;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(user.Id.ToString(), jwt.Claims.Single(c => c.Type is ClaimTypes.NameIdentifier or "nameid").Value);
        Assert.Equal("User", jwt.Claims.Single(c => c.Type is ClaimTypes.Role or "role").Value);
        Assert.Equal(ApiFactory.JwtIssuer, jwt.Issuer);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClientWithToken(token).GetAsync("/api/Profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientWithToken(token).GetAsync("/api/Profile/admin")).StatusCode);
    }

    [Fact]
    public async Task Existing_google_user_signs_in_without_creating_duplicates()
    {
        var identity = NewIdentity();
        var (_, first) = await PostGoogleAsync(new { idToken = factory.Google.Issue(identity) });

        var (response, second) = await PostGoogleAsync(new { idToken = factory.Google.Issue(identity) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(second.GetProperty("isNewUser").GetBoolean());
        Assert.Equal(first.GetProperty("user").GetProperty("id").GetInt32(), second.GetProperty("user").GetProperty("id").GetInt32());
        Assert.Equal(1, await WithDbAsync(db => db.Users.CountAsync(u => u.Email == identity.Email)));
        Assert.Equal(1, await WithDbAsync(db => db.ExternalLogins.CountAsync(l => l.ProviderSubject == identity.Subject)));
    }

    [Fact]
    public async Task Google_identity_is_matched_by_sub_even_after_the_google_email_changes()
    {
        var original = NewIdentity();
        var (_, first) = await PostGoogleAsync(new { idToken = factory.Google.Issue(original) });
        var renamed = original with { Email = $"renamed{TestHelpers.UniqueSuffix()}@gmail.test" };

        var (response, second) = await PostGoogleAsync(new { idToken = factory.Google.Issue(renamed) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(first.GetProperty("user").GetProperty("id").GetInt32(), second.GetProperty("user").GetProperty("id").GetInt32());
        Assert.False(await WithDbAsync(db => db.Users.AnyAsync(u => u.Email == renamed.Email)));
    }

    [Fact]
    public async Task Confirmed_password_account_with_the_same_email_is_linked_and_keeps_its_role_and_password()
    {
        var admin = await factory.SeedUserAsync(role: "Admin", password: "existing-password-1");
        var identity = NewIdentity(email: admin.Email);

        var (response, body) = await PostGoogleAsync(new { idToken = factory.Google.Issue(identity) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(body.GetProperty("isNewUser").GetBoolean());
        Assert.Equal(admin.Id, body.GetProperty("user").GetProperty("id").GetInt32());
        Assert.Equal("Admin", body.GetProperty("user").GetProperty("role").GetString());
        var stored = await WithDbAsync(db => db.Users.Include(u => u.ExternalLogins).AsNoTracking().SingleAsync(u => u.Id == admin.Id));
        Assert.Equal(identity.Subject, Assert.Single(stored.ExternalLogins).ProviderSubject);
        Assert.Equal(admin.PasswordHash, stored.PasswordHash);
        Assert.False(string.IsNullOrEmpty(await factory.CreateClient().LoginAsync(admin.Email, "existing-password-1")));
    }

    [Fact]
    public async Task Unconfirmed_account_with_the_same_email_is_not_linked()
    {
        var unconfirmed = await factory.SeedUserAsync(emailConfirmed: false);

        var (response, body) = await PostGoogleAsync(new { idToken = factory.Google.Issue(NewIdentity(email: unconfirmed.Email)) });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("email_registered_unconfirmed", body.GetProperty("code").GetString());
        Assert.False(await WithDbAsync(db => db.ExternalLogins.AnyAsync(l => l.UserId == unconfirmed.Id)));
        Assert.Equal(1, await WithDbAsync(db => db.Users.CountAsync(u => u.Email == unconfirmed.Email)));
    }

    [Fact]
    public async Task Account_already_linked_to_another_google_identity_is_not_relinked()
    {
        var first = NewIdentity();
        await PostGoogleAsync(new { idToken = factory.Google.Issue(first) });
        var otherGoogleAccount = NewIdentity(email: first.Email); // different sub, same email

        var (response, body) = await PostGoogleAsync(new { idToken = factory.Google.Issue(otherGoogleAccount) });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("account_linked_to_another_google_account", body.GetProperty("code").GetString());
        Assert.False(await WithDbAsync(db => db.ExternalLogins.AnyAsync(l => l.ProviderSubject == otherGoogleAccount.Subject)));
    }

    [Fact]
    public async Task Fields_sent_next_to_the_id_token_are_ignored()
    {
        var identity = NewIdentity();

        var (response, body) = await PostGoogleAsync(new
        {
            idToken = factory.Google.Issue(identity), email = "attacker@evil.test", role = "Admin", username = "chosen", emailVerified = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(identity.Email, body.GetProperty("user").GetProperty("email").GetString());
        Assert.Equal("User", body.GetProperty("user").GetProperty("role").GetString());
        Assert.False(await WithDbAsync(db => db.Users.AnyAsync(u => u.Email == "attacker@evil.test")));
    }

    [Fact]
    public async Task Users_with_the_same_google_name_get_distinct_usernames()
    {
        var (_, a) = await PostGoogleAsync(new { idToken = factory.Google.Issue(NewIdentity("Same Name")) });
        var (_, b) = await PostGoogleAsync(new { idToken = factory.Google.Issue(NewIdentity("Same Name")) });

        Assert.NotEqual(a.GetProperty("user").GetProperty("username").GetString(), b.GetProperty("user").GetProperty("username").GetString());
        Assert.StartsWith("Same_Name", b.GetProperty("user").GetProperty("username").GetString());
    }

    [Theory]
    [InlineData(GoogleTokenRejectedException.InvalidToken, HttpStatusCode.Unauthorized)]
    [InlineData(GoogleTokenRejectedException.EmailNotVerified, HttpStatusCode.Unauthorized)]
    public async Task Rejected_tokens_return_401_with_a_code_and_create_nothing(string code, HttpStatusCode expected)
    {
        var usersBefore = await WithDbAsync(db => db.Users.CountAsync());

        var (response, body) = await PostGoogleAsync(new { idToken = factory.Google.IssueFailure(new GoogleTokenRejectedException(code, "test")) });

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal(usersBefore, await WithDbAsync(db => db.Users.CountAsync()));
    }

    [Fact]
    public async Task Unknown_token_is_rejected()
    {
        var (response, body) = await PostGoogleAsync(new { idToken = "eyJhbGciOiJSUzI1NiJ9.forged.signature" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_google_token", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Google_outage_returns_503_without_internal_details()
    {
        var (response, body) = await PostGoogleAsync(new
        {
            idToken = factory.Google.IssueFailure(new GoogleAuthUnavailableException(GoogleAuthUnavailableException.Unavailable, "certs down", new HttpRequestException("boom")))
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("google_auth_unavailable", body.GetProperty("code").GetString());
        Assert.DoesNotContain("boom", body.ToString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"idToken":""}""")]
    public async Task Missing_id_token_is_a_validation_error(string json)
    {
        var response = await factory.CreateClient().PostAsync("/api/Auth/google", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Endpoint_needs_no_authorization_header()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        var response = await client.PostAsJsonAsync("/api/Auth/google", new { idToken = factory.Google.Issue(NewIdentity()) });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public class GoogleOnlyModeTests(GoogleOnlyApiFactory factory) : IClassFixture<GoogleOnlyApiFactory>
{
    [Fact]
    public async Task Password_registration_and_login_are_disabled_but_google_works()
    {
        var client = factory.CreateClient();
        var seeded = await factory.SeedUserAsync(password: "seeded-password-1");

        var register = await client.PostAsJsonAsync("/api/Auth/register", new { username = "pw" + TestHelpers.UniqueSuffix(), email = $"pw{TestHelpers.UniqueSuffix()}@test.local", password = "some-password-123" });
        var login = await client.PostAsJsonAsync("/api/Auth/login", new { email = seeded.Email, password = "seeded-password-1" });
        var resend = await client.PostAsJsonAsync("/api/Auth/resend-confirmation", new { email = seeded.Email });

        Assert.Equal(HttpStatusCode.Forbidden, register.StatusCode);
        Assert.Equal("password_registration_disabled", (await register.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Equal("password_login_disabled", (await login.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, resend.StatusCode);
        Assert.Empty(factory.SentConfirmations);

        // The existing confirmed account is still reachable through Google with the same email.
        var google = await client.PostAsJsonAsync("/api/Auth/google", new
        {
            idToken = factory.Google.Issue(new GoogleIdentity("sub-" + Guid.NewGuid().ToString("N"), seeded.Email, "Seeded", null))
        });
        Assert.Equal(HttpStatusCode.OK, google.StatusCode);
        Assert.Equal(seeded.Id, (await google.ReadJsonAsync()).GetProperty("user").GetProperty("id").GetInt32());
    }
}

public class GoogleTokenValidatorTests
{
    private const string ClientId = ApiFactory.GoogleClientId;
    private readonly ListLogger<GoogleTokenValidator> _logger = new();

    private static GoogleJsonWebSignature.Payload ValidPayload() => new()
    {
        Issuer = "https://accounts.google.com",
        Audience = ClientId,
        Subject = "110169484474386276334",
        Email = "Player@Gmail.test",
        EmailVerified = true,
        Name = "Player One",
        Picture = "https://lh3.googleusercontent.test/a/photo",
        ExpirationTimeSeconds = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds(),
        IssuedAtTimeSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };

    private GoogleTokenValidator Create(GoogleTokenValidator.VerifyDelegate verify, string? clientId = ClientId) =>
        new(Options.Create(new GoogleAuthOptions { ClientId = clientId }), _logger, verify);

    [Fact]
    public async Task Valid_token_yields_the_verified_identity_and_requires_our_audience()
    {
        GoogleJsonWebSignature.ValidationSettings? used = null;
        var validator = Create((_, settings) => { used = settings; return Task.FromResult(ValidPayload()); });

        var identity = await validator.ValidateAsync("the-id-token");

        Assert.Equal(new[] { ClientId }, used!.Audience);
        Assert.Equal("110169484474386276334", identity.Subject);
        Assert.Equal("player@gmail.test", identity.Email);
        Assert.Equal("Player One", identity.Name);
    }

    [Theory]
    [InlineData("JWT has expired.")]
    [InlineData("JWT contains untrusted 'aud' claim.")]
    [InlineData("JWT invalid, unable to verify signature.")]
    [InlineData("JWT issuer incorrect. Must be one of: 'https://accounts.google.com', 'accounts.google.com'")]
    public async Task Library_rejections_become_invalid_google_token_and_the_token_is_not_logged(string reason)
    {
        const string idToken = "secret-id-token-value";
        var validator = Create((_, _) => throw new InvalidJwtException(reason));

        var ex = await Assert.ThrowsAsync<GoogleTokenRejectedException>(() => validator.ValidateAsync(idToken));

        Assert.Equal(GoogleTokenRejectedException.InvalidToken, ex.Code);
        Assert.All(_logger.Messages, m => Assert.DoesNotContain(idToken, m));
    }

    public static TheoryData<string, Action<GoogleJsonWebSignature.Payload>, string> BadPayloads => new()
    {
        { "wrong audience", p => p.Audience = "someone-else.apps.googleusercontent.com", GoogleTokenRejectedException.InvalidToken },
        { "wrong issuer", p => p.Issuer = "https://evil.example", GoogleTokenRejectedException.InvalidToken },
        { "expired", p => p.ExpirationTimeSeconds = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds(), GoogleTokenRejectedException.InvalidToken },
        { "no expiry", p => p.ExpirationTimeSeconds = null, GoogleTokenRejectedException.InvalidToken },
        { "missing sub", p => p.Subject = null, GoogleTokenRejectedException.InvalidToken },
        { "missing email", p => p.Email = null, GoogleTokenRejectedException.InvalidToken },
        { "invalid email", p => p.Email = "not an email", GoogleTokenRejectedException.InvalidToken },
        { "unverified email", p => p.EmailVerified = false, GoogleTokenRejectedException.EmailNotVerified }
    };

    [Theory]
    [MemberData(nameof(BadPayloads))]
    public async Task Payloads_without_the_required_claims_are_rejected(string _, Action<GoogleJsonWebSignature.Payload> spoil, string expectedCode)
    {
        var payload = ValidPayload();
        spoil(payload);
        var validator = Create((_, _) => Task.FromResult(payload));

        var ex = await Assert.ThrowsAsync<GoogleTokenRejectedException>(() => validator.ValidateAsync("token"));
        Assert.Equal(expectedCode, ex.Code);
    }

    [Fact]
    public async Task Audience_given_as_a_list_is_accepted_when_it_contains_our_client()
    {
        var payload = ValidPayload();
        payload.Audience = new[] { "other.apps.googleusercontent.com", ClientId };
        var identity = await Create((_, _) => Task.FromResult(payload)).ValidateAsync("token");
        Assert.Equal(payload.Subject, identity.Subject);
    }

    [Fact]
    public async Task Non_https_picture_is_dropped()
    {
        var payload = ValidPayload();
        payload.Picture = "http://insecure.test/p.png";
        Assert.Null((await Create((_, _) => Task.FromResult(payload)).ValidateAsync("token")).PictureUrl);
    }

    [Fact]
    public async Task Unreachable_google_certificates_mean_unavailable_not_invalid()
    {
        var validator = Create((_, _) => throw new HttpRequestException("network down"));
        var ex = await Assert.ThrowsAsync<GoogleAuthUnavailableException>(() => validator.ValidateAsync("token"));
        Assert.Equal(GoogleAuthUnavailableException.Unavailable, ex.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("client-secret-looking-value")]
    public async Task Missing_or_invalid_client_id_means_not_configured(string? clientId)
    {
        var called = false;
        var validator = Create((_, _) => { called = true; return Task.FromResult(ValidPayload()); }, clientId);

        var ex = await Assert.ThrowsAsync<GoogleAuthUnavailableException>(() => validator.ValidateAsync("token"));
        Assert.Equal(GoogleAuthUnavailableException.NotConfigured, ex.Code);
        Assert.False(called);
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    [InlineData("eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJzdWIiOiIxIiwiZW1haWwiOiJhQGIuYyIsImVtYWlsX3ZlcmlmaWVkIjp0cnVlfQ.")]
    public async Task Real_library_rejects_malformed_and_unsigned_tokens_offline(string token)
    {
        // Uses the real GoogleJsonWebSignature.ValidateAsync: these tokens fail before any certificate download.
        var validator = new GoogleTokenValidator(Options.Create(new GoogleAuthOptions { ClientId = ClientId }), _logger);

        var ex = await Assert.ThrowsAsync<GoogleTokenRejectedException>(() => validator.ValidateAsync(token));
        Assert.Equal(GoogleTokenRejectedException.InvalidToken, ex.Code);
    }

    [Theory]
    [InlineData("Олена Гравець", "Олена_Гравець")]
    [InlineData("  John  O'Brien-Smith ", "John_OBrien_Smith")]
    [InlineData("A", "player")]
    [InlineData(null, "player")]
    [InlineData("<script>", "script")]
    public void Usernames_are_derived_safely_from_the_google_name(string? name, string expected) =>
        Assert.Equal(expected, GoogleSignInService.BaseUsername(name));
}
