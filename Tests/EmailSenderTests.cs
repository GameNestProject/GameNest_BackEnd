using GameNest_BackEnd.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameNest_BackEnd.Tests;

public class EmailSenderTests
{
    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "GameNest_BackEnd";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private static IServiceProvider BuildProvider(string environment)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new FakeEnvironment(environment));
        services.AddEmailSender(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("Development", typeof(DevelopmentEmailSender))]
    [InlineData("Production", typeof(UnavailableEmailSender))]
    [InlineData("Staging", typeof(UnavailableEmailSender))]
    public void Email_sender_is_selected_by_environment(string environment, Type expected)
    {
        using var scope = BuildProvider(environment).CreateScope();
        Assert.IsType(expected, scope.ServiceProvider.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public async Task Development_sender_logs_a_confirmation_link_with_the_escaped_email_and_token()
    {
        var logger = new CapturingLogger<DevelopmentEmailSender>();
        var options = Options.Create(new EmailOptions { ConfirmationUrlBase = "http://localhost:3000/confirm-email" });
        var sender = new DevelopmentEmailSender(new FakeEnvironment("Development"), options, logger);

        await sender.SendEmailConfirmationAsync("user+1@test.local", "ab+c/d==");

        var message = Assert.Single(logger.Messages);
        Assert.Contains("http://localhost:3000/confirm-email?email=user%2B1%40test.local&token=ab%2Bc%2Fd%3D%3D", message);
    }

    [Fact]
    public void Development_sender_refuses_to_run_outside_development()
    {
        var options = Options.Create(new EmailOptions());
        Assert.Throws<InvalidOperationException>(() =>
            new DevelopmentEmailSender(new FakeEnvironment("Production"), options, new CapturingLogger<DevelopmentEmailSender>()));
    }

    [Fact]
    public async Task Unavailable_sender_never_reports_success()
    {
        await Assert.ThrowsAsync<EmailDeliveryException>(() => new UnavailableEmailSender().SendEmailConfirmationAsync("a@b.c", "token"));
    }

    [Fact]
    public void Confirmation_url_keeps_existing_query_parameters()
    {
        Assert.Equal("https://x.test/c?lang=uk&email=a%40b.c&token=t", EmailOptions.BuildConfirmationUrl("https://x.test/c?lang=uk", "a@b.c", "t"));
    }
}
