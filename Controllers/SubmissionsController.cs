using GameNest_BackEnd.Data;
using GameNest_BackEnd.DTOs;
using GameNest_BackEnd.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
namespace GameNest_BackEnd.Controllers;
[ApiController,Authorize,Route("api/[controller]")]
public class SubmissionsController(GameNestDbContext context):ControllerBase
{
 private static readonly string[] Statuses=["Pending","Approved","Rejected"];
 private int UserId=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
 private bool IsAdmin=>User.IsInRole("Admin");

 /// <summary>Admins: all submissions; users: their own. Optional ?status=Pending|Approved|Rejected. Newest first.</summary>
 [HttpGet] public async Task<IActionResult> Get([FromQuery] string? status=null)
 {
  if(!TryParseStatus(status,out var filter))return InvalidStatus();
  var q=context.GameSubmissions.AsNoTracking();
  return Ok(await List(IsAdmin?q:q.Where(x=>x.SubmittedByUserId==UserId),filter));
 }

 /// <summary>The caller's own submissions (also for admins). Optional ?status=. Newest first.</summary>
 [HttpGet("mine")] public async Task<IActionResult> Mine([FromQuery] string? status=null)
 {
  if(!TryParseStatus(status,out var filter))return InvalidStatus();
  return Ok(await List(context.GameSubmissions.AsNoTracking().Where(x=>x.SubmittedByUserId==UserId),filter));
 }

 [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id){var x=await context.GameSubmissions.AsNoTracking().Include(x=>x.SubmittedByUser).SingleOrDefaultAsync(x=>x.Id==id&&(IsAdmin||x.SubmittedByUserId==UserId));return x is null?NotFound():Ok(SubmissionResponse.From(x));}

 [HttpPost] public async Task<IActionResult> Create(SubmissionDto dto)
 {
  var invalid=await Validate(dto);if(invalid is not null)return invalid;
  // Owner, status and review fields come from the server, never from the request.
  var x=Apply(new GameSubmission{SubmittedByUserId=UserId,Status="Pending",CreatedAt=DateTime.UtcNow},dto);
  context.GameSubmissions.Add(x);await context.SaveChangesAsync();
  await context.Entry(x).Reference(s=>s.SubmittedByUser).LoadAsync();
  return CreatedAtAction(nameof(Get),new{id=x.Id},SubmissionResponse.From(x));
 }

 /// <summary>
 /// Edits a pending submission (owner, or an admin). The write is one conditional UPDATE
 /// (… WHERE Id AND owner/admin AND Status = 'Pending'), so a submission approved or rejected concurrently is never changed.
 /// Only the user-editable fields are written; owner, status and review fields are never touched.
 /// The UPDATE and the read of the response share one transaction: the row lock taken by the UPDATE is held until
 /// the response has been read, so a concurrent delete (or approval) waits instead of removing the row in between.
 /// </summary>
 [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id,SubmissionDto dto)
 {
  var ct=HttpContext.RequestAborted;var userId=UserId;var isAdmin=IsAdmin;
  // Early answers in the established order (404, 409, 400); the UPDATE below re-checks atomically.
  var current=await context.GameSubmissions.AsNoTracking().Where(s=>s.Id==id&&(isAdmin||s.SubmittedByUserId==userId)).Select(s=>s.Status).SingleOrDefaultAsync(ct);
  if(current is null)return NotFound();
  if(current!="Pending")return Conflict(new {code="submission_already_reviewed"});
  var invalid=await Validate(dto);if(invalid is not null)return invalid;
  var v=Apply(new GameSubmission(),dto); // normalized values
  await using var transaction=await context.Database.BeginTransactionAsync(ct);
  var updated=await context.GameSubmissions
   .Where(s=>s.Id==id&&(isAdmin||s.SubmittedByUserId==userId)&&s.Status=="Pending")
   .ExecuteUpdateAsync(u=>u.SetProperty(s=>s.Type,v.Type).SetProperty(s=>s.GameId,v.GameId).SetProperty(s=>s.Title,v.Title)
    .SetProperty(s=>s.Description,v.Description).SetProperty(s=>s.CoverUrl,v.CoverUrl).SetProperty(s=>s.TrailerUrl,v.TrailerUrl)
    .SetProperty(s=>s.Developer,v.Developer).SetProperty(s=>s.Publisher,v.Publisher).SetProperty(s=>s.ReleaseDate,v.ReleaseDate),ct);
  if(updated==0)return await NotPendingResult(id,s=>isAdmin||s.SubmittedByUserId==userId); // nothing written; rolled back on dispose
  var x=await context.GameSubmissions.AsNoTracking().Include(s=>s.SubmittedByUser).SingleAsync(s=>s.Id==id,ct);
  await transaction.CommitAsync(ct);
  return Ok(SubmissionResponse.From(x));
 }

