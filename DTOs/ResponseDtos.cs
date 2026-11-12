using GameNest_BackEnd.Models;

namespace GameNest_BackEnd.DTOs;

// Response models: controllers never return EF entities, so navigation properties
// cannot leak into responses or create serialization cycles.

public record GameResponse(
    int Id, string Title, string Description, string CoverUrl, string TrailerUrl,
    string Developer, string Publisher, DateTime? ReleaseDate, int? GameTitleId, bool IsArchived)
{
    public static GameResponse From(Game g) =>
        new(g.Id, g.Title, g.Description, g.CoverUrl, g.TrailerUrl, g.Developer, g.Publisher, g.ReleaseDate, g.GameTitleId, g.IsArchived);
}

public record LibraryItemResponse(int Id, int UserId, int GameId, string Status, bool Purchased, GameResponse? Game)
{
    public static LibraryItemResponse From(UserGame item, Game? game = null)
    {
        var source = game ?? item.Game;
        return new(item.Id, item.UserId, item.GameId, item.Status, item.Purchased, source is null ? null : GameResponse.From(source));
    }
}

/// <summary>
/// A game submission. <c>GameId</c>: for "Correction" the corrected game; for an approved "NewGame" the catalog game
/// created by the approval (null while pending or rejected). <c>SubmittedByUsername</c> is the submitter's public username.
/// </summary>
public record SubmissionResponse(
    int Id, int SubmittedByUserId, int? GameId, string Type, string Status, string Title, string Description,
    string CoverUrl, string TrailerUrl, string Developer, string Publisher, DateTime? ReleaseDate,
    string? AdminComment, int? ReviewedByUserId, DateTime? ReviewedAt, DateTime CreatedAt, string? SubmittedByUsername)
{
    public static SubmissionResponse From(GameSubmission s, string? submittedByUsername = null) =>
        new(s.Id, s.SubmittedByUserId, s.GameId, s.Type, s.Status, s.Title, s.Description, s.CoverUrl, s.TrailerUrl,
            s.Developer, s.Publisher, s.ReleaseDate, s.AdminComment, s.ReviewedByUserId, s.ReviewedAt, s.CreatedAt,
            submittedByUsername ?? s.SubmittedByUser?.Username);
}

public record UserCreatedGameResponse(
    int Id, int UserId, string Title, string Description, string CoverUrl, string Developer, string Publisher, DateTime? ReleaseDate)
{
    public static UserCreatedGameResponse From(UserCreatedGame x) =>
        new(x.Id, x.UserId, x.Title, x.Description, x.CoverUrl, x.Developer, x.Publisher, x.ReleaseDate);
}
