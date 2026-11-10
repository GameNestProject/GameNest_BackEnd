using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameNest_BackEnd.Services;

/// <summary>
/// Sends emails through the Resend REST API (<c>POST https://api.resend.com/emails</c>).
/// Success is reported only when Resend accepts the message (2xx); every other outcome
/// (HTTP error, network failure, timeout) throws <see cref="EmailDeliveryException"/>.
/// Logs never contain the API key, the confirmation token, the confirmation link or the recipient address.
/// </summary>
public class ResendEmailSender : IEmailSender
{
    public const string UserAgent = "GameNest-Backend/1.0";
    private const int MaxLoggedErrorLength = 300;

    private readonly HttpClient _httpClient;
    private readonly ResendOptions _options;
    private readonly string _confirmationUrlBase;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(HttpClient httpClient, IOptions<ResendOptions> options, IOptions<EmailOptions> emailOptions, ILogger<ResendEmailSender> logger)
    {
        _options = options.Value;
        var errors = _options.Validate().Concat(EmailOptions.ValidateConfirmationUrlBase(emailOptions.Value.ConfirmationUrlBase)).ToList();
        if (errors.Count > 0)
            throw new InvalidOperationException("Resend email sender is misconfigured: " + string.Join(" ", errors));

        _httpClient = httpClient;
        _httpClient.BaseAddress ??= ResendOptions.ApiBaseAddress;
        _confirmationUrlBase = emailOptions.Value.ConfirmationUrlBase!.Trim();
        _logger = logger;
    }

    public async Task SendEmailConfirmationAsync(string email, string token, CancellationToken cancellationToken = default)
    {
        var confirmationUrl = EmailOptions.BuildConfirmationUrl(_confirmationUrlBase, email, token);
        var content = ConfirmationEmailContent.Create(confirmationUrl);

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new ResendSendRequest(_options.FormatFrom(), [email], content.Subject, content.Html, content.Text))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey!.Trim());
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Confirmation email accepted by Resend (id {ResendEmailId}).", TryReadString(body, "id") ?? "unknown");
                return;
            }

            var status = (int)response.StatusCode;
            var errorName = TryReadString(body, "name");
            var errorMessage = Sanitize(TryReadString(body, "message"), email, token, confirmationUrl, _options.ApiKey!);
            _logger.LogError("Resend rejected the confirmation email: HTTP {StatusCode}, error {ErrorName}: {ErrorMessage}",
                status, errorName ?? "unknown", errorMessage ?? "(no message)");
            throw new EmailDeliveryException($"Resend rejected the email (HTTP {status}{(errorName is null ? "" : ", " + errorName)}).");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // the caller cancelled (e.g. the client disconnected): not a delivery failure
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError("Resend did not respond within {TimeoutSeconds} seconds.", _options.TimeoutSeconds);
            throw new EmailDeliveryException("Resend did not respond in time.", ex);
        }
        catch (HttpRequestException ex)
        {
            // The exception text only describes the connection problem; it never contains the request headers or body.
            _logger.LogError("Resend is unreachable: {Reason}", ex.HttpRequestError);
            throw new EmailDeliveryException("Resend is unreachable.", ex);
        }
    }

    private static string? TryReadString(string body, string property)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty(property, out var value)
                   && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Removes values that must never reach the logs from a provider error message and limits its length.</summary>
    internal static string? Sanitize(string? message, string email, string token, string confirmationUrl, string apiKey)
    {
        if (string.IsNullOrEmpty(message)) return message;
        foreach (var (secret, replacement) in new[]
                 {
                     (confirmationUrl, "<confirmation-url>"), (apiKey, "<api-key>"), (token, "<token>"),
                     (Uri.EscapeDataString(token), "<token>"), (email, "<recipient>")
                 })
        {
            if (!string.IsNullOrEmpty(secret))
                message = message.Replace(secret, replacement, StringComparison.OrdinalIgnoreCase);
        }
        message = message.ReplaceLineEndings(" ");
        return message.Length <= MaxLoggedErrorLength ? message : message[..MaxLoggedErrorLength] + "…";
    }

    private sealed record ResendSendRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string Text);
}
