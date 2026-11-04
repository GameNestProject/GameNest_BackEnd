using GameNest_BackEnd.Data;
using GameNest_BackEnd.DTOs.Auth;
using GameNest_BackEnd.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Security.Cryptography;
using GameNest_BackEnd.Services;

namespace GameNest_BackEnd.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly GameNestDbContext _context;
    private readonly IEmailSender _emailSender;

    public AuthController(GameNestDbContext context, IEmailSender emailSender)
    {
        _context = context;
        _emailSender = emailSender;
    }
    private static string HashConfirmationToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private string CreateToken(User user)
{
    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Role, user.Role)
    };

    var key = new SymmetricSecurityKey(
        Encoding.UTF8.GetBytes(
            HttpContext.RequestServices
                .GetRequiredService<IConfiguration>()["Jwt:Key"]!
        )
    );

    var credentials = new SigningCredentials(
        key,
        SecurityAlgorithms.HmacSha256
    );

    var token = new JwtSecurityToken(
        issuer: HttpContext.RequestServices
            .GetRequiredService<IConfiguration>()["Jwt:Issuer"],

        audience: HttpContext.RequestServices
            .GetRequiredService<IConfiguration>()["Jwt:Audience"],

        claims: claims,

        expires: DateTime.UtcNow.AddHours(1),

        signingCredentials: credentials
    );

    return new JwtSecurityTokenHandler().WriteToken(token);
}

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        // Перевіряємо, чи Username або Email вже зайняті
        var existingUser = await _context.Users
            .FirstOrDefaultAsync(u =>
                u.Username == dto.Username ||
                u.Email == dto.Email);

        if (existingUser != null)
{
            return Conflict(new
            {
                code = "username_or_email_already_exists"
            });
        }

        // Хешуємо пароль
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

        // Створюємо користувача
        var confirmationToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var user = new User
        {
            Username = dto.Username,
            Email = dto.Email,
            PasswordHash = passwordHash,
            Role = "User",
            EmailConfirmed = false,
            EmailConfirmationTokenHash = HashConfirmationToken(confirmationToken),
            EmailConfirmationTokenExpiresAt = DateTime.UtcNow.AddHours(24)
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        await _emailSender.SendEmailConfirmationAsync(user.Email, confirmationToken, HttpContext.RequestAborted);

        return Ok(new
{
            code = "registration_success"
        });
    }
    [HttpPost("confirm-email")]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailDto dto)
    {
        var user = await _context.Users.SingleOrDefaultAsync(u => u.Email == dto.Email);
        if (user is null || user.EmailConfirmed || user.EmailConfirmationTokenExpiresAt <= DateTime.UtcNow ||
            user.EmailConfirmationTokenHash is null || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(user.EmailConfirmationTokenHash), Convert.FromHexString(HashConfirmationToken(dto.Token))))
        {
            return BadRequest(new { code = "invalid_or_expired_confirmation_token" });
        }

        user.EmailConfirmed = true;
        user.EmailConfirmationTokenHash = null;
        user.EmailConfirmationTokenExpiresAt = null;
        await _context.SaveChangesAsync();
        return Ok(new { code = "email_confirmed" });
    }
    [HttpPost("login")]
public async Task<IActionResult> Login(LoginDto dto)
{
    var user = await _context.Users
        .FirstOrDefaultAsync(u => u.Email == dto.Email);

    if (user == null)
    {
        return Unauthorized("username_or_email_already_exists");
    }

    var passwordIsValid = BCrypt.Net.BCrypt.Verify(
        dto.Password,
        user.PasswordHash
    );

    if (!passwordIsValid)
    {
        return Unauthorized("username_or_email_already_exists");
    }

    if (!user.EmailConfirmed)
    {
        return Unauthorized(new { code = "email_not_confirmed" });
    }

    var token = CreateToken(user);

    return Ok(new
    {
        token = token
    });
    }
}
