using GameNest_BackEnd.Data;
using GameNest_BackEnd.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Xunit;

namespace GameNest_BackEnd.Tests;

/// <summary>Records requests and answers them without any network access.</summary>
public sealed class FakeResendHandler : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
        (_, _) => Task.FromResult(Json(HttpStatusCode.OK, """{"id":"49a3999c-0ce1-4ea6-ab68-afcd6dc2e794"}"""));

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        return await Respond(request, cancellationToken);
    }

    /// <summary>Extracts the confirmation link (email and token) from the plain-text part of a captured request.</summary>
    public static (string Email, string Token) ReadConfirmationLink(string requestBody)
    {
        using var doc = JsonDocument.Parse(requestBody);
        var text = doc.RootElement.GetProperty("text").GetString()!;
        var url = text.Split('\n').Select(l => l.Trim()).Single(l => l.StartsWith("http", StringComparison.Ordinal));
        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        return (query["email"]!, query["token"]!);
    }
}

public sealed class ListLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Messages.Add(formatter(state, exception) + (exception is null ? "" : " | " + exception));
}

public class ResendEmailSenderTests
{
    private const string ApiKey = "re_test_0123456789abcdefSECRET";
    private const string Recipient = "player+1@test.local";
    private const string Token = "ab+c/d==tok";
    private const string UrlBase = "https://gamenest.test/confirm-email";

    private readonly FakeResendHandler _handler = new();
    private readonly ListLogger<ResendEmailSender> _logger = new();

    private ResendEmailSender CreateSender(Action<ResendOptions>? configure = null, string? urlBase = UrlBase)
    {
        var options = new ResendOptions { ApiKey = ApiKey, FromEmail = "no-reply@gamenest.test", FromName = "GameNest" };
        configure?.Invoke(options);
        return new ResendEmailSender(new HttpClient(_handler), Options.Create(options),
            Options.Create(new EmailOptions { ConfirmationUrlBase = urlBase }), _logger);
    }

