using GameNest_BackEnd.Data;
using GameNest_BackEnd.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace GameNest_BackEnd.Tests;

public static class TestHelpers
{
    public static string CreateJwt(int userId, string role = "User", string key = ApiFactory.JwtKey,
        string issuer = ApiFactory.JwtIssuer, string audience = ApiFactory.JwtAudience, DateTime? expires = null, DateTime? notBefore = null) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(issuer, audience,
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Name, "test"), new Claim(ClaimTypes.Role, role)],
            notBefore: notBefore, expires: expires ?? DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)));

    public static HttpClient CreateClientWithToken(this ApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(JwtBearerDefaults.AuthenticationScheme, token);
        return client;
    }

    public static HttpClient CreateAuthenticatedClient(this ApiFactory factory, int userId, string role = "User") =>
        factory.CreateClientWithToken(CreateJwt(userId, role));

    public static string UniqueSuffix() => Guid.NewGuid().ToString("N")[..8];

    public static async Task<User> SeedUserAsync(this ApiFactory factory, string role = "User", string password = "seeded-password-1", bool emailConfirmed = true)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameNestDbContext>();
        var s = UniqueSuffix();
        var user = new User
        {
            Username = role.ToLowerInvariant() + s, Email = $"{role.ToLowerInvariant()}{s}@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password), Role = role, EmailConfirmed = emailConfirmed
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public static async Task<string> LoginAsync(this HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/Auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("token").GetString()!;
    }

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }
}
