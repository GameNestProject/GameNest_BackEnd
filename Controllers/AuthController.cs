using GameNest_BackEnd.Data;
using GameNest_BackEnd.DTOs.Auth;
using GameNest_BackEnd.Models;
using GameNest_BackEnd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace GameNest_BackEnd.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(GameNestDbContext context, IEmailSender emailSender, IConfiguration configuration, ILogger<AuthController> logger,
    IOptions<AuthFeatureOptions> authFeatures) : ControllerBase
{
    // Email/password registration and login can be switched off (Google-only MVP) without removing them.
    private IActionResult PasswordRegistrationDisabled() => StatusCode(StatusCodes.Status403Forbidden, new { code = "password_registration_disabled" });
    private IActionResult PasswordLoginDisabled() => StatusCode(StatusCodes.Status403Forbidden, new { code = "password_login_disabled" });

    private static readonly TimeSpan ConfirmationTokenLifetime = TimeSpan.FromHours(24);

    // Hash of a random secret nobody knows, created once per process with the same work factor as real
    // password hashes. Verifying against it for unknown emails keeps login timing uniform.
    private static readonly Lazy<string> DummyPasswordHash =
        new(() => BCrypt.Net.BCrypt.HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

    private static string HashConfirmationToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
    private static string CreateConfirmationToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static void SetConfirmationToken(User user, string token)
    {
        user.EmailConfirmationTokenHash = HashConfirmationToken(token);
        user.EmailConfirmationTokenExpiresAt = DateTime.UtcNow.Add(ConfirmationTokenLifetime);
    }

    [HttpPost("register"), EnableRateLimiting(AuthRateLimiting.AuthPolicy)]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        if (!authFeatures.Value.PasswordRegistrationEnabled) return PasswordRegistrationDisabled();
        var username = dto.Username.Trim();
        var email = NormalizeEmail(dto.Email);
        if (await context.Users.AnyAsync(u => u.Username == username || u.Email == email, HttpContext.RequestAborted))
            return Conflict(new { code = "username_or_email_already_exists" });

        var confirmationToken = CreateConfirmationToken();
        var user = new User
        {
            Username = username, Email = email, PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password), Role = "User",
            EmailConfirmed = false
        };
        SetConfirmationToken(user, confirmationToken);
        context.Users.Add(user);
        try { await context.SaveChangesAsync(HttpContext.RequestAborted); }
        catch (DbUpdateException) { return Conflict(new { code = "username_or_email_already_exists" }); }

        try
        {
            await emailSender.SendEmailConfirmationAsync(user.Email, confirmationToken, HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Without the email the account could never be confirmed, so do not keep it.
            logger.LogError(ex, "Confirmation email could not be sent; the new account was removed.");
            context.Users.Remove(user);
            await context.SaveChangesAsync(CancellationToken.None);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "email_service_unavailable" });
        }

        return StatusCode(StatusCodes.Status201Created, new { code = "registration_success" });
    }

    /// <summary>
    /// Issues a new confirmation token for an unconfirmed account. Always returns the same response,
    /// so the endpoint does not reveal whether an email address is registered or already confirmed.
    /// </summary>
    [HttpPost("resend-confirmation"), EnableRateLimiting(AuthRateLimiting.ResendConfirmationPolicy)]
    public async Task<IActionResult> ResendConfirmation(ResendConfirmationDto dto)
    {
        if (!authFeatures.Value.PasswordRegistrationEnabled) return PasswordRegistrationDisabled();
        var email = NormalizeEmail(dto.Email);
        var user = await context.Users.SingleOrDefaultAsync(u => u.Email == email, HttpContext.RequestAborted);
        if (user is { EmailConfirmed: false })
        {
            var confirmationToken = CreateConfirmationToken();
            try
            {
                // Send first: if delivery fails, the previous token stays valid.
                await emailSender.SendEmailConfirmationAsync(user.Email, confirmationToken, HttpContext.RequestAborted);
                SetConfirmationToken(user, confirmationToken);
                await context.SaveChangesAsync(HttpContext.RequestAborted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Confirmation email could not be resent.");
            }
        }

        return Accepted(new { code = "confirmation_resend_requested" });
    }

    [HttpPost("confirm-email"), EnableRateLimiting(AuthRateLimiting.AuthPolicy)]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailDto dto)
    {
        var email = NormalizeEmail(dto.Email);
        var user = await context.Users.SingleOrDefaultAsync(u => u.Email == email, HttpContext.RequestAborted);
        if (user is null || user.EmailConfirmed || user.EmailConfirmationTokenExpiresAt <= DateTime.UtcNow || !TokenMatches(user.EmailConfirmationTokenHash, dto.Token))
            return BadRequest(new { code = "invalid_or_expired_confirmation_token" });
        user.EmailConfirmed = true;
        user.EmailConfirmationTokenHash = null;
        user.EmailConfirmationTokenExpiresAt = null;
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { code = "email_confirmed" });
    }

    [HttpPost("login"), EnableRateLimiting(AuthRateLimiting.AuthPolicy)]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        if (!authFeatures.Value.PasswordLoginEnabled) return PasswordLoginDisabled();
        var email = NormalizeEmail(dto.Email);
        var user = await context.Users.SingleOrDefaultAsync(u => u.Email == email, HttpContext.RequestAborted);
        // Always run one BCrypt verification so that unknown emails take as long as wrong passwords.
        var passwordValid = BCrypt.Net.BCrypt.Verify(dto.Password, user?.PasswordHash ?? DummyPasswordHash.Value);
        if (user is null || !passwordValid) return Unauthorized(new { code = "invalid_credentials" });
        if (!user.EmailConfirmed) return Unauthorized(new { code = "email_not_confirmed" });
        return Ok(new { token = CreateToken(user) });
    }

    /// <summary>
    /// Sign-up and sign-in with a Google ID token (Google Identity Services <c>credential</c>). The token is verified
    /// server-side; email, name and picture are taken only from the verified token. Returns a GameNest JWT.
    /// </summary>
    [HttpPost("google"), EnableRateLimiting(AuthRateLimiting.AuthPolicy)]
    public async Task<IActionResult> Google(GoogleLoginDto dto, [FromServices] IGoogleTokenValidator googleTokenValidator, [FromServices] GoogleSignInService googleSignIn)
    {
        GoogleIdentity identity;
        try
        {
            identity = await googleTokenValidator.ValidateAsync(dto.IdToken, HttpContext.RequestAborted);
        }
        catch (GoogleTokenRejectedException ex)
        {
            return Unauthorized(new { code = ex.Code });
        }
        catch (GoogleAuthUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = ex.Code });
        }

        var result = await googleSignIn.SignInAsync(identity, HttpContext.RequestAborted);
        return result switch
        {
            { Outcome: GoogleSignInOutcome.EmailNotConfirmed } => Conflict(new { code = "email_registered_unconfirmed" }),
            { Outcome: GoogleSignInOutcome.IdentityConflict } => Conflict(new { code = "account_linked_to_another_google_account" }),
            { Outcome: GoogleSignInOutcome.LinkRequired } => Conflict(new { code = "account_link_required" }),
            { Outcome: GoogleSignInOutcome.AmbiguousEmail } => Conflict(new { code = "duplicate_accounts_for_email" }),
            { User: { } user } => Ok(new
            {
                token = CreateToken(user),
                isNewUser = result.Outcome == GoogleSignInOutcome.Created,
                user = new { user.Id, user.Username, user.Email, user.Role, user.AvatarUrl }
            }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "google_sign_in_busy" })
        };
    }

    /// <summary>
    /// Links a Google account to the signed-in user (recovery path for <c>account_link_required</c>). Requires the
    /// GameNest JWT, the account password and a Google ID token. Does not change role, password or email.
    /// </summary>
    [Authorize, HttpPost("google/link"), EnableRateLimiting(AuthRateLimiting.AuthPolicy)]
    public async Task<IActionResult> LinkGoogle(GoogleLinkDto dto, [FromServices] IGoogleTokenValidator googleTokenValidator, [FromServices] GoogleSignInService googleSignIn)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();

        GoogleIdentity identity;
        try
        {
            identity = await googleTokenValidator.ValidateAsync(dto.IdToken, HttpContext.RequestAborted);
        }
        catch (GoogleTokenRejectedException ex)
        {
            return Unauthorized(new { code = ex.Code });
        }
        catch (GoogleAuthUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = ex.Code });
        }

        return await googleSignIn.LinkAsync(userId, dto.Password, identity, HttpContext.RequestAborted) switch
        {
            GoogleLinkOutcome.Linked => Ok(new { code = "google_account_linked" }),
            GoogleLinkOutcome.AlreadyLinked => Ok(new { code = "google_account_already_linked" }),
            GoogleLinkOutcome.InvalidPassword => Unauthorized(new { code = "invalid_credentials" }),
            GoogleLinkOutcome.NoPassword => Conflict(new { code = "account_has_no_password" }),
            GoogleLinkOutcome.UserHasOtherGoogleAccount => Conflict(new { code = "account_linked_to_another_google_account" }),
            GoogleLinkOutcome.GoogleAccountLinkedToOtherUser => Conflict(new { code = "google_account_linked_to_another_user" }),
            _ => Unauthorized()
        };
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
