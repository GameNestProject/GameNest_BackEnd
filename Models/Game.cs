namespace GameNest_BackEnd.Models;

public class Game
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string CoverUrl { get; set; } = string.Empty;

    public string TrailerUrl { get; set; } = string.Empty;

    public string Developer { get; set; } = string.Empty;

    public string Publisher { get; set; } = string.Empty;

    public DateTime? ReleaseDate { get; set; }

    public int? GameTitleId { get; set; }
    public bool IsArchived { get; set; }

    public ICollection<UserGame> UserGames { get; set; } = new List<UserGame>();

    public ICollection<GameSubmission> GameSubmissions { get; set; } = new List<GameSubmission>();
    public GameTitle? GameTitle { get; set; }
}
