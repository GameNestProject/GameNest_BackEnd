namespace GameNest_BackEnd.Models;

public class GameTitle
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public ICollection<Game> Games { get; set; } = new List<Game>();
}
