using GameNest_BackEnd.Data;
using GameNest_BackEnd.DTOs.Auth;
using GameNest_BackEnd.Models;
using GameNest_BackEnd.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace GameNest_BackEnd.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(GameNestDbContext context, IEmailSender emailSender, IConfiguration configuration) : ControllerBase
{
    private static string HashConfirmationToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        var username = dto.Username.Trim();
        var email = dto.Email.Trim().ToLowerInvariant();
        if (await context.Users.AnyAsync(u => u.Username == username || u.Email == email, HttpContext.RequestAborted))
            return Conflict(new { code = "username_or_email_already_exists" });

        var confirmationToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var user = new User
        {
            Username = username, Email = email, PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password), Role = "User",
            EmailConfirmed = false, EmailConfirmationTokenHash = HashConfirmationToken(confirmationToken),
            EmailConfirmationTokenExpiresAt = DateTime.UtcNow.AddHours(24)
        };
        context.Users.Add(user);
        try { await context.SaveChangesAsync(HttpContext.RequestAborted); }
        catch (DbUpdateException) { return Conflict(new { code = "username_or_email_already_exists" }); }

        await emailSender.SendEmailConfirmationAsync(user.Email, confirmationToken, HttpContext.RequestAborted);
        return StatusCode(StatusCodes.Status201Created, new { code = "registration_success" });
    }

    [HttpPost("confirm-email")]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailDto dto)
    {
        var user = await context.Users.SingleOrDefaultAsync(u => u.Email == dto.Email.Trim().ToLower(), HttpContext.RequestAborted);
        if (user is null || user.EmailConfirmed || user.EmailConfirmationTokenExpiresAt <= DateTime.UtcNow || !TokenMatches(user.EmailConfirmationTokenHash, dto.Token))
            return BadRequest(new { code = "invalid_or_expired_confirmation_token" });
        user.EmailConfirmed = true;
        user.EmailConfirmationTokenHash = null;
        user.EmailConfirmationTokenExpiresAt = null;
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { code = "email_confirmed" });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        var user = await context.Users.SingleOrDefaultAsync(u => u.Email == dto.Email.Trim().ToLower(), HttpContext.RequestAborted);
        if (user is null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash)) return Unauthorized(new { code = "invalid_credentials" });
        if (!user.EmailConfirmed) return Unauthorized(new { code = "email_not_confirmed" });
        return Ok(new { token = CreateToken(user) });
    }

    private static bool TokenMatches(string? storedTokenHash, string token)
    {
        if (string.IsNullOrEmpty(storedTokenHash)) return false;
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(storedTokenHash), Convert.FromHexString(HashConfirmationToken(token))); }
        catch (FormatException) { return false; }
    }

    private string CreateToken(User user)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Username), new Claim(ClaimTypes.Role, user.Role) };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!));
        var token = new JwtSecurityToken(configuration["Jwt:Issuer"], configuration["Jwt:Audience"], claims, expires: DateTime.UtcNow.AddHours(1), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
