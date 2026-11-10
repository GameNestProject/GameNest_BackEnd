using Google.Apis.Auth;
using Microsoft.Extensions.Options;
using System.Collections;
using System.Net.Mail;

namespace GameNest_BackEnd.Services;

public class GoogleAuthOptions
{
    public const string SectionName = "Google";
    public const string ClientIdSuffix = ".apps.googleusercontent.com";

    /// <summary>OAuth 2.0 Web client ID from Google Cloud Console (not a secret). Environment variable <c>Google__ClientId</c>.</summary>
    public string? ClientId { get; set; }

    public static bool IsValidClientId(string? clientId) =>
        !string.IsNullOrWhiteSpace(clientId) && clientId.Trim().EndsWith(ClientIdSuffix, StringComparison.Ordinal) && !clientId.Contains(' ');
}

/// <summary>How much Google can be trusted about current ownership of the email address.</summary>
public enum GoogleEmailKind
{
    /// <summary>A Gmail address (<c>@gmail.com</c> / <c>@googlemail.com</c>): Google operates the mailbox.</summary>
    Gmail,
    /// <summary>A Google Workspace account: the token has <c>hd</c> and the email is on that domain.</summary>
    Workspace,
    /// <summary>Any other address (a Google account created with an external email): Google verified it once, but is not authoritative now.</summary>
    Other
}

/// <summary>Identity facts taken only from a cryptographically verified Google ID token.</summary>
/// <param name="HostedDomain">The <c>hd</c> claim (Google Workspace domain), lower-case, or null.</param>
public sealed record GoogleIdentity(string Subject, string Email, string? Name, string? PictureUrl, string? HostedDomain = null)
{
    public GoogleEmailKind EmailKind => Classify(Email, HostedDomain);

    /// <summary>True when Google is authoritative for the address (Gmail or Workspace), so it may confirm the email and auto-link accounts.</summary>
    public bool IsEmailAuthoritative => EmailKind != GoogleEmailKind.Other;

    public static GoogleEmailKind Classify(string email, string? hostedDomain)
    {
        var at = email.LastIndexOf('@');
        var domain = at < 0 ? string.Empty : email[(at + 1)..].ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(hostedDomain))
            return domain == hostedDomain.Trim().ToLowerInvariant() ? GoogleEmailKind.Workspace : GoogleEmailKind.Other;
        return domain is "gmail.com" or "googlemail.com" ? GoogleEmailKind.Gmail : GoogleEmailKind.Other;
    }
}

/// <summary>The ID token is invalid (signature, issuer, audience, lifetime, claims) or the Google email is unverified.</summary>
public class GoogleTokenRejectedException(string code, string reason) : Exception(reason)
{
    public const string InvalidToken = "invalid_google_token";
    public const string EmailNotVerified = "google_email_not_verified";

    /// <summary>Error code returned to the client.</summary>
    public string Code { get; } = code;
}

/// <summary>Google sign-in cannot be performed right now (not configured, or Google's certificates are unreachable).</summary>
public class GoogleAuthUnavailableException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public const string NotConfigured = "google_auth_not_configured";
    public const string Unavailable = "google_auth_unavailable";

    public string Code { get; } = code;
}

public interface IGoogleTokenValidator
{
    /// <summary>Verifies the ID token and returns the identity, or throws <see cref="GoogleTokenRejectedException"/> / <see cref="GoogleAuthUnavailableException"/>.</summary>
    Task<GoogleIdentity> ValidateAsync(string idToken, CancellationToken cancellationToken = default);
}

/// <summary>
/// Verifies Google ID tokens with the official Google.Apis.Auth library (signature against Google's published keys,
/// issuer, audience = our client ID, expiry), then checks the claims GameNest relies on (<c>sub</c>, <c>email</c>,
/// <c>email_verified</c>). The token itself is never logged or stored.
/// </summary>
public sealed class GoogleTokenValidator : IGoogleTokenValidator
{
    public delegate Task<GoogleJsonWebSignature.Payload> VerifyDelegate(string idToken, GoogleJsonWebSignature.ValidationSettings settings);

    private static readonly string[] TrustedIssuers = ["accounts.google.com", "https://accounts.google.com"];
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromSeconds(30);

    private readonly GoogleAuthOptions _options;
    private readonly ILogger<GoogleTokenValidator> _logger;
    private readonly VerifyDelegate _verify;

    public GoogleTokenValidator(IOptions<GoogleAuthOptions> options, ILogger<GoogleTokenValidator> logger)
        : this(options, logger, GoogleJsonWebSignature.ValidateAsync) { }

