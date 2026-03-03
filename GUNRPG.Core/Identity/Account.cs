namespace GUNRPG.Core.Identity;

/// <summary>
/// A game account owned by an ApplicationUser.
/// An account acts as the container for all of the user's Operators.
/// The Account → Operator relationship is one-to-many: an account can own many operators.
/// </summary>
public sealed class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The ASP.NET Identity user that owns this account.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Display name for the account (separate from the authentication username).
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Operator IDs that belong to this account.
    /// Operators are defined in GUNRPG.Core.Operators; the account stores the IDs only
    /// to keep identity concerns cleanly separated from game logic.
    /// </summary>
    public List<Guid> OperatorIds { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
