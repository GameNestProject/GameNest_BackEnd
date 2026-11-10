using GameNest_BackEnd.Data;
using GameNest_BackEnd.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace GameNest_BackEnd.Controllers;
[ApiController,Route("api/[controller]")]
public class GameTitlesController(GameNestDbContext context):ControllerBase
{ [HttpGet("{id:int}/versions")] public async Task<IActionResult> GetVersions(int id){if(!await context.GameTitles.AnyAsync(t=>t.Id==id))return NotFound();return Ok((await context.Games.AsNoTracking().Where(g=>g.GameTitleId==id&&!g.IsArchived).ToListAsync()).Select(GameResponse.From));} }
