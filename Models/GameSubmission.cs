namespace GameNest_BackEnd.Models;

public class GameSubmission
{
    public int Id { get; set; }

    public int SubmittedByUserId { get; set; }

    public int? GameId { get; set; }

    public string Type { get; set; } = "NewGame";

    public string Status { get; set; } = "Pending";

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string CoverUrl { get; set; } = string.Empty;

    public string TrailerUrl { get; set; } = string.Empty;

    public string Developer { get; set; } = string.Empty;

    public string Publisher { get; set; } = string.Empty;

    public DateTime? ReleaseDate { get; set; }

    public string? AdminComment { get; set; }

    public int? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAt { get; set; }

    /// <summary>When the submission was created (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User SubmittedByUser { get; set; } = null!;

    public Game? Game { get; set; }

    public User? ReviewedByUser { get; set; }
}