using GameNest_BackEnd.Data;
using GameNest_BackEnd.DTOs;
using GameNest_BackEnd.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
namespace GameNest_BackEnd.Controllers;
[ApiController,Authorize,Route("api/[controller]")]
public class UserGamesController(GameNestDbContext context):ControllerBase
{
 private int UserId=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
 [HttpGet]public async Task<IActionResult> Get()=>Ok((await context.UserCreatedGames.AsNoTracking().Where(x=>x.UserId==UserId).ToListAsync()).Select(UserCreatedGameResponse.From));
 [HttpGet("{id:int}")]public async Task<IActionResult> Get(int id){var x=await context.UserCreatedGames.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.UserId==UserId);return x is null?NotFound():Ok(UserCreatedGameResponse.From(x));}
 [HttpPost]public async Task<IActionResult> Create(UserCreatedGameDto dto){var x=Apply(new UserCreatedGame{UserId=UserId},dto);context.UserCreatedGames.Add(x);await context.SaveChangesAsync();return CreatedAtAction(nameof(Get),new{id=x.Id},UserCreatedGameResponse.From(x));}
 [HttpPut("{id:int}")]public async Task<IActionResult> Update(int id,UserCreatedGameDto dto){var x=await context.UserCreatedGames.SingleOrDefaultAsync(x=>x.Id==id&&x.UserId==UserId);if(x is null)return NotFound();Apply(x,dto);await context.SaveChangesAsync();return Ok(UserCreatedGameResponse.From(x));}
 [HttpDelete("{id:int}")]public async Task<IActionResult> Delete(int id){var x=await context.UserCreatedGames.SingleOrDefaultAsync(x=>x.Id==id&&x.UserId==UserId);if(x is null)return NotFound();context.UserCreatedGames.Remove(x);await context.SaveChangesAsync();return NoContent();}
 private static UserCreatedGame Apply(UserCreatedGame x,UserCreatedGameDto d){x.Title=d.Title.Trim();x.Description=d.Description;x.CoverUrl=d.CoverUrl;x.Developer=d.Developer;x.Publisher=d.Publisher;x.ReleaseDate=d.ReleaseDate;return x;}
}
