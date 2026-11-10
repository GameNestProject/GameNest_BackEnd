using System.ComponentModel.DataAnnotations;
using System.Text;

namespace GameNest_BackEnd.DTOs.Auth;

/// <summary>
/// Password rules for new passwords:
/// at least <see cref="MinLength"/> characters, at most <see cref="MaxUtf8Bytes"/> UTF-8 bytes
/// (BCrypt ignores everything after 72 bytes), and not one of a few extremely common passwords.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PasswordPolicyAttribute : ValidationAttribute
{
    public const int MinLength = 8;
    public const int MaxUtf8Bytes = 72;

    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password12", "password123", "passw0rd", "12345678", "123456789", "1234567890",
        "87654321", "11111111", "00000000", "qwerty12", "qwerty123", "qwertyuiop", "1q2w3e4r", "1qaz2wsx",
        "abc12345", "abcd1234", "iloveyou", "letmein1", "welcome1", "admin123", "football", "baseball",
        "gamenest", "gamenest1", "gamenest123"
    };

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string password) return ValidationResult.Success; // [Required] handles missing values.

        var member = validationContext.MemberName is null ? null : new[] { validationContext.MemberName };
        if (password.Length < MinLength)
            return new ValidationResult($"The password must be at least {MinLength} characters long.", member);
        if (Encoding.UTF8.GetByteCount(password) > MaxUtf8Bytes)
            return new ValidationResult($"The password must not exceed {MaxUtf8Bytes} bytes in UTF-8 (shorter for non-Latin characters).", member);
        if (CommonPasswords.Contains(password.Trim()))
            return new ValidationResult("This password is too common. Choose a less predictable password.", member);
        return ValidationResult.Success;
    }
}
