using System.Net;

namespace GameNest_BackEnd.Services;

/// <summary>Subject, HTML and plain-text body of the email-confirmation message.</summary>
public sealed record ConfirmationEmailContent(string Subject, string Html, string Text)
{
    /// <summary>
    /// Builds the message for a confirmation link created with <see cref="EmailOptions.BuildConfirmationUrl"/>.
    /// The link is HTML-encoded in the HTML part. The 24-hour validity matches the token lifetime in AuthController.
    /// </summary>
    public static ConfirmationEmailContent Create(string confirmationUrl)
    {
        const string subject = "Підтвердіть email для GameNest";
        var href = WebUtility.HtmlEncode(confirmationUrl);

        var html = $"""
            <!DOCTYPE html>
            <html lang="uk">
            <head><meta charset="utf-8"><title>{subject}</title></head>
            <body style="margin:0;padding:24px;background:#f4f4f7;font-family:Arial,Helvetica,sans-serif;color:#1f2933;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;margin:0 auto;background:#ffffff;border-radius:8px;padding:32px;">
                <tr><td>
                  <h1 style="margin:0 0 16px;font-size:22px;">Ласкаво просимо до GameNest!</h1>
                  <p style="margin:0 0 16px;line-height:1.5;">Щоб завершити реєстрацію, підтвердіть свою адресу електронної пошти.</p>
                  <p style="margin:0 0 24px;">
                    <a href="{href}" style="display:inline-block;padding:12px 24px;background:#4f46e5;color:#ffffff;text-decoration:none;border-radius:6px;font-weight:bold;">Підтвердити email</a>
                  </p>
                  <p style="margin:0 0 8px;line-height:1.5;font-size:14px;">Якщо кнопка не працює, відкрийте це посилання:</p>
                  <p style="margin:0 0 24px;font-size:13px;word-break:break-all;"><a href="{href}">{href}</a></p>
                  <p style="margin:0;font-size:13px;color:#616e7c;line-height:1.5;">Посилання дійсне 24 години і може бути використане лише один раз. Якщо ви не реєструвалися в GameNest, просто проігноруйте цей лист.</p>
                </td></tr>
              </table>
            </body>
            </html>
            """;

        var text = $"""
            Ласкаво просимо до GameNest!

            Щоб завершити реєстрацію, підтвердіть свою адресу електронної пошти за посиланням:
            {confirmationUrl}

            Посилання дійсне 24 години і може бути використане лише один раз.
            Якщо ви не реєструвалися в GameNest, просто проігноруйте цей лист.
            """;

        return new ConfirmationEmailContent(subject, html, text);
    }
}
