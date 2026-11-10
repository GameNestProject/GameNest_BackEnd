namespace GameNest_BackEnd.Services;

public static class EmailServiceCollectionExtensions
{
    /// <summary>
    /// Registers the email sender:
    /// <list type="bullet">
    /// <item><see cref="ResendEmailSender"/> when the <c>Resend</c> settings are present (any environment);</item>
    /// <item>otherwise <see cref="DevelopmentEmailSender"/> in Development (link in the console log);</item>
    /// <item>otherwise <see cref="UnavailableEmailSender"/>, so registration fails with 503 instead of pretending to send.</item>
    /// </list>
    /// A partial Resend configuration (for example an API key without a sender address) or a missing/invalid
    /// <c>Email:ConfirmationUrlBase</c> next to Resend settings stops the application at startup.
    /// The environment is resolved lazily, when the sender is first requested.
    /// </summary>
    public static IServiceCollection AddEmailSender(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<ResendOptions>(configuration.GetSection(ResendOptions.SectionName));

        if (IsResendConfigured(configuration))
        {
            var errors = GetResendOptions(configuration).Validate()
                .Concat(EmailOptions.ValidateConfirmationUrlBase(configuration[$"{EmailOptions.SectionName}:{nameof(EmailOptions.ConfirmationUrlBase)}"]))
                .ToList();
            if (errors.Count > 0)
                throw new InvalidOperationException("Resend email configuration is incomplete or invalid: " + string.Join(" ", errors));

            services.AddHttpClient<ResendEmailSender>(client => client.BaseAddress = ResendOptions.ApiBaseAddress);
            services.AddScoped<IEmailSender>(sp => sp.GetRequiredService<ResendEmailSender>());
            return services;
        }

        services.AddScoped<IEmailSender>(sp => sp.GetRequiredService<IHostEnvironment>().IsDevelopment()
            ? ActivatorUtilities.CreateInstance<DevelopmentEmailSender>(sp)
            : ActivatorUtilities.CreateInstance<UnavailableEmailSender>(sp));
        return services;
    }

    /// <summary>True when any Resend setting is present; such a configuration must then be complete.</summary>
    public static bool IsResendConfigured(IConfiguration configuration) => GetResendOptions(configuration).IsAnySet;

    private static ResendOptions GetResendOptions(IConfiguration configuration) =>
        configuration.GetSection(ResendOptions.SectionName).Get<ResendOptions>() ?? new ResendOptions();
}
