namespace GameNest_BackEnd.Services;

/// <summary>
/// How long a GameNest JWT stays valid: <c>Jwt:LifetimeDays</c> (environment variable <c>Jwt__LifetimeDays</c>),
/// 30 days by default, so users stay signed in like on most sites. A long lifetime is safe enough here because every
/// request re-reads the user's role from the database and tokens of deleted users are rejected (Program.cs).
/// </summary>
public static class JwtLifetime
{
    public const string SettingName = "Jwt:LifetimeDays";
    public const int DefaultDays = 30;
    public const int MaxDays = 365;

    /// <summary>The configured lifetime; throws for a value that is not a whole number of days between 1 and 365.</summary>
    public static TimeSpan From(IConfiguration configuration)
    {
        var raw = configuration[SettingName];
        if (string.IsNullOrWhiteSpace(raw)) return TimeSpan.FromDays(DefaultDays);
        if (!int.TryParse(raw, out var days) || days is < 1 or > MaxDays)
            throw new InvalidOperationException($"{SettingName} must be a whole number of days between 1 and {MaxDays}, got '{raw}'.");
        return TimeSpan.FromDays(days);
    }
}