    private void AssertNoSecretsLogged()
    {
        foreach (var message in _logger.Messages)
        {
            Assert.DoesNotContain(ApiKey, message);
            Assert.DoesNotContain(Token, message);
            Assert.DoesNotContain(Uri.EscapeDataString(Token), message);
            Assert.DoesNotContain(Recipient, message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Sends_the_confirmation_email_through_the_resend_api()
    {
        await CreateSender().SendEmailConfirmationAsync(Recipient, Token);

        var (request, body) = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.resend.com/emails", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(ApiKey, request.Headers.Authorization.Parameter);
        Assert.Contains(ResendEmailSender.UserAgent, request.Headers.UserAgent.ToString());

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("GameNest <no-reply@gamenest.test>", root.GetProperty("from").GetString());
        Assert.Equal(Recipient, Assert.Single(root.GetProperty("to").EnumerateArray()).GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("subject").GetString()));

        const string expectedUrl = UrlBase + "?email=player%2B1%40test.local&token=ab%2Bc%2Fd%3D%3Dtok";
        Assert.Contains(expectedUrl, root.GetProperty("text").GetString());
        // In HTML the link is attribute-encoded (& becomes &amp;).
        Assert.Contains($"href=\"{expectedUrl.Replace("&", "&amp;")}\"", root.GetProperty("html").GetString());
        Assert.Equal((Recipient, Token), FakeResendHandler.ReadConfirmationLink(body));

        Assert.Contains(_logger.Messages, m => m.Contains("49a3999c-0ce1-4ea6-ab68-afcd6dc2e794"));
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task From_name_defaults_to_GameNest()
    {
        await CreateSender(o => o.FromName = null).SendEmailConfirmationAsync(Recipient, Token);

        using var doc = JsonDocument.Parse(_handler.Requests.Single().Body);
        Assert.Equal("GameNest <no-reply@gamenest.test>", doc.RootElement.GetProperty("from").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, """{"statusCode":403,"name":"validation_error","message":"The gamenest.test domain is not verified. Cannot send to player+1@test.local"}""")]
    [InlineData(HttpStatusCode.Unauthorized, """{"statusCode":401,"name":"validation_error","message":"API key is invalid"}""")]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"name":"missing_required_field","message":"Missing `to` field."}""")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"name":"rate_limit_exceeded","message":"Too many requests."}""")]
    [InlineData(HttpStatusCode.InternalServerError, "<html>upstream error</html>")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "")]
    public async Task Api_errors_are_reported_as_delivery_failures(HttpStatusCode status, string body)
    {
        _handler.Respond = (_, _) => Task.FromResult(FakeResendHandler.Json(status, body));

        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => CreateSender().SendEmailConfirmationAsync(Recipient, Token));

        Assert.Contains(((int)status).ToString(), ex.Message);
        Assert.Contains(_logger.Messages, m => m.Contains(((int)status).ToString()));
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task Error_message_that_echoes_secrets_is_sanitized_in_the_logs()
    {
        var echo = $"Bad request for {Recipient} with key {ApiKey} and token {Token}";
        _handler.Respond = (_, _) => Task.FromResult(FakeResendHandler.Json(HttpStatusCode.BadRequest,
            JsonSerializer.Serialize(new { name = "validation_error", message = echo })));

        await Assert.ThrowsAsync<EmailDeliveryException>(() => CreateSender().SendEmailConfirmationAsync(Recipient, Token));

        Assert.Contains(_logger.Messages, m => m.Contains("<recipient>") && m.Contains("<api-key>") && m.Contains("<token>"));
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task Unreachable_resend_is_a_delivery_failure()
    {
        _handler.Respond = (_, _) => throw new HttpRequestException(HttpRequestError.NameResolutionError, "No such host is known.");

        await Assert.ThrowsAsync<EmailDeliveryException>(() => CreateSender().SendEmailConfirmationAsync(Recipient, Token));
        Assert.Contains(_logger.Messages, m => m.Contains("unreachable"));
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task Slow_resend_times_out_as_a_delivery_failure()
    {
        _handler.Respond = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(HttpStatusCode.OK); };

        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => CreateSender(o => o.TimeoutSeconds = 1).SendEmailConfirmationAsync(Recipient, Token));
        Assert.Contains("in time", ex.Message);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_a_delivery_failure()
    {
        _handler.Respond = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(HttpStatusCode.OK); };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateSender().SendEmailConfirmationAsync(Recipient, Token, cts.Token));
        Assert.IsNotType<EmailDeliveryException>(ex.InnerException);
    }

    [Theory]
    [InlineData(null, "no-reply@gamenest.test", UrlBase, "Resend__ApiKey")]
    [InlineData(ApiKey, null, UrlBase, "Resend__FromEmail")]
    [InlineData(ApiKey, "GameNest <no-reply@gamenest.test>", UrlBase, "plain email address")]
    [InlineData(ApiKey, "no-reply@gamenest.test", null, "Email__ConfirmationUrlBase")]
    [InlineData(ApiKey, "no-reply@gamenest.test", "/confirm-email", "absolute")]
    public void Missing_or_invalid_configuration_is_rejected_without_echoing_the_key(string? apiKey, string? fromEmail, string? urlBase, string expected)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CreateSender(o => { o.ApiKey = apiKey; o.FromEmail = fromEmail; }, urlBase));

        Assert.Contains(expected, ex.Message);
        Assert.DoesNotContain(ApiKey, ex.Message);
        Assert.Empty(_handler.Requests);
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "GameNest_BackEnd";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IServiceCollection Services(string environment, Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new FakeEnvironment(environment));
        return services.AddEmailSender(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void Complete_resend_configuration_selects_the_resend_sender(string environment)
    {
        using var provider = Services(environment, new()
        {
            ["Resend:ApiKey"] = ApiKey, ["Resend:FromEmail"] = "no-reply@gamenest.test", ["Email:ConfirmationUrlBase"] = UrlBase
        }).BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<ResendEmailSender>(scope.ServiceProvider.GetRequiredService<IEmailSender>());
    }

    [Theory]
    [InlineData("Resend:ApiKey")]
    [InlineData("Resend:FromEmail")]
    [InlineData("Resend:FromName")]
    public void Partial_resend_configuration_stops_startup(string onlySetting)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Services("Production", new() { [onlySetting] = onlySetting == "Resend:ApiKey" ? ApiKey : "GameNest" }));
        Assert.DoesNotContain(ApiKey, ex.Message);
    }

    [Fact]
    public void Resend_without_confirmation_url_stops_startup()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Services("Production", new()
        {
            ["Resend:ApiKey"] = ApiKey, ["Resend:FromEmail"] = "no-reply@gamenest.test"
        }));
        Assert.Contains("Email__ConfirmationUrlBase", ex.Message);
    }
}

