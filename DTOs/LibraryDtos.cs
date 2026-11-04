using System.ComponentModel.DataAnnotations;

namespace GameNest_BackEnd.DTOs;

public class AddLibraryGameDto { [RegularExpression("^(Planned|Playing|Completed|Dropped|OnHold)$")] public string Status { get; set; } = "Planned"; public bool Purchased { get; set; } }
public class UpdateLibraryGameDto { [Required, RegularExpression("^(Planned|Playing|Completed|Dropped|OnHold)$")] public string Status { get; set; } = "Planned"; public bool Purchased { get; set; } }
