using GameNest_BackEnd.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GameNest_BackEnd.Tests;

public class JwtLifetimeTests
{
    private static IConfiguration Config(string? days) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [JwtLifetime.SettingName] = days }).Build();

    [Theory]
    [InlineData(null, JwtLifetime.DefaultDays)]
    [InlineData("", JwtLifetime.DefaultDays)]
    [InlineData("1", 1)]
    [InlineData("90", 90)]
    [InlineData("365", 365)]
    public void Valid_or_missing_setting_gives_the_lifetime(string? setting, int days) =>
        Assert.Equal(TimeSpan.FromDays(days), JwtLifetime.From(Config(setting)));

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("366")]
    [InlineData("1.5")]
    [InlineData("forever")]
    public void Invalid_setting_is_rejected(string setting) =>
        Assert.Throws<InvalidOperationException>(() => JwtLifetime.From(Config(setting)));
}
