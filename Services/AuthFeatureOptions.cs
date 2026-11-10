namespace GameNest_BackEnd.Services;

/// <summary>
/// Switches for the email/password flow (section <c>Auth</c>). The code, data and email confirmation stay in place;
/// disabled endpoints answer <c>403</c> with a <c>*_disabled</c> code. The MVP default is Google-only (both false),
/// see appsettings.json. Re-enable with <c>Auth__PasswordRegistrationEnabled=true</c> / <c>Auth__PasswordLoginEnabled=true</c>.
/// </summary>
public class AuthFeatureOptions
{
    public const string SectionName = "Auth";

    /// <summary>Controls <c>POST /api/Auth/register</c> and <c>POST /api/Auth/resend-confirmation</c>.</summary>
    public bool PasswordRegistrationEnabled { get; set; }

    /// <summary>Controls <c>POST /api/Auth/login</c>.</summary>
    public bool PasswordLoginEnabled { get; set; }
}
