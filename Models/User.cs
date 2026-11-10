namespace GameNest_BackEnd.Models;

public class User
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    /// <summary>BCrypt hash of the password; null for accounts that sign in only with an external provider (Google).</summary>
    public string? PasswordHash { get; set; }

    /// <summary>Profile picture URL taken from the external provider at sign-up; may be null.</summary>
    public string? AvatarUrl { get; set; }

    public string Role { get; set; } = "User";

    public bool EmailConfirmed { get; set; }
    public string? EmailConfirmationTokenHash { get; set; }
    public DateTime? EmailConfirmationTokenExpiresAt { get; set; }

    public bool IsProfilePublic { get; set; } = true;
    public bool IsLibraryPublic { get; set; } = true;

    public ICollection<UserGame> UserGames { get; set; } = new List<UserGame>();

    public ICollection<GameSubmission> GameSubmissions { get; set; } = new List<GameSubmission>();
    public ICollection<UserCreatedGame> UserCreatedGames { get; set; } = new List<UserCreatedGame>();
    public ICollection<ExternalLogin> ExternalLogins { get; set; } = new List<ExternalLogin>();
}
