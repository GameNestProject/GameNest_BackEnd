using System.ComponentModel.DataAnnotations;

namespace GameNest_BackEnd.DTOs;

public class UpdateProfileDto { [Required, StringLength(100, MinimumLength = 3)] public string Username { get; set; } = string.Empty; }
public class UpdatePrivacyDto { public bool IsProfilePublic { get; set; } = true; public bool IsLibraryPublic { get; set; } = true; }
