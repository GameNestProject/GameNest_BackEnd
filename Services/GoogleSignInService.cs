using GameNest_BackEnd.Data;
using GameNest_BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace GameNest_BackEnd.Services;

public enum GoogleSignInOutcome
{
    /// <summary>The Google identity was already linked; the user signs in.</summary>
    SignedIn,
    /// <summary>A new user was created with the Google identity.</summary>
    Created,
    /// <summary>The Google identity was linked to an existing, confirmed account with the same email.</summary>
    Linked,
    /// <summary>An account with this email exists but its email is not confirmed: not linked automatically.</summary>
    EmailNotConfirmed,
    /// <summary>The account with this email is already linked to a different Google identity.</summary>
    IdentityConflict,
    /// <summary>Concurrent requests kept conflicting; the client may retry.</summary>
    Busy
}

public sealed record GoogleSignInResult(GoogleSignInOutcome Outcome, User? User);

/// <summary>
/// Finds, links or creates the GameNest user for a verified Google identity.
/// The user is found by the Google <c>sub</c> first, never by email alone. Roles are never taken from Google:
/// new users always get <c>User</c>, existing users keep their role. Concurrent requests are handled by the unique
/// indexes (Provider+ProviderSubject, UserId+Provider, Email, Username) and a bounded retry.
/// </summary>
public class GoogleSignInService(GameNestDbContext context, ILogger<GoogleSignInService> logger)
{
    private const int MaxAttempts = 3;
    private const int MaxUsernameLength = 30;

    public async Task<GoogleSignInResult> SignInAsync(GoogleIdentity identity, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            context.ChangeTracker.Clear();

            var login = await context.ExternalLogins.Include(l => l.User)
                .SingleOrDefaultAsync(l => l.Provider == ExternalLogin.GoogleProvider && l.ProviderSubject == identity.Subject, cancellationToken);
            if (login is not null)
                return new GoogleSignInResult(GoogleSignInOutcome.SignedIn, login.User);

            var existing = await context.Users.Include(u => u.ExternalLogins)
                .SingleOrDefaultAsync(u => u.Email == identity.Email, cancellationToken);
            try
            {
                if (existing is not null)
                {
                    if (existing.ExternalLogins.Any(l => l.Provider == ExternalLogin.GoogleProvider))
                        return new GoogleSignInResult(GoogleSignInOutcome.IdentityConflict, null);
                    if (!existing.EmailConfirmed)
                        return new GoogleSignInResult(GoogleSignInOutcome.EmailNotConfirmed, null);

                    existing.ExternalLogins.Add(NewGoogleLogin(identity));
                    existing.AvatarUrl ??= identity.PictureUrl;
                    await context.SaveChangesAsync(cancellationToken);
                    logger.LogInformation("Google identity linked to existing user {UserId}.", existing.Id);
                    return new GoogleSignInResult(GoogleSignInOutcome.Linked, existing);
                }

                var user = new User
                {
                    Username = await GenerateUsernameAsync(identity.Name, cancellationToken),
                    Email = identity.Email,
                    EmailConfirmed = true, // verified by Google (email_verified = true)
                    PasswordHash = null,   // no password: this account signs in with Google only
                    Role = "User",
                    AvatarUrl = identity.PictureUrl
                };
                user.ExternalLogins.Add(NewGoogleLogin(identity));
                context.Users.Add(user);
                await context.SaveChangesAsync(cancellationToken);
                logger.LogInformation("New user {UserId} created with Google sign-in.", user.Id);
                return new GoogleSignInResult(GoogleSignInOutcome.Created, user);
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                // A concurrent request created the same login, email or username first: start over with fresh data.
                logger.LogInformation("Google sign-in hit a uniqueness conflict (attempt {Attempt}); retrying.", attempt);
            }
            catch (DbUpdateException)
            {
                logger.LogWarning("Google sign-in kept conflicting after {Attempts} attempts.", MaxAttempts);
            }
        }

        context.ChangeTracker.Clear();
        return new GoogleSignInResult(GoogleSignInOutcome.Busy, null);
    }

    private static ExternalLogin NewGoogleLogin(GoogleIdentity identity) =>
        new() { Provider = ExternalLogin.GoogleProvider, ProviderSubject = identity.Subject, CreatedAt = DateTime.UtcNow };

    /// <summary>Public username from the Google name (never from the email). Collisions get a random numeric suffix.</summary>
    private async Task<string> GenerateUsernameAsync(string? name, CancellationToken cancellationToken)
    {
        var baseName = BaseUsername(name);
        if (!await context.Users.AnyAsync(u => u.Username == baseName, cancellationToken))
            return baseName;

        for (var i = 0; i < 10; i++)
        {
            var candidate = $"{baseName[..Math.Min(baseName.Length, MaxUsernameLength - 5)]}_{RandomNumberGenerator.GetInt32(1000, 10000)}";
            if (!await context.Users.AnyAsync(u => u.Username == candidate, cancellationToken))
                return candidate;
        }
        return $"{baseName[..Math.Min(baseName.Length, MaxUsernameLength - 9)]}_{Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant()}";
    }

    /// <summary>Letters and digits are kept (any script); spaces, dots and dashes become '_'; everything else is dropped.</summary>
    public static string BaseUsername(string? name)
    {
        var builder = new StringBuilder();
        foreach (var c in (name ?? string.Empty).Normalize(NormalizationForm.FormC))
        {
            if (char.IsLetterOrDigit(c)) builder.Append(c);
            else if ((c is ' ' or '.' or '-' or '_') && builder.Length > 0 && builder[^1] != '_') builder.Append('_');
            if (builder.Length >= MaxUsernameLength) break;
        }
        var result = builder.ToString().Trim('_');
        return result.Length >= 3 ? result : "player";
    }
}
