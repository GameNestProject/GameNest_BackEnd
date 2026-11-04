namespace GameNest_BackEnd.Models;

public class User
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = "User";

    public bool EmailConfirmed { get; set; }
    public string? EmailConfirmationTokenHash { get; set; }
    public DateTime? EmailConfirmationTokenExpiresAt { get; set; }

    public bool IsProfilePublic { get; set; } = true;
    public bool IsLibraryPublic { get; set; } = true;

    public ICollection<UserGame> UserGames { get; set; } = new List<UserGame>();

    public ICollection<GameSubmission> GameSubmissions { get; set; } = new List<GameSubmission>();
    public ICollection<UserCreatedGame> UserCreatedGames { get; set; } = new List<UserCreatedGame>();
}
