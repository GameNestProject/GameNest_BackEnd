using GameNest_BackEnd.Data;
using GameNest_BackEnd.Models;
using GameNest_BackEnd.Services;
using Google.Apis.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace GameNest_BackEnd.Tests;

/// <summary>Automatic linking only where Google is authoritative for the email; explicit linking everywhere else.</summary>
public class GoogleAccountLinkingTests(GoogleApiFactory factory) : IClassFixture<GoogleApiFactory>
{
    private const string Password = "existing-password-1";

    private static string Sub() => "sub-" + Guid.NewGuid().ToString("N");
    private static string Unique(string domain) => $"user{TestHelpers.UniqueSuffix()}@{domain}";

    private async Task<(HttpStatusCode Status, JsonElement Body)> SignInAsync(GoogleIdentity identity)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/Auth/google", new { idToken = factory.Google.Issue(identity) });
        return (response.StatusCode, await response.ReadJsonAsync());
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> LinkAsync(string jwt, GoogleIdentity identity, string password = Password)
    {
        var response = await factory.CreateClientWithToken(jwt).PostAsJsonAsync("/api/Auth/google/link", new { idToken = factory.Google.Issue(identity), password });
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(string.IsNullOrEmpty(text) ? "{}" : text);
        return (response.StatusCode, doc.RootElement.Clone());
    }

    private async Task<User> LoadAsync(int id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<GameNestDbContext>().Users.Include(u => u.ExternalLogins).AsNoTracking().SingleAsync(u => u.Id == id);
    }

