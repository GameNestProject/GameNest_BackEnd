using GameNest_BackEnd.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Xunit;

namespace GameNest_BackEnd.Tests;

public class JwtValidationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<HttpResponseMessage> GetProfileAsync(string token) => await factory.CreateClientWithToken(token).GetAsync("/api/Profile");

    [Fact]
    public async Task Expired_token_is_rejected_without_detailed_error_description()
    {
        var user = await factory.SeedUserAsync();
        var response = await GetProfileAsync(TestHelpers.CreateJwt(user.Id, expires: DateTime.UtcNow.AddSeconds(-5), notBefore: DateTime.UtcNow.AddMinutes(-10)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("error_description", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_rejected()
    {
        var user = await factory.SeedUserAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetProfileAsync(TestHelpers.CreateJwt(user.Id, key: "another-signing-key-that-is-long-enough-1234"))).StatusCode);
    }

    [Fact]
    public async Task Token_with_wrong_issuer_or_audience_is_rejected()
    {
        var user = await factory.SeedUserAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetProfileAsync(TestHelpers.CreateJwt(user.Id, issuer: "someone-else"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetProfileAsync(TestHelpers.CreateJwt(user.Id, audience: "someone-else"))).StatusCode);
    }

    [Fact]
    public async Task Unsigned_alg_none_token_is_rejected()
    {
        var user = await factory.SeedUserAsync("Admin");
        var payload = TestHelpers.CreateJwt(user.Id, "Admin").Split('.')[1];
        var header = Base64UrlEncoder.Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}");

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClientWithToken($"{header}.{payload}.").DeleteAsync("/api/Games/1")).StatusCode);
    }

    [Fact]
    public async Task Validly_signed_token_without_a_user_id_is_rejected_with_401()
    {
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(ApiFactory.JwtIssuer, ApiFactory.JwtAudience,
            [new Claim(ClaimTypes.Role, "User")], expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.JwtKey)), SecurityAlgorithms.HmacSha256)));

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClientWithToken(token).GetAsync("/api/UserGames")).StatusCode);
    }

    [Fact]
    public async Task Role_cannot_be_chosen_at_registration()
    {
        var s = TestHelpers.UniqueSuffix();
        var email = $"role{s}@test.local";
        await factory.CreateClient().PostAsJsonAsync("/api/Auth/register", new { username = "role" + s, email, password = "role-test-password", role = "Admin", emailConfirmed = true });

        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<GameNestDbContext>().Users.AsNoTracking().SingleAsync(u => u.Email == email);
        Assert.Equal("User", user.Role);
        Assert.False(user.EmailConfirmed);
    }
}

public class AdminAuthorizationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    public static TheoryData<string, string> AdminEndpoints => new()
    {
        { "POST", "/api/Games" },
        { "PUT", "/api/Games/1" },
        { "DELETE", "/api/Games/1" },
        { "PUT", "/api/Submissions/1/approve" },
        { "PUT", "/api/Submissions/1/reject" },
        { "GET", "/api/Profile/admin" }
    };

    private static HttpRequestMessage Request(string method, string url) => new(new HttpMethod(method), url)
    {
        Content = method is "GET" or "DELETE" ? null : JsonContent.Create(new { title = "x", adminComment = "x" })
    };

    [Theory, MemberData(nameof(AdminEndpoints))]
    public async Task Regular_user_gets_403_on_admin_endpoints(string method, string url)
    {
        var user = await factory.SeedUserAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateAuthenticatedClient(user.Id).SendAsync(Request(method, url))).StatusCode);
    }

    [Theory, MemberData(nameof(AdminEndpoints))]
    public async Task Anonymous_request_gets_401_on_admin_endpoints(string method, string url) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().SendAsync(Request(method, url))).StatusCode);

    [Fact]
    public async Task Admin_can_edit_a_pending_submission_but_not_a_reviewed_one()
    {
        var user = factory.CreateAuthenticatedClient((await factory.SeedUserAsync()).Id);
        var admin = factory.CreateAuthenticatedClient((await factory.SeedUserAsync("Admin")).Id, "Admin");
        var id = (await (await user.PostAsJsonAsync("/api/Submissions", new { type = "NewGame", title = "Typo Titel" })).ReadJsonAsync()).GetProperty("id").GetInt32();

        var edited = await admin.PutAsJsonAsync($"/api/Submissions/{id}", new { type = "NewGame", title = "Typo Title" });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal("Typo Title", (await edited.ReadJsonAsync()).GetProperty("title").GetString());

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync($"/api/Submissions/{id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/Submissions/{id}", new { type = "NewGame", title = "Too late" })).StatusCode);
    }
}

