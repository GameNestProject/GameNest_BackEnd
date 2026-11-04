using System.ComponentModel.DataAnnotations;

namespace GameNest_BackEnd.DTOs;

public class SubmissionDto
{
    [Required, RegularExpression("^(NewGame|Correction)$")] public string Type { get; set; } = "NewGame";
    public int? GameId { get; set; }
    [Required, StringLength(200)] public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CoverUrl { get; set; } = string.Empty;
    public string TrailerUrl { get; set; } = string.Empty;
    public string Developer { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public DateTime? ReleaseDate { get; set; }
}
public class RejectSubmissionDto { [Required, StringLength(2000)] public string AdminComment { get; set; } = string.Empty; }