 /// <summary>
 /// Deletes the caller's own pending submission with one conditional DELETE (… WHERE Id AND owner AND Status = 'Pending').
 /// With a concurrent approval, PostgreSQL serializes the two statements on the row: either the DELETE wins (the approval
 /// then finds no pending row and creates no game) or the approval wins (the DELETE then matches nothing → 409).
 /// </summary>
 [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id)
 {
  var userId=UserId;
  var deleted=await context.GameSubmissions.Where(s=>s.Id==id&&s.SubmittedByUserId==userId&&s.Status=="Pending").ExecuteDeleteAsync(HttpContext.RequestAborted);
  if(deleted==1)return NoContent();
  return await NotPendingResult(id,s=>s.SubmittedByUserId==userId);
 }

 /// <summary>
 /// Approves a pending submission atomically: the status is claimed with a conditional UPDATE (… WHERE Status = 'Pending')
 /// inside the same transaction that creates or updates the game, so concurrent or repeated approvals cannot create
 /// a second game. "NewGame" adds a Game (reusing the GameTitle with the same title) and records it in GameId.
 /// </summary>
 [Authorize(Roles="Admin"),HttpPut("{id:int}/approve")] public async Task<IActionResult> Approve(int id)
 {
  var ct=HttpContext.RequestAborted;
  try
  {
   await using var transaction=await context.Database.BeginTransactionAsync(ct);
   if(!await Claim(id,"Approved",null,ct))return await NotPendingResult(id);
   var x=await context.GameSubmissions.Include(s=>s.SubmittedByUser).SingleAsync(s=>s.Id==id,ct);
   if(x.Type=="NewGame")
   {
    var title=await context.GameTitles.SingleOrDefaultAsync(t=>t.Title==x.Title,ct);
    if(title is null){title=new GameTitle{Title=x.Title};context.GameTitles.Add(title);}
    var game=ToGame(x,title);context.Games.Add(game);
    await context.SaveChangesAsync(ct);
    x.GameId=game.Id;
   }
   else
   {
    var game=await context.Games.FindAsync([x.GameId],ct);
    // Not committed: the claim above is rolled back and the submission stays Pending.
    if(game is null||game.IsArchived)return BadRequest(new {code="game_not_found"});
    ApplyGame(game,x);
   }
   await context.SaveChangesAsync(ct);
   await transaction.CommitAsync(ct);
   return Ok(SubmissionResponse.From(x));
  }
  catch(DbUpdateException)
  {
   // e.g. a concurrent approval created the same GameTitle first; nothing was committed.
   return Conflict(new {code="concurrent_update"});
  }
 }

 /// <summary>Rejects a pending submission with an optional reason (shown to the submitter). Atomic like approval.</summary>
 [Authorize(Roles="Admin"),HttpPut("{id:int}/reject")] public async Task<IActionResult> Reject(int id,[FromBody(EmptyBodyBehavior=EmptyBodyBehavior.Allow)] RejectSubmissionDto? dto=null)
 {
  var reason=string.IsNullOrWhiteSpace(dto?.AdminComment)?null:dto!.AdminComment!.Trim();
  if(!await Claim(id,"Rejected",reason,HttpContext.RequestAborted))return await NotPendingResult(id);
  var x=await context.GameSubmissions.AsNoTracking().Include(s=>s.SubmittedByUser).SingleAsync(s=>s.Id==id);
  return Ok(SubmissionResponse.From(x));
 }

