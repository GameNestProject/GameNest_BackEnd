using GameNest_BackEnd.DTOs.Auth;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace GameNest_BackEnd.Tests;

public class DtoValidationTests
{
    private static List<ValidationResult> Validate(object dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        return results;
    }

    private static bool PasswordAccepted(string password) =>
        Validate(new RegisterDto { Username = "valid-name", Email = "user@test.local", Password = password })
            .All(r => !r.MemberNames.Contains(nameof(RegisterDto.Password)));

    [Fact]
    public void RegisterDto_rejects_an_invalid_email_and_short_password()
    {
        var results = Validate(new RegisterDto { Username = "valid-name", Email = "not-an-email", Password = "short" });
        Assert.True(results.Count >= 2);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(72)]
    public void Ascii_passwords_up_to_72_bytes_are_accepted(int length) =>
        Assert.True(PasswordAccepted(new string('x', length - 1) + "1"));

    [Fact]
    public void Ascii_password_longer_than_72_bytes_is_rejected() =>
        Assert.False(PasswordAccepted(new string('x', 73)));

    [Fact]
    public void Password_shorter_than_8_characters_is_rejected() =>
        Assert.False(PasswordAccepted("abc1234"));

    [Fact]
    public void Multibyte_password_within_72_bytes_is_accepted()
    {
        var password = new string('ї', 36); // 2 bytes per character = 72 bytes
        Assert.True(PasswordAccepted(password));
    }

    [Fact]
    public void Multibyte_password_over_72_bytes_is_rejected_even_when_it_has_few_characters()
    {
        var cyrillic = new string('ї', 37); // 37 characters, 74 bytes
        var emoji = string.Concat(Enumerable.Repeat("🎮", 19)); // 38 UTF-16 chars, 76 bytes
        Assert.False(PasswordAccepted(cyrillic));
        Assert.False(PasswordAccepted(emoji));
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("password")]
    [InlineData("PASSWORD123")]
    [InlineData("qwerty123")]
    [InlineData(" gamenest ")]
    public void Common_passwords_are_rejected(string password) => Assert.False(PasswordAccepted(password));
}

public class RegistrationPasswordPolicyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("12345678")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 73 bytes
    public async Task Register_rejects_passwords_that_violate_the_policy(string password)
    {
        var s = TestHelpers.UniqueSuffix();
        var response = await factory.CreateClient().PostAsJsonAsync("/api/Auth/register", new { username = "pw" + s, email = $"pw{s}@test.local", password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty("Password", out _));
    }

    [Fact]
    public async Task Existing_users_can_still_log_in_with_longer_passwords()
    {
        // Login keeps accepting up to 128 characters, so accounts created before the policy change keep working.
        var longPassword = new string('k', 100);
        var user = await factory.SeedUserAsync(password: longPassword);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().PostAsJsonAsync("/api/Auth/login", new { email = user.Email, password = longPassword })).StatusCode);
    }
}
