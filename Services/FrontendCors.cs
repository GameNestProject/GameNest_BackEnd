namespace GameNest_BackEnd.Services;

/// <summary>
/// CORS for the browser frontend. Allowed origins come from the <c>CORS_ALLOWED_ORIGINS</c> setting
/// (environment variable or configuration), a comma-separated list such as
/// <c>https://gamenest.pp.ua,https://www.gamenest.pp.ua</c>. Wildcards are not accepted.
/// Outside Development the setting is required, so a misconfigured deployment fails at startup
/// instead of silently rejecting (or allowing) every browser request.
/// </summary>
public static class FrontendCors
{
    public const string PolicyName = "Frontend";
    public const string SettingName = "CORS_ALLOWED_ORIGINS";

    public static IServiceCollection AddFrontendCors(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var origins = ParseOrigins(configuration[SettingName]);
        if (origins.Length == 0 && !environment.IsDevelopment())
            throw new InvalidOperationException(
                $"{SettingName} must be configured outside Development (comma-separated list of allowed origins, e.g. https://gamenest.pp.ua).");

        services.AddCors(options => options.AddPolicy(PolicyName, policy => policy
            .WithOrigins(origins)
            .WithMethods("GET", "POST", "PUT", "DELETE")
            .WithHeaders("Authorization", "Content-Type")
            // Lets the frontend read the retry delay of 429 responses and the URL of created resources.
            .WithExposedHeaders("Retry-After", "Location")
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10))));
        return services;
    }

    /// <summary>Parses and validates the comma-separated origin list. Each origin must be an absolute http(s) URL without a path.</summary>
    public static string[] ParseOrigins(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];

        var origins = new List<string>();
        foreach (var raw in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = raw.TrimEnd('/');
            if (candidate.Contains('*')
                || !Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
                || !string.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidOperationException(
                    $"{SettingName} contains an invalid origin '{raw}'. Use absolute origins such as https://gamenest.pp.ua (no path, no wildcard).");
            origins.Add(uri.GetLeftPart(UriPartial.Authority));
        }
        return origins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
