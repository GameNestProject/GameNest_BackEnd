namespace GameNest_BackEnd.Services;

// SMTP/provider integration belongs behind this interface. This fallback deliberately never exposes tokens to clients.
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendEmailConfirmationAsync(string email, string token, CancellationToken cancellationToken = default)
    {
        logger.LogWarning("Email confirmation requested for {Email}, but no email provider is configured.", email);
        return Task.CompletedTask;
    }
}
