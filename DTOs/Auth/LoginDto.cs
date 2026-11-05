using System.ComponentModel.DataAnnotations;

namespace GameNest_BackEnd.DTOs.Auth;

public class LoginDto
{
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(128)]
    public string Password { get; set; } = string.Empty;
}
