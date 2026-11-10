namespace GameNest_BackEnd.Services;

/// <summary>
/// Thrown by an <see cref="IEmailSender"/> when an email could not be delivered.
/// The message must never contain confirmation tokens or other secrets.
/// </summary>
public class EmailDeliveryException(string message, Exception? innerException = null) : Exception(message, innerException);
