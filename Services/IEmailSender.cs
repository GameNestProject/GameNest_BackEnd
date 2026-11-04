namespace GameNest_BackEnd.Services;

public interface IEmailSender
{
    Task SendEmailConfirmationAsync(string email, string token, CancellationToken cancellationToken = default);
}
