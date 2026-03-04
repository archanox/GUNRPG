using Microsoft.AspNetCore.Identity;

namespace GUNRPG.Infrastructure.Identity;

/// <summary>
/// Represents an authenticated user account in GunRPG.
/// Integrates with ASP.NET Identity for password hashing and validation.
/// Each user may own one Account which in turn contains many Operators.
/// </summary>
public sealed class ApplicationUser : IdentityUser
{
    /// <summary>
    /// The game account associated with this user. One-to-one with ApplicationUser.
    /// </summary>
    public Guid? AccountId { get; set; }

    /// <summary>
    /// When the user was first registered on this node.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
