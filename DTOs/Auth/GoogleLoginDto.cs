using System.ComponentModel.DataAnnotations;

namespace GameNest_BackEnd.DTOs.Auth;

/// <summary>The Google ID token (the <c>credential</c> from Google Identity Services). Nothing else is accepted from the client.</summary>
public class GoogleLoginDto
{
    [Required, StringLength(8192, MinimumLength = 1)]
    public string IdToken { get; set; } = string.Empty;
}
