using GameNest_BackEnd.Data;
using GameNest_BackEnd.DTOs;
using GameNest_BackEnd.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
namespace GameNest_BackEnd.Controllers;
[ApiController,Authorize,Route("api/[controller]")]
public class SubmissionsController(GameNestDbContext context):ControllerBase
{
 private int UserId=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
 private bool IsAdmin=>User.IsInRole("Admin");
 [HttpGet] public async Task<IActionResult> Get(){var q=context.GameSubmissions.AsNoTracking();return Ok((await (IsAdmin?q:q.Where(x=>x.SubmittedByUserId==UserId)).ToListAsync()).Select(SubmissionResponse.From));}
 [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id){var x=await context.GameSubmissions.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&(IsAdmin||x.SubmittedByUserId==UserId));return x is null?NotFound():Ok(SubmissionResponse.From(x));}
 [HttpPost] public async Task<IActionResult> Create(SubmissionDto dto){var invalid=await Validate(dto);if(invalid is not null)return invalid;var x=Apply(new GameSubmission{SubmittedByUserId=UserId},dto);context.GameSubmissions.Add(x);await context.SaveChangesAsync();return CreatedAtAction(nameof(Get),new{id=x.Id},SubmissionResponse.From(x));}
 [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id,SubmissionDto dto){var x=await context.GameSubmissions.SingleOrDefaultAsync(x=>x.Id==id&&x.SubmittedByUserId==UserId);if(x is null)return NotFound();if(x.Status!="Pending")return Conflict(new {code="submission_already_reviewed"});var invalid=await Validate(dto);if(invalid is not null)return invalid;Apply(x,dto);await context.SaveChangesAsync();return Ok(SubmissionResponse.From(x));}
 [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id){var x=await context.GameSubmissions.SingleOrDefaultAsync(x=>x.Id==id&&x.SubmittedByUserId==UserId);if(x is null)return NotFound();if(x.Status!="Pending")return Conflict(new {code="submission_already_reviewed"});context.GameSubmissions.Remove(x);await context.SaveChangesAsync();return NoContent();}
 [Authorize(Roles="Admin"),HttpPut("{id:int}/approve")] public async Task<IActionResult> Approve(int id){await using var transaction=await context.Database.BeginTransactionAsync(HttpContext.RequestAborted);var x=await context.GameSubmissions.SingleOrDefaultAsync(x=>x.Id==id);if(x is null)return NotFound();if(x.Status!="Pending")return Conflict(new {code="submission_already_reviewed"});if(x.Type=="NewGame"){var title=await context.GameTitles.SingleOrDefaultAsync(t=>t.Title==x.Title);if(title is null){title=new GameTitle{Title=x.Title};context.GameTitles.Add(title);}context.Games.Add(ToGame(x,title));}else{var game=await context.Games.FindAsync([x.GameId],HttpContext.RequestAborted);if(game is null||game.IsArchived)return BadRequest(new {code="game_not_found"});ApplyGame(game,x);}x.Status="Approved";x.ReviewedByUserId=UserId;x.ReviewedAt=DateTime.UtcNow;await context.SaveChangesAsync();await transaction.CommitAsync(HttpContext.RequestAborted);return Ok(SubmissionResponse.From(x));}
 [Authorize(Roles="Admin"),HttpPut("{id:int}/reject")] public async Task<IActionResult> Reject(int id,RejectSubmissionDto dto){var x=await context.GameSubmissions.FindAsync([id],HttpContext.RequestAborted);if(x is null)return NotFound();if(x.Status!="Pending")return Conflict(new {code="submission_already_reviewed"});x.Status="Rejected";x.AdminComment=dto.AdminComment;x.ReviewedByUserId=UserId;x.ReviewedAt=DateTime.UtcNow;await context.SaveChangesAsync();return Ok(SubmissionResponse.From(x));}
 private async Task<IActionResult?> Validate(SubmissionDto dto){if(dto.Type=="NewGame"&&dto.GameId is not null)return BadRequest(new {code="new_game_must_not_have_game_id"});if(dto.Type=="Correction"&&(dto.GameId is null||!await context.Games.AnyAsync(g=>g.Id==dto.GameId&&!g.IsArchived)))return BadRequest(new {code="correction_requires_existing_game"});return null;}
 private static GameSubmission Apply(GameSubmission x,SubmissionDto d){x.Type=d.Type;x.GameId=d.GameId;x.Title=d.Title.Trim();x.Description=d.Description;x.CoverUrl=d.CoverUrl;x.TrailerUrl=d.TrailerUrl;x.Developer=d.Developer;x.Publisher=d.Publisher;x.ReleaseDate=d.ReleaseDate;return x;}
 private static Game ToGame(GameSubmission s,GameTitle t)=>new(){Title=s.Title,Description=s.Description,CoverUrl=s.CoverUrl,TrailerUrl=s.TrailerUrl,Developer=s.Developer,Publisher=s.Publisher,ReleaseDate=s.ReleaseDate,GameTitle=t};
 private static void ApplyGame(Game g,GameSubmission s){g.Title=s.Title;g.Description=s.Description;g.CoverUrl=s.CoverUrl;g.TrailerUrl=s.TrailerUrl;g.Developer=s.Developer;g.Publisher=s.Publisher;g.ReleaseDate=s.ReleaseDate;}
}
