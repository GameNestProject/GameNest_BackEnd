namespace GameNest_BackEnd.Models;

public class UserGame
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int GameId { get; set; }

    public string Status { get; set; } = "Planned";

    public bool Purchased { get; set; }

    public User User { get; set; } = null!;

    public Game Game { get; set; } = null!;
}
