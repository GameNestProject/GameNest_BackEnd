using GameNest_BackEnd.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data;
using System.Net.Mail;

namespace GameNest_BackEnd.Cli;

/// <summary>
/// Local operator command that grants the <c>Admin</c> role to an existing, email-confirmed user:
/// <code>dotnet run --no-launch-profile -- admin promote --email user@example.com [--additional]</code>
/// It runs instead of the web server, uses <c>ConnectionStrings:DefaultConnection</c> (environment variable
/// <c>ConnectionStrings__DefaultConnection</c>) and never changes passwords or other user data.
/// There is deliberately no HTTP endpoint for this.
/// </summary>
public static class AdminCommand
{
    public const int Success = 0;
    public const int UsageError = 1;
    public const int UserNotFound = 2;
    public const int EmailNotConfirmed = 3;
    public const int Aborted = 4;
    public const int AdminAlreadyExists = 5;
    public const int ConfigurationError = 6;
    public const int Failed = 7;

    private const string AdminRole = "Admin";
    private const string UserRole = "User";

    public const string Usage = """
        Usage:
          dotnet run --no-launch-profile -- admin promote --email <email> [--additional]

        Grants the Admin role to an existing user whose email address is confirmed.
        The database comes from ConnectionStrings__DefaultConnection.
          --email <email>   exact email address of the registered user (required)
          --additional      allow promotion although another administrator already exists
        You will be asked to type the user's username to confirm.
        """;

    public sealed record PromoteRequest(string Email, bool AllowAdditional);

    public static bool IsAdminCommand(string[] args) => args.Length > 0 && args[0] == "admin";

    /// <summary>Parses the arguments strictly. Returns null (after writing the reason) for anything unexpected.</summary>
    public static PromoteRequest? Parse(string[] args, TextWriter error)
    {
        if (args.Length < 2 || args[0] != "admin" || args[1] != "promote")
        {
            error.WriteLine(args.Length >= 2 && args[0] == "admin" ? $"Unknown admin command '{args[1]}'." : "Missing admin command.");
            error.WriteLine(Usage);
            return null;
        }

        string? email = null;
        var allowAdditional = false;
        for (var i = 2; i < args.Length; i++)
        {
            var arg = args[i];
            string? value = null;
            if (arg == "--email")
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                {
                    error.WriteLine("--email requires a value.");
                    return null;
                }
                value = args[++i];
            }
            else if (arg.StartsWith("--email=", StringComparison.Ordinal))
            {
                value = arg["--email=".Length..];
            }
            else if (arg == "--additional")
            {
                allowAdditional = true;
                continue;
            }
            else
            {
                error.WriteLine($"Unknown argument '{arg}'.");
                error.WriteLine(Usage);
                return null;
            }

            if (email is not null)
            {
                error.WriteLine("--email may be given only once.");
                return null;
            }
            email = value;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            error.WriteLine("--email is required.");
            error.WriteLine(Usage);
            return null;
        }

        // Exactly one plain address: rejects "Name <a@b>", lists, wildcards in the domain and other surprises.
        var trimmed = email.Trim();
        if (!MailAddress.TryCreate(trimmed, out var address) || address.Address != trimmed || trimmed.Contains(',') || trimmed.Contains('*'))
        {
            error.WriteLine($"'{email}' is not a single valid email address.");
            return null;
        }