public class OwnershipTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task User_cannot_read_change_or_delete_another_users_created_game()
    {
        var owner = factory.CreateAuthenticatedClient((await factory.SeedUserAsync()).Id);
        var other = factory.CreateAuthenticatedClient((await factory.SeedUserAsync()).Id);
        var id = (await (await owner.PostAsJsonAsync("/api/UserGames", new { title = "Private" })).ReadJsonAsync()).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/UserGames/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/UserGames/{id}", new { title = "Hijacked" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/UserGames/{id}")).StatusCode);
        Assert.Empty((await (await other.GetAsync("/api/UserGames")).ReadJsonAsync()).EnumerateArray());
        Assert.Equal("Private", (await (await owner.GetAsync($"/api/UserGames/{id}")).ReadJsonAsync()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task User_cannot_read_change_or_delete_another_users_submission()
    {
        var owner = factory.CreateAuthenticatedClient((await factory.SeedUserAsync()).Id);
        var other = factory.CreateAuthenticatedClient((await factory.SeedUserAsync()).Id);
        var id = (await (await owner.PostAsJsonAsync("/api/Submissions", new { type = "NewGame", title = "Mine" })).ReadJsonAsync()).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/Submissions/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/Submissions/{id}", new { type = "NewGame", title = "Hijacked" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/Submissions/{id}")).StatusCode);
        Assert.DoesNotContain((await (await other.GetAsync("/api/Submissions")).ReadJsonAsync()).EnumerateArray(), s => s.GetProperty("id").GetInt32() == id);
    }

    [Fact]
    public async Task Admin_cannot_delete_another_users_submission()
    {
        var owner = factory.CreateAuthenticatedClient((await factory.SeedUserAsync()).Id);
        var admin = factory.CreateAuthenticatedClient((await factory.SeedUserAsync("Admin")).Id, "Admin");
        var id = (await (await owner.PostAsJsonAsync("/api/Submissions", new { type = "NewGame", title = "Keep" })).ReadJsonAsync()).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/Submissions/{id}")).StatusCode);
    }
}

/// <summary>
/// The role is read from the database on every request, not trusted from the token: a token keeps the role of the
/// moment of sign-in, while /api/Profile (and the frontend) already shows the current one.
/// </summary>
public class CurrentRoleFromDatabaseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task SetRoleAsync(int userId, string role)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameNestDbContext>();
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(u => u.SetProperty(x => x.Role, role));
    }

    [Fact]
    public async Task User_promoted_after_sign_in_sees_everyones_submissions_with_the_old_token()
    {
        var submitter = factory.CreateAuthenticatedClient((await factory.SeedUserAsync()).Id);
        var id = (await (await submitter.PostAsJsonAsync("/api/Submissions", new { type = "NewGame", title = "Queued " + TestHelpers.UniqueSuffix() })).ReadJsonAsync()).GetProperty("id").GetInt32();
        var promoted = await factory.SeedUserAsync();
        var client = factory.CreateAuthenticatedClient(promoted.Id, "User"); // token issued before the promotion
        await SetRoleAsync(promoted.Id, "Admin");

        var pending = await client.GetAsync("/api/Submissions?status=Pending");
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        Assert.Contains((await pending.ReadJsonAsync()).EnumerateArray(), s => s.GetProperty("id").GetInt32() == id);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/Profile/admin")).StatusCode);
    }

    [Fact]
    public async Task Admin_demoted_after_sign_in_loses_admin_rights_with_the_old_token()
    {
        var submitter = factory.CreateAuthenticatedClient((await factory.SeedUserAsync()).Id);
        var id = (await (await submitter.PostAsJsonAsync("/api/Submissions", new { type = "NewGame", title = "Other " + TestHelpers.UniqueSuffix() })).ReadJsonAsync()).GetProperty("id").GetInt32();
        var demoted = await factory.SeedUserAsync("Admin");
        var client = factory.CreateAuthenticatedClient(demoted.Id, "Admin"); // token issued before the demotion
        await SetRoleAsync(demoted.Id, "User");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/Profile/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync($"/api/Submissions/{id}/approve", null)).StatusCode);
        Assert.DoesNotContain((await (await client.GetAsync("/api/Submissions")).ReadJsonAsync()).EnumerateArray(), s => s.GetProperty("id").GetInt32() == id);
    }

    [Fact]
    public async Task Token_of_a_deleted_user_is_rejected_with_401()
    {
        var user = await factory.SeedUserAsync("Admin");
        var client = factory.CreateAuthenticatedClient(user.Id, "Admin");
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<GameNestDbContext>().Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/Submissions")).StatusCode);
    }
}
