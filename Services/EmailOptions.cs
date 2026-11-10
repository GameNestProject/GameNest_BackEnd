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

    /// <summary>Returns problems with a confirmation URL base that real emails would link to.</summary>
    public static IReadOnlyList<string> ValidateConfirmationUrlBase(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return ["Email:ConfirmationUrlBase (environment variable Email__ConfirmationUrlBase) is not set."];
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(uri.Fragment))
            return ["Email:ConfirmationUrlBase must be an absolute http(s) URL without a #fragment, e.g. https://gamenest.pp.ua/confirm-email."];
        return [];
    }

    public static string BuildConfirmationUrl(string baseUrl, string email, string token)
    {
        var separator = baseUrl.Contains('?') ? '&' : '?';
        return $"{baseUrl}{separator}email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
    }
}
