using Microsoft.Extensions.Options;

namespace GameNest_BackEnd.Services;

/// <summary>
/// Development-only email sender: writes the confirmation link to the application log
/// instead of sending an email. It refuses to run outside the Development environment
/// because the log output contains the confirmation token.
/// </summary>
public class DevelopmentEmailSender : IEmailSender
{
    private readonly ILogger<DevelopmentEmailSender> _logger;
    private readonly EmailOptions _options;

    public DevelopmentEmailSender(IHostEnvironment environment, IOptions<EmailOptions> options, ILogger<DevelopmentEmailSender> logger)
    {
        if (!environment.IsDevelopment())
            throw new InvalidOperationException($"{nameof(DevelopmentEmailSender)} may only be used in the Development environment.");
        _logger = logger;
        _options = options.Value;
    }

    public Task SendEmailConfirmationAsync(string email, string token, CancellationToken cancellationToken = default)
    {
        var confirmationUrl = string.IsNullOrWhiteSpace(_options.ConfirmationUrlBase)
            ? "(set Email:ConfirmationUrlBase to get a link)"
            : EmailOptions.BuildConfirmationUrl(_options.ConfirmationUrlBase, email, token);

        _logger.LogWarning(
            "[Development email] Confirmation link for {Email}: {ConfirmationUrl}. " +
            "Without a frontend, confirm via POST /api/Auth/confirm-email with body {{\"email\":\"{Email}\",\"token\":\"{Token}\"}}",
            email, confirmationUrl, email, token);

        return Task.CompletedTask;
    }
}
