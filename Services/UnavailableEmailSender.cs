namespace GameNest_BackEnd.Services;

/// <summary>
/// Used outside Development until a real email provider (SMTP or an email API) is integrated.
/// It never pretends that an email was sent: every call fails with <see cref="EmailDeliveryException"/>,
/// so callers can respond with 503 instead of silently creating accounts that can never be confirmed.
/// </summary>
public class UnavailableEmailSender : IEmailSender
{
    public Task SendEmailConfirmationAsync(string email, string token, CancellationToken cancellationToken = default) =>
        throw new EmailDeliveryException("No email delivery provider is configured.");
}
