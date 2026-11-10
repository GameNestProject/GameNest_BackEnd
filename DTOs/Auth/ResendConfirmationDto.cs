using System.ComponentModel.DataAnnotations;

namespace GameNest_BackEnd.DTOs.Auth;

public class ResendConfirmationDto
{
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; set; } = string.Empty;
}
