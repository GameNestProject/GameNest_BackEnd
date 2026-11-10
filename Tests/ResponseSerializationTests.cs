using GameNest_BackEnd.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace GameNest_BackEnd.Tests;

public class ResponseSerializationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] NavigationProperties = ["gameTitle", "userGames", "gameSubmissions", "user", "submittedByUser", "reviewedByUser"];

    private static void AssertNoNavigationProperties(JsonElement element)
    {
        foreach (var name in NavigationProperties)
            Assert.False(element.TryGetProperty(name, out _), $"Response must not expose navigation property '{name}'.");
    }

    private async Task<HttpClient> AdminClientAsync() => factory.CreateAuthenticatedClient((await factory.SeedUserAsync("Admin")).Id, "Admin");

    private async Task<int> CountGamesAsync(string title)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<GameNestDbContext>().Games.CountAsync(g => g.Title == title);
    }

    [Fact]
    public async Task Admin_can_create_a_game_and_receives_a_serializable_response()
    {
        var admin = await AdminClientAsync();
        var title = "Created " + TestHelpers.UniqueSuffix();

        var response = await admin.PostAsJsonAsync("/api/Games", new { title, description = "d", developer = "dev", publisher = "pub" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal(title, body.GetProperty("title").GetString());
        Assert.Equal("dev", body.GetProperty("developer").GetString());
        Assert.True(body.GetProperty("gameTitleId").GetInt32() > 0);
        AssertNoNavigationProperties(body);
        Assert.Equal(1, await CountGamesAsync(title));
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync(response.Headers.Location)).StatusCode);
    }

    [Fact]
    public async Task Admin_can_update_a_game_and_the_change_is_saved()
    {
        var admin = await AdminClientAsync();
        var title = "Original " + TestHelpers.UniqueSuffix();
        var id = (await (await admin.PostAsJsonAsync("/api/Games", new { title })).ReadJsonAsync()).GetProperty("id").GetInt32();

        var response = await admin.PutAsJsonAsync($"/api/Games/{id}", new { title, description = "updated", publisher = "new pub" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal("updated", body.GetProperty("description").GetString());
        AssertNoNavigationProperties(body);
        using var scope = factory.Services.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<GameNestDbContext>().Games.AsNoTracking().SingleAsync(g => g.Id == id);
        Assert.Equal("new pub", saved.Publisher);
        Assert.Equal(1, await CountGamesAsync(title));
    }

    [Fact]
    public async Task Catalog_endpoints_return_games_without_navigation_properties()
    {
        var admin = await AdminClientAsync();
        var created = await (await admin.PostAsJsonAsync("/api/Games", new { title = "Catalog " + TestHelpers.UniqueSuffix() })).ReadJsonAsync();
        var client = factory.CreateClient();

        var list = await (await client.GetAsync("/api/Games")).ReadJsonAsync();
        Assert.All(list.EnumerateArray(), AssertNoNavigationProperties);
        AssertNoNavigationProperties(await (await client.GetAsync($"/api/Games/{created.GetProperty("id").GetInt32()}")).ReadJsonAsync());
        var versions = await client.GetAsync($"/api/GameTitles/{created.GetProperty("gameTitleId").GetInt32()}/versions");
        Assert.Equal(HttpStatusCode.OK, versions.StatusCode);
        Assert.All((await versions.ReadJsonAsync()).EnumerateArray(), AssertNoNavigationProperties);
    }

    [Fact]
    public async Task Library_add_get_and_update_serialize_with_game_details()
    {
        var admin = await AdminClientAsync();
        var title = "Library " + TestHelpers.UniqueSuffix();
        var gameId = (await (await admin.PostAsJsonAsync("/api/Games", new { title })).ReadJsonAsync()).GetProperty("id").GetInt32();
        var user = factory.CreateAuthenticatedClient((await factory.SeedUserAsync()).Id);

        var added = await user.PostAsJsonAsync($"/api/Library/{gameId}", new { status = "Playing", purchased = true });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var addedBody = await added.ReadJsonAsync();
        Assert.Equal(title, addedBody.GetProperty("game").GetProperty("title").GetString());
        AssertNoNavigationProperties(addedBody);

        var list = await user.GetAsync("/api/Library");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var item = Assert.Single((await list.ReadJsonAsync()).EnumerateArray());
        Assert.Equal(gameId, item.GetProperty("gameId").GetInt32());
        Assert.Equal("Playing", item.GetProperty("status").GetString());
        Assert.True(item.GetProperty("purchased").GetBoolean());
        AssertNoNavigationProperties(item);
        AssertNoNavigationProperties(item.GetProperty("game"));

        var updated = await user.PutAsJsonAsync($"/api/Library/{gameId}", new { status = "Completed", purchased = true });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Completed", (await updated.ReadJsonAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Approving_a_correction_updates_the_game_and_serializes_the_submission()
    {
        var admin = await AdminClientAsync();
        var title = "Correctable " + TestHelpers.UniqueSuffix();
        var gameId = (await (await admin.PostAsJsonAsync("/api/Games", new { title })).ReadJsonAsync()).GetProperty("id").GetInt32();
        var user = factory.CreateAuthenticatedClient((await factory.SeedUserAsync()).Id);
        var submissionId = (await (await user.PostAsJsonAsync("/api/Submissions", new { type = "Correction", gameId, title, developer = "fixed dev" })).ReadJsonAsync()).GetProperty("id").GetInt32();

        var response = await admin.PutAsync($"/api/Submissions/{submissionId}/approve", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal("Approved", body.GetProperty("status").GetString());
        AssertNoNavigationProperties(body);
        using var scope = factory.Services.CreateScope();
        Assert.Equal("fixed dev", (await scope.ServiceProvider.GetRequiredService<GameNestDbContext>().Games.AsNoTracking().SingleAsync(g => g.Id == gameId)).Developer);
    }

    [Fact]
    public async Task User_created_games_serialize_without_navigation_properties()
    {
        var user = factory.CreateAuthenticatedClient((await factory.SeedUserAsync()).Id);
        var created = await user.PostAsJsonAsync("/api/UserGames", new { title = "Mine" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        AssertNoNavigationProperties(await created.ReadJsonAsync());
        Assert.All((await (await user.GetAsync("/api/UserGames")).ReadJsonAsync()).EnumerateArray(), AssertNoNavigationProperties);
    }
}
