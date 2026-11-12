using GameNest_BackEnd.Data;
using GameNest_BackEnd.DTOs;
using GameNest_BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace GameNest_BackEnd.Tests;

public class SubmissionModerationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(User User, HttpClient Client)> UserAsync(string role = "User")
    {
        var user = await factory.SeedUserAsync(role);
        return (user, factory.CreateAuthenticatedClient(user.Id, role));
    }

    private static object ValidGame(string title) => new
    {
        type = "NewGame", title, description = "A cozy test game.", developer = "Dev Studio", publisher = "Pub House",
        releaseDate = "2025-05-01", coverUrl = "https://images.example/cover.png", trailerUrl = "https://video.example/watch?v=1"
    };

    private static async Task<int> SubmitAsync(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync("/api/Submissions", ValidGame(title));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("id").GetInt32();
    }

    private async Task<T> DbAsync<T>(Func<GameNestDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<GameNestDbContext>());
    }

    private static string Unique(string prefix) => prefix + " " + TestHelpers.UniqueSuffix();

    // ---------- submitting ----------

    [Fact]
    public async Task User_submits_a_game_and_server_sets_owner_status_and_dates()
    {
        var (user, client) = await UserAsync();
        var (other, _) = await UserAsync();
        var title = Unique("Submitted");

        var response = await client.PostAsJsonAsync("/api/Submissions", new
        {
            type = "NewGame", title = "  " + title + "  ", description = "  text  ", releaseDate = "2025-05-01",
            // fields a client must not be able to set
            submittedByUserId = other.Id, status = "Approved", reviewedByUserId = other.Id, adminComment = "forged", gameId = (int?)null
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal(user.Id, body.GetProperty("submittedByUserId").GetInt32());
        Assert.Equal(user.Username, body.GetProperty("submittedByUsername").GetString());
        Assert.Equal("Pending", body.GetProperty("status").GetString());
        Assert.Equal(title, body.GetProperty("title").GetString());
        Assert.Equal("text", body.GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("adminComment").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("reviewedByUserId").ValueKind);
        Assert.InRange(body.GetProperty("createdAt").GetDateTime(), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

        var stored = await DbAsync(db => db.GameSubmissions.AsNoTracking().SingleAsync(s => s.Title == title));
        Assert.Equal(new DateTime(2025, 5, 1), stored.ReleaseDate);
    }

    [Fact]
    public async Task Anonymous_and_invalid_tokens_cannot_submit_or_list()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync("/api/Submissions", ValidGame("x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/Submissions/mine")).StatusCode);
        var forged = factory.CreateClientWithToken(TestHelpers.CreateJwt(1, "Admin", key: "a-completely-different-signing-key-of-enough-length"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync("/api/Submissions?status=Pending")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.PutAsync("/api/Submissions/1/approve", null)).StatusCode);
    }

    public static TheoryData<string, object> InvalidSubmissions => new()
    {
        { "Title", new { type = "NewGame", title = "" } },
        { "Title", new { type = "NewGame", title = "   " } },
        { "Title", new { type = "NewGame", title = new string('t', 201) } },
        { "CoverUrl", new { type = "NewGame", title = "x", coverUrl = "javascript:alert(1)" } },
        { "CoverUrl", new { type = "NewGame", title = "x", coverUrl = "http://insecure.example/c.png" } },
        { "CoverUrl", new { type = "NewGame", title = "x", coverUrl = "https://user:pass@example.com/c.png" } },
        { "TrailerUrl", new { type = "NewGame", title = "x", trailerUrl = "not a url" } },
        { "Description", new { type = "NewGame", title = "x", description = new string('d', 5001) } },
        { "Developer", new { type = "NewGame", title = "x", developer = new string('d', 201) } },
        { "ReleaseDate", new { type = "NewGame", title = "x", releaseDate = "1800-01-01" } },
        { "ReleaseDate", new { type = "NewGame", title = "x", releaseDate = "2999-01-01" } },
        { "Type", new { type = "Hack", title = "x" } }
    };

    [Theory, MemberData(nameof(InvalidSubmissions))]
    public async Task Invalid_submissions_are_rejected_with_field_errors(string field, object payload)
    {
        var (_, client) = await UserAsync();
        var countBefore = await DbAsync(db => db.GameSubmissions.CountAsync());

        var response = await client.PostAsJsonAsync("/api/Submissions", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty(field, out _), $"expected an error for {field}");
        Assert.Equal(countBefore, await DbAsync(db => db.GameSubmissions.CountAsync()));
    }

    [Fact]
    public async Task Editing_a_pending_submission_is_validated_too()
    {
        var (_, client) = await UserAsync();
        var id = await SubmitAsync(client, Unique("Editable"));

        var response = await client.PutAsJsonAsync($"/api/Submissions/{id}", new { type = "NewGame", title = "ok", coverUrl = "javascript:alert(1)" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- reading ----------

    [Fact]
    public async Task Users_see_only_their_own_submissions_newest_first()
    {
        var (_, alice) = await UserAsync();
        var (_, bob) = await UserAsync();
        var a1 = await SubmitAsync(alice, Unique("Alice one"));
        var a2 = await SubmitAsync(alice, Unique("Alice two"));
        var b1 = await SubmitAsync(bob, Unique("Bob one"));

        var mine = (await (await alice.GetAsync("/api/Submissions/mine")).ReadJsonAsync()).EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToList();
        var listed = (await (await alice.GetAsync("/api/Submissions")).ReadJsonAsync()).EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToList();

        Assert.Equal([a2, a1], mine);
        Assert.Equal([a2, a1], listed);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/Submissions/{b1}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PutAsJsonAsync($"/api/Submissions/{b1}", ValidGame("hijack"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync($"/api/Submissions/{b1}")).StatusCode);
    }

    [Fact]
    public async Task Admin_lists_pending_submissions_of_everyone_with_usernames_and_mine_shows_only_own()
    {
        var (user, client) = await UserAsync();
        var (_, admin) = await UserAsync("Admin");
        var id = await SubmitAsync(client, Unique("Queue"));
        var ownAdmin = await SubmitAsync(admin, Unique("Admin own"));

        var pending = (await (await admin.GetAsync("/api/Submissions?status=pending")).ReadJsonAsync()).EnumerateArray().ToList();
        var adminMine = (await (await admin.GetAsync("/api/Submissions/mine")).ReadJsonAsync()).EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToList();

        var row = Assert.Single(pending, e => e.GetProperty("id").GetInt32() == id);
        Assert.Equal(user.Username, row.GetProperty("submittedByUsername").GetString());
        Assert.All(pending, e => Assert.Equal("Pending", e.GetProperty("status").GetString()));
        Assert.Equal([ownAdmin], adminMine);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/Submissions?status=Deleted")).StatusCode);
    }

    // ---------- moderation ----------

    [Fact]
    public async Task Approval_creates_one_public_game_links_it_and_the_user_sees_the_status()
    {
        var (_, client) = await UserAsync();
        var (admin, adminClient) = await UserAsync("Admin");
        var title = Unique("Approved game");
        var id = await SubmitAsync(client, title);

        var approve = await adminClient.PutAsync($"/api/Submissions/{id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        var body = await approve.ReadJsonAsync();
        Assert.Equal("Approved", body.GetProperty("status").GetString());
        Assert.Equal(admin.Id, body.GetProperty("reviewedByUserId").GetInt32());
        var gameId = body.GetProperty("gameId").GetInt32();

        var catalog = (await (await factory.CreateClient().GetAsync("/api/Games")).ReadJsonAsync()).EnumerateArray().ToList();
        var game = Assert.Single(catalog, g => g.GetProperty("title").GetString() == title);
        Assert.Equal(gameId, game.GetProperty("id").GetInt32());
        Assert.Equal("https://images.example/cover.png", game.GetProperty("coverUrl").GetString());
        Assert.NotEqual(JsonValueKind.Null, game.GetProperty("gameTitleId").ValueKind);

        var mine = Assert.Single((await (await client.GetAsync("/api/Submissions/mine")).ReadJsonAsync()).EnumerateArray(), e => e.GetProperty("id").GetInt32() == id);
        Assert.Equal("Approved", mine.GetProperty("status").GetString());

        // processed once only
        Assert.Equal(HttpStatusCode.Conflict, (await adminClient.PutAsync($"/api/Submissions/{id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await adminClient.PutAsJsonAsync($"/api/Submissions/{id}/reject", new { adminComment = "late" })).StatusCode);
        Assert.Equal(1, await DbAsync(db => db.Games.CountAsync(g => g.Title == title)));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/Submissions/{id}", ValidGame(title))).StatusCode);
    }

    [Fact]
    public async Task Approving_a_title_that_exists_reuses_its_game_title()
    {
        var (_, client) = await UserAsync();
        var (_, adminClient) = await UserAsync("Admin");
        var title = Unique("Shared title");
        var first = await SubmitAsync(client, title);
        var second = await SubmitAsync(client, title);

        var g1 = (await (await adminClient.PutAsync($"/api/Submissions/{first}/approve", null)).ReadJsonAsync()).GetProperty("gameId").GetInt32();
        var g2 = (await (await adminClient.PutAsync($"/api/Submissions/{second}/approve", null)).ReadJsonAsync()).GetProperty("gameId").GetInt32();

        var titles = await DbAsync(db => db.Games.Where(g => g.Id == g1 || g.Id == g2).Select(g => g.GameTitleId).Distinct().ToListAsync());
        Assert.Single(titles);
        Assert.Equal(1, await DbAsync(db => db.GameTitles.CountAsync(t => t.Title == title)));
    }

    [Fact]
    public async Task Rejection_with_reason_is_visible_to_the_submitter_and_creates_no_game()
    {
        var (_, client) = await UserAsync();
        var (_, adminClient) = await UserAsync("Admin");
        var title = Unique("Rejected game");
        var id = await SubmitAsync(client, title);

        var reject = await adminClient.PutAsJsonAsync($"/api/Submissions/{id}/reject", new { adminComment = "  Already in the catalog.  " });

        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        var mine = Assert.Single((await (await client.GetAsync("/api/Submissions/mine")).ReadJsonAsync()).EnumerateArray(), e => e.GetProperty("id").GetInt32() == id);
        Assert.Equal("Rejected", mine.GetProperty("status").GetString());
        Assert.Equal("Already in the catalog.", mine.GetProperty("adminComment").GetString());
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("gameId").ValueKind);
        Assert.Equal(0, await DbAsync(db => db.Games.CountAsync(g => g.Title == title)));
        Assert.Equal(HttpStatusCode.Conflict, (await adminClient.PutAsync($"/api/Submissions/{id}/approve", null)).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("{\"adminComment\":\"   \"}")]
    public async Task Rejection_reason_is_optional(string? json)
    {
        var (_, client) = await UserAsync();
        var (_, adminClient) = await UserAsync("Admin");
        var id = await SubmitAsync(client, Unique("No reason"));
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/Submissions/{id}/reject");
        if (json is not null) request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await adminClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await response.ReadJsonAsync()).GetProperty("adminComment").ValueKind);
    }

    [Fact]
    public async Task Rejection_reason_longer_than_the_limit_is_refused_and_nothing_changes()
    {
        var (_, client) = await UserAsync();
        var (_, adminClient) = await UserAsync("Admin");
        var id = await SubmitAsync(client, Unique("Long reason"));

        var response = await adminClient.PutAsJsonAsync($"/api/Submissions/{id}/reject", new { adminComment = new string('r', 2001) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Pending", await DbAsync(db => db.GameSubmissions.Where(s => s.Id == id).Select(s => s.Status).SingleAsync()));
    }

    [Fact]
    public async Task Unknown_submission_returns_404_for_moderation()
    {
        var (_, adminClient) = await UserAsync("Admin");
        Assert.Equal(HttpStatusCode.NotFound, (await adminClient.PutAsync("/api/Submissions/987654/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminClient.PutAsync("/api/Submissions/987654/reject", null)).StatusCode);
    }

    [Fact]
    public async Task Failed_correction_approval_is_rolled_back_and_stays_pending()
    {
        var (_, client) = await UserAsync();
        var (_, adminClient) = await UserAsync("Admin");
        var gameId = (await (await adminClient.PostAsJsonAsync("/api/Games", new { title = Unique("To archive") })).ReadJsonAsync()).GetProperty("id").GetInt32();
        var correction = await client.PostAsJsonAsync("/api/Submissions", new { type = "Correction", gameId, title = "Fixed title" });
        var id = (await correction.ReadJsonAsync()).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await adminClient.DeleteAsync($"/api/Games/{gameId}")).StatusCode);

        var approve = await adminClient.PutAsync($"/api/Submissions/{id}/approve", null);

        Assert.Equal(HttpStatusCode.BadRequest, approve.StatusCode);
        var stored = await DbAsync(db => db.GameSubmissions.AsNoTracking().SingleAsync(s => s.Id == id));
        Assert.Equal("Pending", stored.Status);
        Assert.Null(stored.ReviewedByUserId);
        Assert.Null(stored.ReviewedAt);
    }

    [Fact]
    public async Task Regular_users_cannot_moderate_or_list_the_queue_of_others()
    {
        var (_, client) = await UserAsync();
        var (_, other) = await UserAsync();
        var id = await SubmitAsync(other, Unique("Not yours"));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync($"/api/Submissions/{id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/Submissions/{id}/reject", new { adminComment = "no" })).StatusCode);
        var pending = (await (await client.GetAsync("/api/Submissions?status=Pending")).ReadJsonAsync()).EnumerateArray();
        Assert.DoesNotContain(pending, e => e.GetProperty("id").GetInt32() == id);
        Assert.Equal("Pending", await DbAsync(db => db.GameSubmissions.Where(s => s.Id == id).Select(s => s.Status).SingleAsync()));
    }

    // ---------- direct creation by an admin ----------

    [Fact]
    public async Task Admin_creates_a_game_directly_without_a_submission_and_it_is_public()
    {
        var (_, adminClient) = await UserAsync("Admin");
        var title = Unique("Direct game");
        var submissionsBefore = await DbAsync(db => db.GameSubmissions.CountAsync());

        var response = await adminClient.PostAsJsonAsync("/api/Games", new { title, description = "d", releaseDate = "2024-02-29", coverUrl = "https://images.example/d.png" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var catalog = (await (await factory.CreateClient().GetAsync("/api/Games")).ReadJsonAsync()).EnumerateArray();
        Assert.Contains(catalog, g => g.GetProperty("title").GetString() == title);
        Assert.Equal(submissionsBefore, await DbAsync(db => db.GameSubmissions.CountAsync()));
        Assert.Equal(new DateTime(2024, 2, 29), await DbAsync(db => db.Games.Where(g => g.Title == title).Select(g => g.ReleaseDate).SingleAsync()));
    }

    [Theory]
    [InlineData("CoverUrl", "{\"title\":\"x\",\"coverUrl\":\"javascript:alert(1)\"}")]
    [InlineData("TrailerUrl", "{\"title\":\"x\",\"trailerUrl\":\"http://insecure.example\"}")]
    [InlineData("Title", "{\"title\":\" \"}")]
    [InlineData("ReleaseDate", "{\"title\":\"x\",\"releaseDate\":\"1900-01-01\"}")]
    public async Task Direct_creation_is_validated(string field, string json)
    {
        var (_, adminClient) = await UserAsync("Admin");
        var response = await adminClient.PostAsync("/api/Games", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Direct_creation_tolerates_null_optional_fields()
    {
        var (_, adminClient) = await UserAsync("Admin");
        var response = await adminClient.PostAsJsonAsync("/api/Games", new { title = Unique("Nulls"), description = (string?)null, developer = (string?)null, coverUrl = (string?)null });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("", (await response.ReadJsonAsync()).GetProperty("description").GetString());
    }
}

public class GameFieldValidationTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("https://cdn.example/c.png", true)]
    [InlineData("http://cdn.example/c.png", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:image/png;base64,AAAA", false)]
    [InlineData("https://user:pw@cdn.example/c.png", false)]
    [InlineData("/relative/path.png", false)]
    public void Https_url_rule(string? value, bool valid) => Assert.Equal(valid, new HttpsUrlAttribute().IsValid(value));

    [Fact]
    public void Release_date_is_stored_as_a_utc_calendar_date()
    {
        var unspecified = new DateTime(2025, 5, 1, 13, 45, 0, DateTimeKind.Unspecified);
        var normalized = GameFields.ReleaseDate(unspecified)!.Value;
        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        Assert.Equal(new DateTime(2025, 5, 1), normalized);
        Assert.Null(GameFields.ReleaseDate(null));
    }

    [Fact]
    public void Dto_attributes_report_field_names()
    {
        var dto = new SubmissionDto { Title = "x", CoverUrl = "javascript:x", ReleaseDate = new DateTime(1800, 1, 1) };
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(SubmissionDto.CoverUrl)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(SubmissionDto.ReleaseDate)));
    }
}

/// <summary>
/// Concurrency of moderation against a real PostgreSQL database (opt-in, see <see cref="PostgresTestSettings"/>).
/// Ordered races: a row lock held by the test queues two real HTTP requests on the same submission in a known order,
/// then releases them, so both interleavings are exercised deterministically. Every test checks the database afterwards.
/// </summary>
public class SubmissionConcurrencyTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private sealed record SubmissionRow(string Status, int? GameId, string Title, string? AdminComment);

    private HttpClient Client(int userId, string role = "User")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestHelpers.CreateJwt(userId, role));
        return client;
    }

    private async Task<(HttpClient User, HttpClient Admin)> ClientsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameNestDbContext>();
        var s = TestHelpers.UniqueSuffix();
        var user = new User { Username = "pg_user_" + s, Email = $"pg_user_{s}@test.local", Role = "User", EmailConfirmed = true };
        var admin = new User { Username = "pg_admin_" + s, Email = $"pg_admin_{s}@test.local", Role = "Admin", EmailConfirmed = true };
        db.Users.AddRange(user, admin);
        await db.SaveChangesAsync();
        return (Client(user.Id), Client(admin.Id, "Admin"));
    }

    private static async Task<int> SubmitAsync(HttpClient user, string title)
    {
        var response = await user.PostAsJsonAsync("/api/Submissions", new { type = "NewGame", title, description = "original", releaseDate = "2025-05-01" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("id").GetInt32();
    }

    private async Task<SubmissionRow?> RowAsync(int id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<GameNestDbContext>().GameSubmissions.AsNoTracking()
            .Where(s => s.Id == id).Select(s => new SubmissionRow(s.Status, s.GameId, s.Title, s.AdminComment)).SingleOrDefaultAsync();
    }

    private async Task<List<Game>> GamesTitledAsync(params string[] titles)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<GameNestDbContext>().Games.AsNoTracking().Where(g => titles.Contains(g.Title)).ToListAsync();
    }

    /// <summary>Sends <paramref name="first"/> then <paramref name="second"/> while the row is locked; both run once it is released.</summary>
    private async Task<(HttpStatusCode First, HttpStatusCode Second)> OrderedRaceAsync(int submissionId, Func<Task<HttpResponseMessage>> first, Func<Task<HttpResponseMessage>> second)
    {
        Task<HttpResponseMessage> a, b;
        await using (await factory.LockSubmissionAsync(submissionId))
        {
            a = first();
            await factory.WaitForBlockedSessionsAsync(1);
            b = second();
            await factory.WaitForBlockedSessionsAsync(2);
        }
        return ((await a).StatusCode, (await b).StatusCode);
    }

    private static Func<Task<HttpResponseMessage>> Approve(HttpClient admin, int id) => () => admin.PutAsync($"/api/Submissions/{id}/approve", null);
    private static Func<Task<HttpResponseMessage>> Reject(HttpClient admin, int id) => () => admin.PutAsJsonAsync($"/api/Submissions/{id}/reject", new { adminComment = "race" });
    private static Func<Task<HttpResponseMessage>> Delete(HttpClient user, int id) => () => user.DeleteAsync($"/api/Submissions/{id}");
    private static Func<Task<HttpResponseMessage>> Edit(HttpClient user, int id, string title) =>
        () => user.PutAsJsonAsync($"/api/Submissions/{id}", new { type = "NewGame", title, description = "edited", releaseDate = "2024-01-01" });

    private static string Title(string label) => $"PG {label} {TestHelpers.UniqueSuffix()}";

    // ---------- approve vs delete ----------

    [PostgresFact]
    public async Task Approve_then_delete_keeps_the_approved_submission_and_its_single_game()
    {
        var (user, admin) = await ClientsAsync();
        var title = Title("approve-delete");
        var id = await SubmitAsync(user, title);

        var (approve, delete) = await OrderedRaceAsync(id, Approve(admin, id), Delete(user, id));

        Assert.Equal(HttpStatusCode.OK, approve);
        Assert.Equal(HttpStatusCode.Conflict, delete);
        var row = Assert.IsType<SubmissionRow>(await RowAsync(id));
        Assert.Equal("Approved", row.Status);
        var game = Assert.Single(await GamesTitledAsync(title));
        Assert.Equal(game.Id, row.GameId);
    }

    [PostgresFact]
    public async Task Delete_then_approve_creates_no_game()
    {
        var (user, admin) = await ClientsAsync();
        var title = Title("delete-approve");
        var id = await SubmitAsync(user, title);

        var (delete, approve) = await OrderedRaceAsync(id, Delete(user, id), Approve(admin, id));

        Assert.Equal(HttpStatusCode.NoContent, delete);
        Assert.Equal(HttpStatusCode.NotFound, approve);
        Assert.Null(await RowAsync(id));
        Assert.Empty(await GamesTitledAsync(title));
    }

    // ---------- edit vs delete ----------

    [PostgresFact]
    public async Task Edit_then_delete_returns_the_edited_submission_and_then_deletes_it()
    {
        var (user, _) = await ClientsAsync();
        var title = Title("edit-delete");
        var id = await SubmitAsync(user, title);

        HttpResponseMessage edit, delete;
        Task<HttpResponseMessage> editTask, deleteTask;
        await using (await factory.LockSubmissionAsync(id))
        {
            editTask = Edit(user, id, title + " edited")();
            await factory.WaitForBlockedSessionsAsync(1);
            deleteTask = Delete(user, id)();
            await factory.WaitForBlockedSessionsAsync(2);
        }
        (edit, delete) = (await editTask, await deleteTask);

        // The update keeps the row locked until its response has been read, so the delete cannot remove it in between.
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var body = await edit.ReadJsonAsync();
        Assert.Equal((id, title + " edited", "Pending"), (body.GetProperty("id").GetInt32(), body.GetProperty("title").GetString(), body.GetProperty("status").GetString()));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Null(await RowAsync(id));
        Assert.Empty(await GamesTitledAsync(title, title + " edited"));
    }

    // ---------- approve vs edit ----------

    [PostgresFact]
    public async Task Approve_then_edit_leaves_the_approved_data_unchanged()
    {
        var (user, admin) = await ClientsAsync();
        var title = Title("approve-edit");
        var id = await SubmitAsync(user, title);

        var (approve, edit) = await OrderedRaceAsync(id, Approve(admin, id), Edit(user, id, title + " edited"));

        Assert.Equal(HttpStatusCode.OK, approve);
        Assert.Equal(HttpStatusCode.Conflict, edit);
        var row = Assert.IsType<SubmissionRow>(await RowAsync(id));
        Assert.Equal(("Approved", title), (row.Status, row.Title));
        var game = Assert.Single(await GamesTitledAsync(title, title + " edited"));
        Assert.Equal((title, "original", row.GameId), (game.Title, game.Description, (int?)game.Id));
    }

    [PostgresFact]
    public async Task Edit_then_approve_publishes_the_edited_data()
    {
        var (user, admin) = await ClientsAsync();
        var title = Title("edit-approve");
        var id = await SubmitAsync(user, title);

        var (edit, approve) = await OrderedRaceAsync(id, Edit(user, id, title + " edited"), Approve(admin, id));

        Assert.Equal(HttpStatusCode.OK, edit);
        Assert.Equal(HttpStatusCode.OK, approve);
        var row = Assert.IsType<SubmissionRow>(await RowAsync(id));
        Assert.Equal(("Approved", title + " edited"), (row.Status, row.Title));
        var game = Assert.Single(await GamesTitledAsync(title, title + " edited"));
        Assert.Equal((title + " edited", "edited", new DateTime(2024, 1, 1), row.GameId), (game.Title, game.Description, game.ReleaseDate, (int?)game.Id));
    }

    // ---------- approve vs approve / reject ----------

    [PostgresFact]
    public async Task Second_approval_in_a_race_creates_no_second_game()
    {
        var (user, admin) = await ClientsAsync();
        var title = Title("approve-approve");
        var id = await SubmitAsync(user, title);

        var (first, second) = await OrderedRaceAsync(id, Approve(admin, id), Approve(admin, id));

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Conflict), (first, second));
        Assert.Single(await GamesTitledAsync(title));
        Assert.Equal("Approved", (await RowAsync(id))!.Status);
    }

    [PostgresFact]
    public async Task Approve_then_reject_keeps_the_approval()
    {
        var (user, admin) = await ClientsAsync();
        var title = Title("approve-reject");
        var id = await SubmitAsync(user, title);

        var (approve, reject) = await OrderedRaceAsync(id, Approve(admin, id), Reject(admin, id));

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Conflict), (approve, reject));
        var row = (await RowAsync(id))!;
        Assert.Equal(("Approved", (string?)null), (row.Status, row.AdminComment));
        Assert.Single(await GamesTitledAsync(title));
    }

    [PostgresFact]
    public async Task Reject_then_approve_creates_no_game()
    {
        var (user, admin) = await ClientsAsync();
        var title = Title("reject-approve");
        var id = await SubmitAsync(user, title);

        var (reject, approve) = await OrderedRaceAsync(id, Reject(admin, id), Approve(admin, id));

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Conflict), (reject, approve));
        var row = (await RowAsync(id))!;
        Assert.Equal(("Rejected", "race", (int?)null), (row.Status, row.AdminComment, row.GameId));
        Assert.Empty(await GamesTitledAsync(title));
    }

    // ---------- unordered stress: invariants under whatever interleaving PostgreSQL produces ----------

    [PostgresFact]
    public async Task Unordered_concurrent_moderation_never_leaves_inconsistent_data()
    {
        var (user, admin) = await ClientsAsync();
        for (var i = 0; i < 15; i++)
        {
            var title = Title("stress " + i);
            var id = await SubmitAsync(user, title);

            var responses = await Task.WhenAll(Approve(admin, id)(), Approve(admin, id)(), Reject(admin, id)(), Delete(user, id)(), Edit(user, id, title + " edited")());
            var codes = responses.Select(r => r.StatusCode).ToArray();
            var row = await RowAsync(id);
            var games = await GamesTitledAsync(title, title + " edited");

            Assert.DoesNotContain(HttpStatusCode.InternalServerError, codes);
            if (row is null)
            {
                // deleted first: nothing was approved or rejected, and no game exists
                Assert.Equal(HttpStatusCode.NoContent, codes[3]);
                Assert.Empty(games);
                Assert.DoesNotContain(HttpStatusCode.OK, codes[..3]);
            }
            else
            {
                Assert.NotEqual("Pending", row.Status); // one moderation action always wins against a pending row
                Assert.Equal(1, codes[..3].Count(c => c == HttpStatusCode.OK));
                Assert.Equal(HttpStatusCode.Conflict, codes[3]);
                if (row.Status == "Approved")
                {
                    var game = Assert.Single(games);
                    Assert.Equal(game.Id, row.GameId);
                    Assert.Equal(row.Title, game.Title);
                }
                else
                {
                    Assert.Equal("Rejected", row.Status);
                    Assert.Empty(games);
                    Assert.Null(row.GameId);
                }
            }
        }
    }
}
