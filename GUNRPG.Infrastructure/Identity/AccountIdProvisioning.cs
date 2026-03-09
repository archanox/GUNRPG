using Microsoft.AspNetCore.Identity;

namespace GUNRPG.Infrastructure.Identity;

/// <summary>
/// Ensures authenticated users have a stable account identifier for operator ownership isolation.
/// </summary>
public static class AccountIdProvisioning
{
    public static async Task<IdentityResult> EnsureAssignedAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationUser user,
        CancellationToken ct = default)
    {
        if (user.AccountId is { } accountId && accountId != Guid.Empty)
            return IdentityResult.Success;

        user.AccountId = Guid.NewGuid();
        return await userManager.UpdateAsync(user);
    }
}
