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
    /// <summary>The Google identity was linked to an existing, confirmed account with the same email (Gmail / Workspace only).</summary>
    Linked,
    /// <summary>An account with this email exists but its email is not confirmed: not linked automatically.</summary>
    EmailNotConfirmed,
    /// <summary>The account with this email is already linked to a different Google identity.</summary>
    IdentityConflict,
    /// <summary>An account with this email exists, but Google is not authoritative for the address (no Gmail, no Workspace <c>hd</c>): the owner must link explicitly.</summary>
    LinkRequired,
    /// <summary>Several legacy accounts share this email when case is ignored: nothing is linked automatically.</summary>
    AmbiguousEmail,
    /// <summary>Concurrent requests kept conflicting; the client may retry.</summary>
    Busy
}

public sealed record GoogleSignInResult(GoogleSignInOutcome Outcome, User? User);

public enum GoogleLinkOutcome
{
    /// <summary>The Google identity is now linked to the signed-in user.</summary>
    Linked,
    /// <summary>The Google identity was already linked to the signed-in user (no change).</summary>
    AlreadyLinked,
    /// <summary>The signed-in user no longer exists.</summary>
    UserNotFound,
    /// <summary>The password is missing or wrong.</summary>
    InvalidPassword,
    /// <summary>The account has no password (it signs in with Google only), so explicit linking does not apply.</summary>
    NoPassword,
    /// <summary>The signed-in user is already linked to a different Google account.</summary>
    UserHasOtherGoogleAccount,
    /// <summary>This Google account is already linked to another GameNest user.</summary>
    GoogleAccountLinkedToOtherUser
}

/// <summary>
/// Finds, links or creates the GameNest user for a verified Google identity.
/// The user is found by the Google <c>sub</c> first, never by email alone. An existing account is linked automatically
/// only when Google is authoritative for the address (Gmail, or Workspace with <c>hd</c>) and the account's email is
/// confirmed; otherwise the owner links explicitly (<see cref="LinkAsync"/>). A linked <c>sub</c> is never rebound.
/// Roles, passwords and email confirmation of existing users are never changed here; new users always get <c>User</c>.
/// Concurrent requests are handled by the unique indexes (Provider+ProviderSubject, UserId+Provider, Email, Username)
/// and a bounded retry.
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

            var login = await FindGoogleLoginAsync(identity.Subject, cancellationToken);
            if (login is not null)
                return new GoogleSignInResult(GoogleSignInOutcome.SignedIn, login.User);

            // Case-insensitive: accounts created before email normalization may contain upper-case letters.
            var matches = await context.Users.Include(u => u.ExternalLogins)
                .Where(u => u.Email.ToLower() == identity.Email)
                .Take(2)
                .ToListAsync(cancellationToken);
            if (matches.Count > 1)
                return new GoogleSignInResult(GoogleSignInOutcome.AmbiguousEmail, null);
            var existing = matches.SingleOrDefault();
            try
            {
                if (existing is not null)
                {
                    if (existing.ExternalLogins.Any(l => l.Provider == ExternalLogin.GoogleProvider))
                        return new GoogleSignInResult(GoogleSignInOutcome.IdentityConflict, null);
                    if (!existing.EmailConfirmed)
                        return new GoogleSignInResult(GoogleSignInOutcome.EmailNotConfirmed, null);
                    if (!identity.IsEmailAuthoritative)
                        return new GoogleSignInResult(GoogleSignInOutcome.LinkRequired, null);

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
                    // Confirmed only when Google is authoritative for the address (Gmail / Workspace). For other
                    // addresses Google sign-in still works (by sub), but GameNest does not treat the email as confirmed.
                    EmailConfirmed = identity.IsEmailAuthoritative,
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

    /// <summary>
    /// Explicit linking by the owner of an existing account: the caller is authenticated with a GameNest JWT and
    /// re-enters the account password, and proves control of the Google account with a verified ID token.
    /// The emails do not have to match. Role, password, email and email confirmation are not changed.
    /// </summary>
    public async Task<GoogleLinkOutcome> LinkAsync(int userId, string password, GoogleIdentity identity, CancellationToken cancellationToken = default)
    {
        context.ChangeTracker.Clear();

        var user = await context.Users.Include(u => u.ExternalLogins).SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null) return GoogleLinkOutcome.UserNotFound;
        if (user.PasswordHash is null) return GoogleLinkOutcome.NoPassword;
        if (string.IsNullOrEmpty(password) || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash)) return GoogleLinkOutcome.InvalidPassword;

        var login = await FindGoogleLoginAsync(identity.Subject, cancellationToken);
        if (login is not null)
            return login.UserId == userId ? GoogleLinkOutcome.AlreadyLinked : GoogleLinkOutcome.GoogleAccountLinkedToOtherUser;
        if (user.ExternalLogins.Any(l => l.Provider == ExternalLogin.GoogleProvider))
            return GoogleLinkOutcome.UserHasOtherGoogleAccount;

        user.ExternalLogins.Add(NewGoogleLogin(identity));
        user.AvatarUrl ??= identity.PictureUrl;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent request linked this Google account or this user first; report what is stored now.
            context.ChangeTracker.Clear();
            var current = await FindGoogleLoginAsync(identity.Subject, cancellationToken);
            if (current?.UserId == userId) return GoogleLinkOutcome.AlreadyLinked;
            return current is not null ? GoogleLinkOutcome.GoogleAccountLinkedToOtherUser : GoogleLinkOutcome.UserHasOtherGoogleAccount;
        }
        logger.LogInformation("Google identity explicitly linked to user {UserId}.", userId);
        return GoogleLinkOutcome.Linked;
    }

    private Task<ExternalLogin?> FindGoogleLoginAsync(string subject, CancellationToken cancellationToken) =>
        context.ExternalLogins.Include(l => l.User)
            .SingleOrDefaultAsync(l => l.Provider == ExternalLogin.GoogleProvider && l.ProviderSubject == subject, cancellationToken);

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
