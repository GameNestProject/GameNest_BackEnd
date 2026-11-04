using System.ComponentModel.DataAnnotations;

namespace GameNest_BackEnd.DTOs.Auth;

public class ConfirmEmailDto { [Required, EmailAddress] public string Email { get; set; } = string.Empty; [Required] public string Token { get; set; } = string.Empty; }
