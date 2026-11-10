using System.ComponentModel.DataAnnotations;

namespace GameNest_BackEnd.DTOs.Auth;

/// <summary>Explicit linking of a Google account to the signed-in user: Google ID token plus the account password.</summary>
public class GoogleLinkDto
{
    [Required, StringLength(8192, MinimumLength = 1)]
    public string IdToken { get; set; } = string.Empty;

    [Required, StringLength(128)]
    public string Password { get; set; } = string.Empty;
}
