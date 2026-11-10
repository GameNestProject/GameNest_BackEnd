namespace GameNest_BackEnd.Models;

/// <summary>
/// An external identity (e.g. a Google account) linked to a GameNest user.
/// <see cref="ProviderSubject"/> is the provider's stable account id (Google: the <c>sub</c> claim),
/// never the email address, which can change. Provider + ProviderSubject is unique, and a user
/// can have at most one identity per provider.
/// </summary>
public class ExternalLogin
{
    public const string GoogleProvider = "Google";

    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public string Provider { get; set; } = string.Empty;

    public string ProviderSubject { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