        // Same normalization as registration (AuthController.NormalizeEmail).
        return new PromoteRequest(trimmed.ToLowerInvariant(), allowAdditional);
    }

    /// <summary>Entry point used by Program.cs: parses arguments, connects to PostgreSQL and runs the promotion.</summary>
    public static async Task<int> RunAsync(string[] args, IConfiguration configuration, TextReader input, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        if (args is ["admin"] or ["admin", "--help" or "-h" or "help"])
        {
            output.WriteLine(Usage);
            return args.Length == 1 ? UsageError : Success;
        }

        var request = Parse(args, error);
        if (request is null) return UsageError;

        var connectionString = configuration["ConnectionStrings:DefaultConnection"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            error.WriteLine("ConnectionStrings__DefaultConnection is not set. Set it in this shell session (see README) and run the command again.");
            return ConfigurationError;
        }

        string target;
        try
        {
            var csb = new NpgsqlConnectionStringBuilder(connectionString);
            target = $"host={csb.Host}, port={csb.Port}, database={csb.Database}, user={csb.Username}";
        }
        catch (ArgumentException)
        {
            error.WriteLine("ConnectionStrings__DefaultConnection is not a valid Npgsql connection string (use Host=...;Database=...;Username=...;Password=...).");
            return ConfigurationError;
        }

        var options = new DbContextOptionsBuilder<GameNestDbContext>().UseNpgsql(connectionString).Options;
        try
        {
            await using var db = new GameNestDbContext(options);
            return await PromoteAsync(db, request, target, input, output, error, cancellationToken);
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or DbUpdateException)
        {
            // The message describes the connection/database problem; it does not contain the password.
            error.WriteLine($"Database error: {ex.GetBaseException().Message}");
            error.WriteLine("Nothing was changed.");
            return Failed;
        }
    }

    /// <summary>Validates the user, asks for confirmation and grants the Admin role. The database is changed only on success.</summary>
    public static async Task<int> PromoteAsync(GameNestDbContext db, PromoteRequest request, string target, TextReader input, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Email == request.Email)
            .Select(u => new { u.Id, u.Username, u.Email, u.Role, u.EmailConfirmed })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            error.WriteLine($"No user with email '{request.Email}' exists in {target}. Nothing was changed.");
            return UserNotFound;
        }

        if (user.Role == AdminRole)
        {
            output.WriteLine($"User '{user.Username}' ({user.Email}, id {user.Id}) is already an administrator. Nothing was changed.");
            return Success;
        }

        if (!user.EmailConfirmed)
        {
            error.WriteLine($"The email address of user '{user.Username}' ({user.Email}) is not confirmed. Confirm it first. Nothing was changed.");
            return EmailNotConfirmed;
        }

        if (user.Role != UserRole)
        {
            error.WriteLine($"User '{user.Username}' has the unexpected role '{user.Role}'. Nothing was changed.");
            return Failed;
        }

        var adminCount = await db.Users.CountAsync(u => u.Role == AdminRole, cancellationToken);
        if (adminCount > 0 && !request.AllowAdditional)
        {
            error.WriteLine($"{adminCount} administrator(s) already exist. To add another one deliberately, repeat the command with --additional. Nothing was changed.");
            return AdminAlreadyExists;
        }

        output.WriteLine("About to grant the Admin role:");
        output.WriteLine($"  database : {target}");
        output.WriteLine($"  user id  : {user.Id}");
        output.WriteLine($"  username : {user.Username}");
        output.WriteLine($"  email    : {user.Email}");
        output.WriteLine($"  role     : {user.Role} -> {AdminRole}");
        if (adminCount > 0) output.WriteLine($"  note     : {adminCount} administrator(s) already exist");
        output.Write($"Type the username '{user.Username}' to confirm (anything else aborts): ");
        output.Flush();

        var answer = input.ReadLine();
        if (answer is null || answer.Trim() != user.Username)
        {
            output.WriteLine();
            error.WriteLine("Confirmation did not match. Aborted; nothing was changed.");
            return Aborted;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!request.AllowAdditional && await db.Users.AnyAsync(u => u.Role == AdminRole, cancellationToken))
        {
            error.WriteLine("Another administrator was created in the meantime. Nothing was changed.");
            return AdminAlreadyExists;
        }

        // Conditional update: only the confirmed user with this id and the User role, nothing else (password untouched).
        var updated = await db.Users
            .Where(u => u.Id == user.Id && u.Email == request.Email && u.EmailConfirmed && u.Role == UserRole)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, AdminRole), cancellationToken);
        if (updated != 1)
        {
            error.WriteLine("The user changed while waiting for confirmation. Nothing was changed; run the command again.");
            return Failed;
        }
        await transaction.CommitAsync(cancellationToken);

        output.WriteLine($"Done: user '{user.Username}' ({user.Email}, id {user.Id}) is now an administrator.");
        output.WriteLine("Existing tokens keep the old role until they expire (up to 1 hour): the user must log in again.");
        return Success;
    }
}