    private async Task<int> CountUsersAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<GameNestDbContext>().Users.CountAsync(u => u.Email.ToLower() == email.ToLower());
    }

    // ---------- automatic linking on sign-in ----------

    [Fact]
    public async Task Gmail_identity_links_automatically_without_touching_privileged_data()
    {
        var user = await factory.SeedUserAsync(role: "Admin", password: Password, email: Unique("gmail.com"));
        var identity = new GoogleIdentity(Sub(), user.Email, "Gmail User", null);

        var (status, body) = await SignInAsync(identity);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(user.Id, body.GetProperty("user").GetProperty("id").GetInt32());
        var stored = await LoadAsync(user.Id);
        Assert.Equal(identity.Subject, Assert.Single(stored.ExternalLogins).ProviderSubject);
        Assert.Equal("Admin", stored.Role);
        Assert.Equal(user.PasswordHash, stored.PasswordHash);
        Assert.Equal(user.Email, stored.Email);
        Assert.True(stored.EmailConfirmed);
    }

    [Fact]
    public async Task Workspace_identity_with_matching_hd_links_automatically()
    {
        var user = await factory.SeedUserAsync(email: Unique("studio.test"));

        var (status, body) = await SignInAsync(new GoogleIdentity(Sub(), user.Email, "Workspace User", null, "studio.test"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(user.Id, body.GetProperty("user").GetProperty("id").GetInt32());
        Assert.Single((await LoadAsync(user.Id)).ExternalLogins);
    }

    [Fact]
    public async Task Third_party_email_without_hd_is_not_linked_automatically()
    {
        var user = await factory.SeedUserAsync(email: Unique("outlook.test"));

        var (status, body) = await SignInAsync(new GoogleIdentity(Sub(), user.Email, "Outlook User", null));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("account_link_required", body.GetProperty("code").GetString());
        Assert.Empty((await LoadAsync(user.Id)).ExternalLogins);
        Assert.Equal(1, await CountUsersAsync(user.Email));
    }

    [Fact]
    public async Task Workspace_hd_that_does_not_match_the_email_domain_is_not_linked_automatically()
    {
        var user = await factory.SeedUserAsync(email: Unique("partner.test"));

        var (status, body) = await SignInAsync(new GoogleIdentity(Sub(), user.Email, "Guest", null, "studio.test"));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("account_link_required", body.GetProperty("code").GetString());
        Assert.Empty((await LoadAsync(user.Id)).ExternalLogins);
    }

    [Fact]
    public async Task Unconfirmed_account_is_not_linked_even_for_gmail()
    {
        var user = await factory.SeedUserAsync(emailConfirmed: false, email: Unique("gmail.com"));

        var (status, body) = await SignInAsync(new GoogleIdentity(Sub(), user.Email, "Squatted", null));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("email_registered_unconfirmed", body.GetProperty("code").GetString());
        var stored = await LoadAsync(user.Id);
        Assert.Empty(stored.ExternalLogins);
        Assert.False(stored.EmailConfirmed);
    }

    /// <summary>Inserts a row as it may exist in an old database (email not normalized), bypassing EF's normalization.</summary>
    private async Task<int> InsertLegacyUserAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameNestDbContext>();
        var username = "legacy" + TestHelpers.UniqueSuffix();
        await db.Database.ExecuteSqlRawAsync(
            """INSERT INTO "Users" ("Username","Email","PasswordHash","Role","EmailConfirmed","IsProfilePublic","IsLibraryPublic") VALUES ({0},{1},'x','User',1,1,1)""",
            username, email);
        return await db.Users.Where(u => u.Username == username).Select(u => u.Id).SingleAsync();
    }

    [Fact]
    public async Task Legacy_mixed_case_email_is_matched_instead_of_creating_a_duplicate()
    {
        var lower = Unique("gmail.com");
        var legacyId = await InsertLegacyUserAsync(char.ToUpperInvariant(lower[0]) + lower[1..]);

        var (status, body) = await SignInAsync(new GoogleIdentity(Sub(), lower, "Legacy", null));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(legacyId, body.GetProperty("user").GetProperty("id").GetInt32());
        Assert.Equal(1, await CountUsersAsync(lower));
        Assert.NotEqual(lower, (await LoadAsync(legacyId)).Email); // linking does not rewrite the stored email
    }

    [Fact]
    public async Task Case_variant_duplicates_are_never_linked_automatically()
    {
        // Possible only in a database where AddUsersEmailLowerUniqueIndex has not been applied yet.
        var lower = Unique("gmail.com");
        await InsertLegacyUserAsync(lower);
        await InsertLegacyUserAsync(lower.ToUpperInvariant());

        var (status, body) = await SignInAsync(new GoogleIdentity(Sub(), lower, "Dup", null));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("duplicate_accounts_for_email", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Linked_sub_is_never_overwritten_by_another_google_account()
    {
        var email = Unique("gmail.com");
        var first = new GoogleIdentity(Sub(), email, "First", null);
        var (_, created) = await SignInAsync(first);
        var userId = created.GetProperty("user").GetProperty("id").GetInt32();

        var (status, body) = await SignInAsync(new GoogleIdentity(Sub(), email, "Second", null));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("account_linked_to_another_google_account", body.GetProperty("code").GetString());
        Assert.Equal(first.Subject, Assert.Single((await LoadAsync(userId)).ExternalLogins).ProviderSubject);
    }

    [Fact]
    public async Task Already_linked_user_signs_in_again_by_sub_whatever_the_email_kind()
    {
        // A third-party user linked explicitly earlier keeps signing in, because the lookup is by sub.
        var user = await factory.SeedUserAsync(password: Password, email: Unique("company.test"));
        var identity = new GoogleIdentity(Sub(), user.Email, "Linked", null);
        var jwt = await factory.CreateClient().LoginAsync(user.Email, Password);
        Assert.Equal(HttpStatusCode.OK, (await LinkAsync(jwt, identity)).Status);

        var (status, body) = await SignInAsync(identity);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("isNewUser").GetBoolean());
        Assert.Equal(user.Id, body.GetProperty("user").GetProperty("id").GetInt32());
    }

    // ---------- email confirmation of new Google users ----------

    [Theory]
    [InlineData("gmail.com", null, true)]
    [InlineData("googlemail.com", null, true)]
    [InlineData("studio.test", "studio.test", true)]
    [InlineData("outlook.test", null, false)]
    [InlineData("partner.test", "studio.test", false)]
    public async Task New_users_are_confirmed_only_when_google_is_authoritative_but_can_always_sign_in(string domain, string? hd, bool confirmed)
    {
        var identity = new GoogleIdentity(Sub(), Unique(domain), "New Player", null, hd);

        var (status, body) = await SignInAsync(identity);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("isNewUser").GetBoolean());
        Assert.Equal("User", body.GetProperty("user").GetProperty("role").GetString());
        var stored = await LoadAsync(body.GetProperty("user").GetProperty("id").GetInt32());
        Assert.Equal(confirmed, stored.EmailConfirmed);
        Assert.Null(stored.PasswordHash);

        var again = await SignInAsync(identity);
        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.Equal(stored.Id, again.Body.GetProperty("user").GetProperty("id").GetInt32());
    }

    // ---------- explicit linking (recovery path) ----------

    [Fact]
    public async Task Third_party_account_owner_links_google_explicitly_with_jwt_and_password()
    {
        var user = await factory.SeedUserAsync(role: "Admin", password: Password, email: Unique("company.test"));
        var jwt = await factory.CreateClient().LoginAsync(user.Email, Password);
        var identity = new GoogleIdentity(Sub(), $"personal{TestHelpers.UniqueSuffix()}@gmail.com", "Owner", null); // emails may differ

        var (status, body) = await LinkAsync(jwt, identity);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("google_account_linked", body.GetProperty("code").GetString());
        var stored = await LoadAsync(user.Id);
        Assert.Equal(identity.Subject, Assert.Single(stored.ExternalLogins).ProviderSubject);
        Assert.Equal("Admin", stored.Role);
        Assert.Equal(user.Email, stored.Email);
        Assert.Equal(user.PasswordHash, stored.PasswordHash);

        Assert.Equal("google_account_already_linked", (await LinkAsync(jwt, identity)).Body.GetProperty("code").GetString());
        var (signInStatus, signIn) = await SignInAsync(identity);
        Assert.Equal(HttpStatusCode.OK, signInStatus);
        Assert.Equal(user.Id, signIn.GetProperty("user").GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Explicit_linking_requires_the_correct_password()
    {
        var user = await factory.SeedUserAsync(password: Password, email: Unique("company.test"));
        var jwt = await factory.CreateClient().LoginAsync(user.Email, Password);

        var (status, body) = await LinkAsync(jwt, new GoogleIdentity(Sub(), user.Email, "X", null), password: "wrong-password-1");

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("invalid_credentials", body.GetProperty("code").GetString());
        Assert.Empty((await LoadAsync(user.Id)).ExternalLogins);
    }

    [Fact]
    public async Task Explicit_linking_requires_a_gamenest_jwt()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/Auth/google/link",
            new { idToken = factory.Google.Issue(new GoogleIdentity(Sub(), Unique("gmail.com"), "X", null)), password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Google_account_linked_to_another_user_cannot_be_moved()
    {
        var victimIdentity = new GoogleIdentity(Sub(), Unique("gmail.com"), "Victim", null);
        var (_, victim) = await SignInAsync(victimIdentity);
        var attacker = await factory.SeedUserAsync(password: Password, email: Unique("company.test"));
        var jwt = await factory.CreateClient().LoginAsync(attacker.Email, Password);

        var (status, body) = await LinkAsync(jwt, victimIdentity);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("google_account_linked_to_another_user", body.GetProperty("code").GetString());
        Assert.Empty((await LoadAsync(attacker.Id)).ExternalLogins);
        var (_, again) = await SignInAsync(victimIdentity);
        Assert.Equal(victim.GetProperty("user").GetProperty("id").GetInt32(), again.GetProperty("user").GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task User_with_a_google_account_cannot_link_a_second_one()
    {
        var user = await factory.SeedUserAsync(password: Password, email: Unique("company.test"));
        var jwt = await factory.CreateClient().LoginAsync(user.Email, Password);
        var first = new GoogleIdentity(Sub(), user.Email, "First", null);
        Assert.Equal(HttpStatusCode.OK, (await LinkAsync(jwt, first)).Status);

        var (status, body) = await LinkAsync(jwt, new GoogleIdentity(Sub(), Unique("gmail.com"), "Second", null));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("account_linked_to_another_google_account", body.GetProperty("code").GetString());
        Assert.Equal(first.Subject, Assert.Single((await LoadAsync(user.Id)).ExternalLogins).ProviderSubject);
    }

    [Fact]
    public async Task Google_only_account_has_no_password_to_confirm_linking()
    {
        var (_, created) = await SignInAsync(new GoogleIdentity(Sub(), Unique("gmail.com"), "GoogleOnly", null));
        var jwt = created.GetProperty("token").GetString()!;

        var (status, body) = await LinkAsync(jwt, new GoogleIdentity(Sub(), Unique("gmail.com"), "Other", null), password: "anything-at-all");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("account_has_no_password", body.GetProperty("code").GetString());
    }
}

public class GoogleEmailClassificationTests
{
    [Theory]
    [InlineData("a@gmail.com", null, GoogleEmailKind.Gmail)]
    [InlineData("a@googlemail.com", null, GoogleEmailKind.Gmail)]
    [InlineData("a@studio.test", "studio.test", GoogleEmailKind.Workspace)]
    [InlineData("a@studio.test", "STUDIO.test", GoogleEmailKind.Workspace)]
    [InlineData("a@partner.test", "studio.test", GoogleEmailKind.Other)]
    [InlineData("a@gmail.com", "studio.test", GoogleEmailKind.Other)]
    [InlineData("a@outlook.test", null, GoogleEmailKind.Other)]
    [InlineData("a@gmail.com.evil.test", null, GoogleEmailKind.Other)]
    [InlineData("a@sub.gmail.com", null, GoogleEmailKind.Other)]
    public void Email_kind_depends_on_domain_and_hd(string email, string? hd, GoogleEmailKind expected) =>
        Assert.Equal(expected, GoogleIdentity.Classify(email, hd));

    [Fact]
    public async Task Hd_claim_is_taken_from_the_verified_token()
    {
        var payload = new GoogleJsonWebSignature.Payload
        {
            Issuer = "accounts.google.com", Audience = ApiFactory.GoogleClientId, Subject = "123",
            Email = "dev@studio.test", EmailVerified = true, HostedDomain = "Studio.Test",
            ExpirationTimeSeconds = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()
        };
        var validator = new GoogleTokenValidator(Options.Create(new GoogleAuthOptions { ClientId = ApiFactory.GoogleClientId }),
            new ListLogger<GoogleTokenValidator>(), (_, _) => Task.FromResult(payload));

        var identity = await validator.ValidateAsync("token");

        Assert.Equal("studio.test", identity.HostedDomain);
        Assert.Equal(GoogleEmailKind.Workspace, identity.EmailKind);
    }
}
