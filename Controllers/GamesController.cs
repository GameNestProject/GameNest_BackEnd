using GameNest_BackEnd.Data;
using GameNest_BackEnd.DTOs;
using GameNest_BackEnd.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace GameNest_BackEnd.Controllers;
[ApiController,Route("api/[controller]")]
public class GamesController(GameNestDbContext context):ControllerBase
{
 [HttpGet] public async Task<IActionResult> GetGames()=>Ok(await context.Games.AsNoTracking().Where(g=>!g.IsArchived).ToListAsync());
 [HttpGet("{id:int}")] public async Task<IActionResult> GetGameById(int id){var g=await context.Games.AsNoTracking().FirstOrDefaultAsync(g=>g.Id==id&&!g.IsArchived);return g is null?NotFound():Ok(g);}
 [Authorize(Roles="Admin"),HttpPost] public async Task<IActionResult> CreateGame(CreateGameDto dto){var t=await ResolveTitle(dto);if(t is null)return BadRequest(new {code="game_title_not_found"});var g=Apply(new Game(),dto);g.GameTitle=t;context.Games.Add(g);await context.SaveChangesAsync();return CreatedAtAction(nameof(GetGameById),new{id=g.Id},g);}
 [Authorize(Roles="Admin"),HttpPut("{id:int}")] public async Task<IActionResult> UpdateGame(int id,CreateGameDto dto){var g=await context.Games.FirstOrDefaultAsync(g=>g.Id==id&&!g.IsArchived);if(g is null)return NotFound();var t=await ResolveTitle(dto);if(t is null)return BadRequest(new {code="game_title_not_found"});Apply(g,dto);g.GameTitle=t;await context.SaveChangesAsync();return Ok(g);}
 [Authorize(Roles="Admin"),HttpDelete("{id:int}")] public async Task<IActionResult> ArchiveGame(int id){var g=await context.Games.FindAsync([id],HttpContext.RequestAborted);if(g is null||g.IsArchived)return NotFound();g.IsArchived=true;await context.SaveChangesAsync();return NoContent();}
 private async Task<GameTitle?> ResolveTitle(CreateGameDto dto){if(dto.GameTitleId is int id)return await context.GameTitles.FindAsync([id],HttpContext.RequestAborted);var text=dto.Title.Trim();var found=await context.GameTitles.SingleOrDefaultAsync(t=>t.Title==text);if(found is not null)return found;var created=new GameTitle{Title=text};context.GameTitles.Add(created);return created;}
 private static Game Apply(Game g,CreateGameDto d){g.Title=d.Title.Trim();g.Description=d.Description;g.CoverUrl=d.CoverUrl;g.TrailerUrl=d.TrailerUrl;g.Developer=d.Developer;g.Publisher=d.Publisher;g.ReleaseDate=d.ReleaseDate;return g;}
}
