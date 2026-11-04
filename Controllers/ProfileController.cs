using GameNest_BackEnd.Data;
using GameNest_BackEnd.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace GameNest_BackEnd.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProfileController(GameNestDbContext context) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [Authorize, HttpGet]
    public async Task<IActionResult> GetProfile() { var u=await context.Users.FindAsync([CurrentUserId], HttpContext.RequestAborted); return u is null ? Unauthorized() : Ok(new { u.Id,u.Username,u.Email,u.Role,u.EmailConfirmed }); }
    [Authorize, HttpPut]
    public async Task<IActionResult> UpdateProfile(UpdateProfileDto dto) { var name=dto.Username.Trim(); if(await context.Users.AnyAsync(u=>u.Id!=CurrentUserId&&u.Username==name)) return Conflict(new { code="username_already_exists" }); var u=await context.Users.FindAsync([CurrentUserId],HttpContext.RequestAborted); if(u is null)return Unauthorized();u.Username=name;await context.SaveChangesAsync();return Ok(new {u.Id,u.Username,u.Email,u.Role,u.EmailConfirmed}); }
    [HttpGet("{username}")]
    public async Task<IActionResult> GetPublicProfile(string username) { var u=await context.Users.AsNoTracking().SingleOrDefaultAsync(u=>u.Username==username&&u.IsProfilePublic);return u is null?NotFound():Ok(new {u.Id,u.Username}); }
    [Authorize, HttpGet("privacy")]
    public async Task<IActionResult> GetPrivacy(){var u=await context.Users.AsNoTracking().SingleOrDefaultAsync(u=>u.Id==CurrentUserId);return u is null?Unauthorized():Ok(new {u.IsProfilePublic,u.IsLibraryPublic});}
    [Authorize, HttpPut("privacy")]
    public async Task<IActionResult> UpdatePrivacy(UpdatePrivacyDto dto){var u=await context.Users.FindAsync([CurrentUserId],HttpContext.RequestAborted);if(u is null)return Unauthorized();u.IsProfilePublic=dto.IsProfilePublic;u.IsLibraryPublic=dto.IsLibraryPublic;await context.SaveChangesAsync();return Ok(new {u.IsProfilePublic,u.IsLibraryPublic});}
    [Authorize(Roles="Admin"), HttpGet("admin")]
    public IActionResult AdminTest()=>Ok("Ти маєш роль Admin.");
}
