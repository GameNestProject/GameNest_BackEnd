using GameNest_BackEnd.Data;
using GameNest_BackEnd.DTOs;
using GameNest_BackEnd.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
namespace GameNest_BackEnd.Controllers;
[ApiController,Authorize,Route("api/[controller]")]
public class LibraryController(GameNestDbContext context):ControllerBase
{
 private int UserId=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
 [HttpGet] public async Task<IActionResult> Get()=>Ok((await context.UserGames.AsNoTracking().Include(x=>x.Game).Where(x=>x.UserId==UserId).ToListAsync()).Select(x=>LibraryItemResponse.From(x)));
 [HttpPost("{gameId:int}")] public async Task<IActionResult> Add(int gameId,AddLibraryGameDto dto){var game=await context.Games.AsNoTracking().FirstOrDefaultAsync(g=>g.Id==gameId&&!g.IsArchived);if(game is null)return NotFound(new {code="game_not_found"});if(await context.UserGames.AnyAsync(x=>x.UserId==UserId&&x.GameId==gameId))return Conflict(new {code="game_already_in_library"});var item=new UserGame{UserId=UserId,GameId=gameId,Status=dto.Status,Purchased=dto.Purchased};context.UserGames.Add(item);await context.SaveChangesAsync();return CreatedAtAction(nameof(Get),new {gameId},LibraryItemResponse.From(item,game));}
 [HttpPut("{gameId:int}")] public async Task<IActionResult> Update(int gameId,UpdateLibraryGameDto dto){var item=await context.UserGames.Include(x=>x.Game).SingleOrDefaultAsync(x=>x.UserId==UserId&&x.GameId==gameId);if(item is null)return NotFound();item.Status=dto.Status;item.Purchased=dto.Purchased;await context.SaveChangesAsync();return Ok(LibraryItemResponse.From(item));}
 [HttpDelete("{gameId:int}")] public async Task<IActionResult> Delete(int gameId){var item=await context.UserGames.SingleOrDefaultAsync(x=>x.UserId==UserId&&x.GameId==gameId);if(item is null)return NotFound();context.UserGames.Remove(item);await context.SaveChangesAsync();return NoContent();}
}
