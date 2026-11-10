using System.Net.Mail;

namespace GameNest_BackEnd.Services;

/// <summary>
/// Settings for <see cref="ResendEmailSender"/>, bound from the <c>Resend</c> section
/// (environment variables <c>Resend__ApiKey</c>, <c>Resend__FromEmail</c>, <c>Resend__FromName</c>).
/// The API key is a secret: it must only come from environment variables or User Secrets.
/// </summary>
public class ResendOptions
{
    public const string SectionName = "Resend";
    public const string DefaultFromName = "GameNest";
    public static readonly Uri ApiBaseAddress = new("https://api.resend.com/");

    public string? ApiKey { get; set; }

    /// <summary>Sender address on a domain verified in Resend, e.g. <c>no-reply@gamenest.pp.ua</c>.</summary>
    public string? FromEmail { get; set; }

    /// <summary>Display name of the sender. Defaults to <see cref="DefaultFromName"/>.</summary>
    public string? FromName { get; set; }

    /// <summary>Timeout of a single request to Resend.</summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>True when at least one Resend setting is present (used to detect partial configuration).</summary>
    public bool IsAnySet =>
        !string.IsNullOrWhiteSpace(ApiKey) || !string.IsNullOrWhiteSpace(FromEmail) || !string.IsNullOrWhiteSpace(FromName);

    /// <summary>Returns the configuration problems, without echoing any values.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(ApiKey))
            errors.Add("Resend:ApiKey (environment variable Resend__ApiKey) is not set.");
        if (string.IsNullOrWhiteSpace(FromEmail))
            errors.Add("Resend:FromEmail (environment variable Resend__FromEmail) is not set.");
        else if (!MailAddress.TryCreate(FromEmail.Trim(), out var address) || address.Address != FromEmail.Trim())
            errors.Add("Resend:FromEmail must be a plain email address such as no-reply@example.com.");
        if (FromName is not null && (FromName.Contains('<') || FromName.Contains('>') || FromName.Contains('"') || FromName.Contains('\r') || FromName.Contains('\n')))
            errors.Add("Resend:FromName must not contain <, >, quotes or line breaks.");
        if (TimeoutSeconds is < 1 or > 120)
            errors.Add("Resend:TimeoutSeconds must be between 1 and 120.");
        return errors;
    }

    /// <summary>The <c>from</c> value in the format Resend expects: <c>Name &lt;email&gt;</c>.</summary>
    public string FormatFrom()
    {
        var name = string.IsNullOrWhiteSpace(FromName) ? DefaultFromName : FromName.Trim();
        return $"{name} <{FromEmail!.Trim()}>";
    }
}