 /// <summary>Moves a submission out of Pending in one conditional UPDATE; false if it is missing or already reviewed.</summary>
 private async Task<bool> Claim(int id,string status,string? adminComment,CancellationToken ct)
 {
  var reviewer=UserId;var now=DateTime.UtcNow;
  var query=context.GameSubmissions.Where(s=>s.Id==id&&s.Status=="Pending");
  var updated=status=="Rejected"
   ?await query.ExecuteUpdateAsync(u=>u.SetProperty(s=>s.Status,status).SetProperty(s=>s.ReviewedByUserId,reviewer).SetProperty(s=>s.ReviewedAt,now).SetProperty(s=>s.AdminComment,adminComment),ct)
   :await query.ExecuteUpdateAsync(u=>u.SetProperty(s=>s.Status,status).SetProperty(s=>s.ReviewedByUserId,reviewer).SetProperty(s=>s.ReviewedAt,now),ct);
  return updated==1;
 }
 /// <summary>After a conditional write matched nothing: 409 if the row exists (and is visible to the caller), else 404.</summary>
 private async Task<IActionResult> NotPendingResult(int id,System.Linq.Expressions.Expression<Func<GameSubmission,bool>>? visible=null)
 {
  var q=context.GameSubmissions.Where(s=>s.Id==id);
  if(visible is not null)q=q.Where(visible);
  return await q.AnyAsync()?Conflict(new {code="submission_already_reviewed"}):NotFound();
 }

 private static bool TryParseStatus(string? value,out string? status)
 {
  status=null;
  if(string.IsNullOrWhiteSpace(value))return true;
  status=Statuses.FirstOrDefault(s=>string.Equals(s,value.Trim(),StringComparison.OrdinalIgnoreCase));
  return status is not null;
 }
 private BadRequestObjectResult InvalidStatus()=>BadRequest(new {code="invalid_status"});
 private static async Task<List<SubmissionResponse>> List(IQueryable<GameSubmission> q,string? status)
 {
  if(status is not null)q=q.Where(x=>x.Status==status);
  var rows=await q.OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).Select(x=>new{Submission=x,x.SubmittedByUser.Username}).ToListAsync();
  return rows.Select(r=>SubmissionResponse.From(r.Submission,r.Username)).ToList();
 }

 private async Task<IActionResult?> Validate(SubmissionDto dto){if(dto.Type=="NewGame"&&dto.GameId is not null)return BadRequest(new {code="new_game_must_not_have_game_id"});if(dto.Type=="Correction"&&(dto.GameId is null||!await context.Games.AnyAsync(g=>g.Id==dto.GameId&&!g.IsArchived)))return BadRequest(new {code="correction_requires_existing_game"});return null;}
 private static GameSubmission Apply(GameSubmission x,SubmissionDto d){x.Type=d.Type;x.GameId=d.GameId;x.Title=d.Title.Trim();x.Description=GameFields.Text(d.Description);x.CoverUrl=GameFields.Text(d.CoverUrl);x.TrailerUrl=GameFields.Text(d.TrailerUrl);x.Developer=GameFields.Text(d.Developer);x.Publisher=GameFields.Text(d.Publisher);x.ReleaseDate=GameFields.ReleaseDate(d.ReleaseDate);return x;}
 private static Game ToGame(GameSubmission s,GameTitle t)=>new(){Title=s.Title,Description=s.Description,CoverUrl=s.CoverUrl,TrailerUrl=s.TrailerUrl,Developer=s.Developer,Publisher=s.Publisher,ReleaseDate=s.ReleaseDate,GameTitle=t};
 private static void ApplyGame(Game g,GameSubmission s){g.Title=s.Title;g.Description=s.Description;g.CoverUrl=s.CoverUrl;g.TrailerUrl=s.TrailerUrl;g.Developer=s.Developer;g.Publisher=s.Publisher;g.ReleaseDate=s.ReleaseDate;}
}
