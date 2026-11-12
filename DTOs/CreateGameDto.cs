using System.ComponentModel.DataAnnotations;

namespace GameNest_BackEnd.DTOs;

public class CreateGameDto
{
    [Required, StringLength(GameFields.TitleMaxLength)] public string Title { get; set; } = string.Empty;

    [StringLength(GameFields.DescriptionMaxLength)] public string? Description { get; set; } = string.Empty;

    [HttpsUrl] public string? CoverUrl { get; set; } = string.Empty;

    [HttpsUrl] public string? TrailerUrl { get; set; } = string.Empty;

    [StringLength(GameFields.NameMaxLength)] public string? Developer { get; set; } = string.Empty;

    [StringLength(GameFields.NameMaxLength)] public string? Publisher { get; set; } = string.Empty;

    [ReleaseDate] public DateTime? ReleaseDate { get; set; }
    public int? GameTitleId { get; set; }
}
