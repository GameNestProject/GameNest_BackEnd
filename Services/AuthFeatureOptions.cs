namespace GameNest_BackEnd.Services;

/// <summary>
/// Switches for the email/password flow (section <c>Auth</c>). The code, data and email confirmation stay in place;
/// disabled endpoints answer <c>403</c> with a <c>*_disabled</c> code. MVP default (appsettings.json): Google-only, both
/// switches off. For a temporary migration of existing accounts an operator may set <c>Auth__PasswordLoginEnabled=true</c>
/// explicitly (users then sign in and link Google via <c>POST /api/Auth/google/link</c>) and must switch it off afterwards.
/// </summary>
public class AuthFeatureOptions
{
    public const string SectionName = "Auth";

    /// <summary>Controls <c>POST /api/Auth/register</c> and <c>POST /api/Auth/resend-confirmation</c>.</summary>
    public bool PasswordRegistrationEnabled { get; set; }

    /// <summary>Controls <c>POST /api/Auth/login</c>.</summary>
    public bool PasswordLoginEnabled { get; set; }
}
