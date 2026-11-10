namespace GameNest_BackEnd.Services;

/// <summary>
/// Switches for the email/password flow (section <c>Auth</c>). The code, data and email confirmation stay in place;
/// disabled endpoints answer <c>403</c> with a <c>*_disabled</c> code. MVP default (appsettings.json): new accounts only
/// through Google (registration off), password login still on so existing accounts can sign in and link Google
/// (<c>POST /api/Auth/google/link</c>). Turn login off with <c>Auth__PasswordLoginEnabled=false</c> once they have moved.
/// </summary>
public class AuthFeatureOptions
{
    public const string SectionName = "Auth";

    /// <summary>Controls <c>POST /api/Auth/register</c> and <c>POST /api/Auth/resend-confirmation</c>.</summary>
    public bool PasswordRegistrationEnabled { get; set; }

    /// <summary>Controls <c>POST /api/Auth/login</c>.</summary>
    public bool PasswordLoginEnabled { get; set; }
}
