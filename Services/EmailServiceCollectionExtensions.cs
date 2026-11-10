namespace GameNest_BackEnd.Services;

public static class EmailServiceCollectionExtensions
{
    /// <summary>
    /// Registers the email sender for the current environment: <see cref="DevelopmentEmailSender"/>
    /// in Development, <see cref="UnavailableEmailSender"/> everywhere else until a real provider exists.
    /// The environment is resolved lazily, when the sender is first requested.
    /// </summary>
    public static IServiceCollection AddEmailSender(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.AddScoped<IEmailSender>(sp => sp.GetRequiredService<IHostEnvironment>().IsDevelopment()
            ? ActivatorUtilities.CreateInstance<DevelopmentEmailSender>(sp)
            : ActivatorUtilities.CreateInstance<UnavailableEmailSender>(sp));
        return services;
    }
}
