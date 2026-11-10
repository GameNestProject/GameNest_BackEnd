using System.ComponentModel.DataAnnotations;

namespace GameNest_BackEnd.DTOs.Auth;

public class RegisterDto
{
    [Required, StringLength(100, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(320)]
    public string Email { get; set; } = string.Empty;

    [Required, PasswordPolicy]
    public string Password { get; set; } = string.Empty;
}