    /// <summary>Test hook: replaces the library call; all GameNest-side checks still run.</summary>
    public GoogleTokenValidator(IOptions<GoogleAuthOptions> options, ILogger<GoogleTokenValidator> logger, VerifyDelegate verify)
    {
        _options = options.Value;
        _logger = logger;
        _verify = verify;
    }

    public async Task<GoogleIdentity> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
    {
        if (!GoogleAuthOptions.IsValidClientId(_options.ClientId))
            throw new GoogleAuthUnavailableException(GoogleAuthUnavailableException.NotConfigured, "Google:ClientId is not configured.");
        var clientId = _options.ClientId!.Trim();

        var settings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = [clientId],
            IssuedAtClockTolerance = ClockTolerance,
            ExpirationTimeClockTolerance = ClockTolerance
        };

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await _verify(idToken, settings).WaitAsync(cancellationToken);
        }
        catch (InvalidJwtException ex)
        {
            // The library's messages describe the failed check (expired, wrong audience, bad signature, ...); they do not contain the token.
            _logger.LogInformation("Google ID token rejected: {Reason}", ex.Message);
            throw new GoogleTokenRejectedException(GoogleTokenRejectedException.InvalidToken, ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Google signing certificates could not be retrieved: {Error}", ex.GetType().Name);
            throw new GoogleAuthUnavailableException(GoogleAuthUnavailableException.Unavailable, "Google certificates unavailable.", ex);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or Newtonsoft.Json.JsonException or System.Text.Json.JsonException)
        {
            // Malformed token (bad base64 / JSON). Treated like any other invalid token.
            _logger.LogInformation("Malformed Google ID token rejected ({Error}).", ex.GetType().Name);
            throw new GoogleTokenRejectedException(GoogleTokenRejectedException.InvalidToken, "Malformed token.");
        }

        try
        {
            return ToIdentity(payload, clientId, DateTimeOffset.UtcNow);
        }
        catch (GoogleTokenRejectedException ex)
        {
            _logger.LogInformation("Google ID token rejected: {Reason}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// GameNest-side checks on a payload the library has verified. They repeat issuer/audience/expiry defensively
    /// and enforce the claims GameNest depends on. Throws <see cref="GoogleTokenRejectedException"/> when not acceptable.
    /// </summary>
    public static GoogleIdentity ToIdentity(GoogleJsonWebSignature.Payload payload, string clientId, DateTimeOffset now)
    {
        static GoogleIdentity Reject(string code, string reason) => throw new GoogleTokenRejectedException(code, reason);

        if (payload.Issuer is null || !TrustedIssuers.Contains(payload.Issuer))
            return Reject(GoogleTokenRejectedException.InvalidToken, "Untrusted issuer.");
        if (!AudienceContains(payload.Audience, clientId))
            return Reject(GoogleTokenRejectedException.InvalidToken, "Token was issued for another client.");
        if (payload.ExpirationTimeSeconds is not { } exp || DateTimeOffset.FromUnixTimeSeconds(exp) < now - ClockTolerance)
            return Reject(GoogleTokenRejectedException.InvalidToken, "Token is expired or has no expiry.");
        if (string.IsNullOrWhiteSpace(payload.Subject) || payload.Subject.Length > 255)
            return Reject(GoogleTokenRejectedException.InvalidToken, "Token has no valid subject.");
        if (string.IsNullOrWhiteSpace(payload.Email) || !MailAddress.TryCreate(payload.Email.Trim(), out var address) || address.Address != payload.Email.Trim())
            return Reject(GoogleTokenRejectedException.InvalidToken, "Token has no valid email.");
        if (!payload.EmailVerified)
            return Reject(GoogleTokenRejectedException.EmailNotVerified, "Google email address is not verified.");

        var name = string.IsNullOrWhiteSpace(payload.Name) ? payload.GivenName : payload.Name;
        var picture = Uri.TryCreate(payload.Picture, UriKind.Absolute, out var pictureUri) && pictureUri.Scheme == Uri.UriSchemeHttps && payload.Picture!.Length <= 2048
            ? payload.Picture
            : null;

        // Same normalization as email/password registration (AuthController.NormalizeEmail).
        var hostedDomain = string.IsNullOrWhiteSpace(payload.HostedDomain) ? null : payload.HostedDomain.Trim().ToLowerInvariant();
        return new GoogleIdentity(payload.Subject, payload.Email.Trim().ToLowerInvariant(), name?.Trim(), picture, hostedDomain);
    }

    private static bool AudienceContains(object? audience, string clientId) => audience switch
    {
        string single => single == clientId,
        IEnumerable many => many.Cast<object?>().Any(a => a?.ToString() == clientId),
        _ => false
    };
}
