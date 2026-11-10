namespace GameNest_BackEnd.Services;

public class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Base URL of the page that confirms an email address, for example the frontend route
    /// <c>https://example.com/confirm-email</c>. The <c>email</c> and <c>token</c> query
    /// parameters are appended to it.
    /// </summary>
    public string? ConfirmationUrlBase { get; set; }

    public static string BuildConfirmationUrl(string baseUrl, string email, string token)
    {
        var separator = baseUrl.Contains('?') ? '&' : '?';
        return $"{baseUrl}{separator}email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
    }
}
