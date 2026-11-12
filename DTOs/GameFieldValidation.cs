using System.ComponentModel.DataAnnotations;

namespace GameNest_BackEnd.DTOs;

/// <summary>
/// Shared limits and normalization for the game fields used by <see cref="SubmissionDto"/> and <see cref="CreateGameDto"/>.
/// </summary>
public static class GameFields
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 5000;
    public const int NameMaxLength = 200;   // developer, publisher
    public const int UrlMaxLength = 2048;
    public const int RejectionReasonMaxLength = 2000;
    public const int MinReleaseYear = 1950;
    public const int MaxYearsAhead = 10;

    /// <summary>Trims text and turns null into an empty string (the columns are NOT NULL).</summary>
    public static string Text(string? value) => value?.Trim() ?? string.Empty;

    /// <summary>
    /// A release date is a calendar date: keep the date part and store it as UTC midnight.
    /// PostgreSQL (timestamptz) rejects DateTime values with Kind=Unspecified, which is what "2025-05-01" deserializes to.
    /// </summary>
    public static DateTime? ReleaseDate(DateTime? value) =>
        value is { } d ? DateTime.SpecifyKind(d.Date, DateTimeKind.Utc) : null;
}

/// <summary>Empty, or an absolute https URL without credentials (cover and trailer links).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class HttpsUrlAttribute : ValidationAttribute
{
    public HttpsUrlAttribute() : base("{0} must be an absolute https:// URL.") { }

    public override bool IsValid(object? value)
    {
        if (value is null) return true;
        if (value is not string text) return false;
        text = text.Trim();
        if (text.Length == 0) return true;
        return text.Length <= GameFields.UrlMaxLength
               && Uri.TryCreate(text, UriKind.Absolute, out var uri)
               && uri.Scheme == Uri.UriSchemeHttps
               && !string.IsNullOrEmpty(uri.Host)
               && string.IsNullOrEmpty(uri.UserInfo);
    }
}

/// <summary>Null, or a date between <see cref="GameFields.MinReleaseYear"/> and <see cref="GameFields.MaxYearsAhead"/> years from now.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ReleaseDateAttribute : ValidationAttribute
{
    public ReleaseDateAttribute() : base("{0} must be between " + GameFields.MinReleaseYear + " and " + GameFields.MaxYearsAhead + " years from now.") { }

    public override bool IsValid(object? value) =>
        value is null || (value is DateTime d && d.Year >= GameFields.MinReleaseYear && d.Date <= DateTime.UtcNow.Date.AddYears(GameFields.MaxYearsAhead));
}
