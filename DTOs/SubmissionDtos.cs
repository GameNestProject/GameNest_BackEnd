using System.ComponentModel.DataAnnotations;

namespace GameNest_BackEnd.DTOs;

/// <summary>
/// A game proposed by a user. The submitter, status and review fields are never taken from the request:
/// the server sets them (SubmittedByUserId from the JWT, Status = Pending).
/// </summary>
public class SubmissionDto
{
    [Required, RegularExpression("^(NewGame|Correction)$")] public string Type { get; set; } = "NewGame";
    public int? GameId { get; set; }
    [Required, StringLength(GameFields.TitleMaxLength)] public string Title { get; set; } = string.Empty;
    [StringLength(GameFields.DescriptionMaxLength)] public string? Description { get; set; } = string.Empty;
    [HttpsUrl] public string? CoverUrl { get; set; } = string.Empty;
    [HttpsUrl] public string? TrailerUrl { get; set; } = string.Empty;
    [StringLength(GameFields.NameMaxLength)] public string? Developer { get; set; } = string.Empty;
    [StringLength(GameFields.NameMaxLength)] public string? Publisher { get; set; } = string.Empty;
    [ReleaseDate] public DateTime? ReleaseDate { get; set; }
}

/// <summary>Optional reason shown to the submitter.</summary>
public class RejectSubmissionDto { [StringLength(GameFields.RejectionReasonMaxLength)] public string? AdminComment { get; set; } }
