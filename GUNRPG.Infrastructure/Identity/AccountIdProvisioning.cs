using System.Collections.Concurrent;
using LiteDB;
using Microsoft.AspNetCore.Identity;

namespace GUNRPG.Infrastructure.Identity;

/// <summary>
/// Ensures authenticated users have a stable account identifier for operator ownership isolation.
/// </summary>
public static class AccountIdProvisioning
{
    private static readonly ConcurrentDictionary<string, UserLock> UserLocks = new(StringComparer.Ordinal);

    public static async Task<IdentityResult> EnsureAssignedAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationUser user,
        CancellationToken ct = default)
    {
        if (user.AccountId is { } accountId && accountId != Guid.Empty)
            return IdentityResult.Success;

        using (await AcquireUserLockAsync(user.Id, ct))
        {
            var storedUser = await userManager.FindByIdAsync(user.Id);
            if (storedUser is null)
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "UserNotFound",
                    Description = "The user no longer exists in the identity store.",
                });
            }

            if (storedUser.AccountId is { } storedAccountId && storedAccountId != Guid.Empty)
            {
                user.AccountId = storedAccountId;
                return IdentityResult.Success;
            }

            storedUser.AccountId = Guid.NewGuid();
            var result = await userManager.UpdateAsync(storedUser);

            if (result.Succeeded && storedUser.AccountId is { } newAccountId && newAccountId != Guid.Empty)
            {
                user.AccountId = newAccountId;
                return result;
            }

            var reloadedUser = await userManager.FindByIdAsync(user.Id);
            if (reloadedUser?.AccountId is { } reloadedAccountId && reloadedAccountId != Guid.Empty)
            {
                user.AccountId = reloadedAccountId;
                return IdentityResult.Success;
            }

            return result;
        }
    }

    public static async Task<(IdentityResult Result, Guid? AccountId)> EnsureAssignedAsync(
        ILiteCollection<ApplicationUser> users,
        string userId,
        CancellationToken ct = default)
    {
        using (await AcquireUserLockAsync(userId, ct))
        {
            var storedUser = users.FindById(userId);
            if (storedUser is null)
            {
                return (
                    IdentityResult.Failed(new IdentityError
                    {
                        Code = "UserNotFound",
                        Description = "The user no longer exists in the identity store.",
                    }),
                    null);
            }

            if (storedUser.AccountId is { } storedAccountId && storedAccountId != Guid.Empty)
                return (IdentityResult.Success, storedAccountId);

            storedUser.AccountId = Guid.NewGuid();
            if (users.Update(storedUser) && storedUser.AccountId is { } newAccountId && newAccountId != Guid.Empty)
                return (IdentityResult.Success, newAccountId);

            var reloadedUser = users.FindById(userId);
            if (reloadedUser?.AccountId is { } reloadedAccountId && reloadedAccountId != Guid.Empty)
                return (IdentityResult.Success, reloadedAccountId);

            return (
                IdentityResult.Failed(new IdentityError
                {
                    Code = "UserUpdateFailed",
                    Description = "Failed to assign an account ID to the user.",
                }),
                null);
        }
    }

    private static async Task<IDisposable> AcquireUserLockAsync(string userId, CancellationToken ct)
    {
        while (true)
        {
            var userLock = UserLocks.GetOrAdd(userId, static _ => new UserLock());
            Interlocked.Increment(ref userLock.RefCount);

            if (UserLocks.TryGetValue(userId, out var currentLock) && ReferenceEquals(userLock, currentLock))
            {
                await userLock.Gate.WaitAsync(ct);
                return new Releaser(userId, userLock);
            }

            Interlocked.Decrement(ref userLock.RefCount);
        }
    }

    private sealed class UserLock
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public volatile int RefCount;
    }

    private readonly struct Releaser(string userId, UserLock userLock) : IDisposable
    {
        public void Dispose()
        {
            userLock.Gate.Release();
            if (Interlocked.Decrement(ref userLock.RefCount) == 0)
                UserLocks.TryRemove(new KeyValuePair<string, UserLock>(userId, userLock));
        }
    }
}