/// <summary>The real API with the real <see cref="ResendEmailSender"/>; only the HTTP transport to Resend is faked.</summary>
public class ResendApiFactory : ApiFactory
{
    public FakeResendHandler Handler { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddScoped<IEmailSender>(sp => new ResendEmailSender(
                new HttpClient(Handler),
                Options.Create(new ResendOptions { ApiKey = "re_integration_test_key", FromEmail = "no-reply@gamenest.test" }),
                Options.Create(new EmailOptions { ConfirmationUrlBase = "https://gamenest.test/confirm-email" }),
                sp.GetRequiredService<ILogger<ResendEmailSender>>()));
        });
    }
}

public class ResendRegistrationTests(ResendApiFactory factory) : IClassFixture<ResendApiFactory>
{
    private const string Password = "correct-horse-battery";

    [Fact]
    public async Task Registration_email_link_confirms_the_account()
    {
        var client = factory.CreateClient();
        var s = TestHelpers.UniqueSuffix();
        var email = $"resend{s}@test.local";

        var register = await client.PostAsJsonAsync("/api/Auth/register", new { username = "resend" + s, email, password = Password });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var (linkEmail, linkToken) = FakeResendHandler.ReadConfirmationLink(factory.Handler.Requests.Last().Body);
        Assert.Equal(email, linkEmail);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/Auth/login", new { email, password = Password })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/Auth/confirm-email", new { email = linkEmail, token = linkToken })).StatusCode);
        Assert.False(string.IsNullOrEmpty(await client.LoginAsync(email, Password)));
    }

    [Fact]
    public async Task Resend_failure_returns_503_keeps_no_account_and_allows_registering_again()
    {
        var client = factory.CreateClient();
        var s = TestHelpers.UniqueSuffix();
        var email = $"fail{s}@test.local";
        var body = new { username = "fail" + s, email, password = Password };

        factory.Handler.Respond = (_, _) => Task.FromResult(FakeResendHandler.Json(HttpStatusCode.Forbidden, """{"name":"validation_error","message":"domain not verified"}"""));
        try
        {
            var failed = await client.PostAsJsonAsync("/api/Auth/register", body);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.Equal("email_service_unavailable", (await failed.ReadJsonAsync()).GetProperty("code").GetString());

            using (var scope = factory.Services.CreateScope())
                Assert.False(await scope.ServiceProvider.GetRequiredService<GameNestDbContext>().Users.AnyAsync(u => u.Email == email));
        }
        finally
        {
            factory.Handler.Respond = (_, _) => Task.FromResult(FakeResendHandler.Json(HttpStatusCode.OK, """{"id":"ok"}"""));
        }

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/Auth/register", body)).StatusCode);
        using var verify = factory.Services.CreateScope();
        var users = await verify.ServiceProvider.GetRequiredService<GameNestDbContext>().Users.Where(u => u.Email == email).ToListAsync();
        Assert.False(Assert.Single(users).EmailConfirmed);
    }

    [Fact]
    public async Task Resend_failure_on_resend_confirmation_keeps_the_previous_link_valid()
    {
        var client = factory.CreateClient();
        var s = TestHelpers.UniqueSuffix();
        var email = $"again{s}@test.local";
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/Auth/register", new { username = "again" + s, email, password = Password })).StatusCode);
        var (_, firstToken) = FakeResendHandler.ReadConfirmationLink(factory.Handler.Requests.Last().Body);

        factory.Handler.Respond = (_, _) => throw new HttpRequestException("connection refused");
        try
        {
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/Auth/resend-confirmation", new { email })).StatusCode);
        }
        finally
        {
            factory.Handler.Respond = (_, _) => Task.FromResult(FakeResendHandler.Json(HttpStatusCode.OK, """{"id":"ok"}"""));
        }

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/Auth/confirm-email", new { email, token = firstToken })).StatusCode);
    }
}
