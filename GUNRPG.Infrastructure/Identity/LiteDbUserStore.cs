using GUNRPG.Core.Identity;
using LiteDB;
using Microsoft.AspNetCore.Identity;

namespace GUNRPG.Infrastructure.Identity;

/// <summary>
/// LiteDB-backed ASP.NET Identity user store.
/// Implements the minimum interfaces required by <see cref="UserManager{TUser}"/>:
/// user CRUD, password hashing storage, and e-mail lookup.
/// </summary>
public sealed class LiteDbUserStore :
    IUserStore<ApplicationUser>,
    IUserPasswordStore<ApplicationUser>,
    IUserEmailStore<ApplicationUser>
{
    internal const string CollectionName = "identity_users";

    private readonly ILiteCollection<ApplicationUser> _users;

    public LiteDbUserStore(ILiteDatabase db)
    {
        _users = db.GetCollection<ApplicationUser>(CollectionName);
        _users.EnsureIndex(u => u.NormalizedUserName, unique: true);
        _users.EnsureIndex(u => u.NormalizedEmail);
    }

    // ── IUserStore ──────────────────────────────────────────────────────────

    public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken ct)
    {
        try
        {
            _users.Insert(user);
            return Task.FromResult(IdentityResult.Success);
        }
        catch (LiteException ex)
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "LiteDbInsertError",
                Description = $"Failed to create user: {ex.Message}",
            }));
        }
    }

    public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken ct)
    {
        try
        {
            if (!_users.Update(user))
                return Task.FromResult(IdentityResult.Failed(new IdentityError
                {
                    Code = "UserUpdateFailed",
                    Description = "Failed to update user: user not found or not modified.",
                }));

            return Task.FromResult(IdentityResult.Success);
        }
        catch (LiteException ex)
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "LiteDbUpdateError",
                Description = $"Failed to update user: {ex.Message}",
            }));
        }
    }

    public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken ct)
    {
        try
        {
            if (!_users.Delete(user.Id))
                return Task.FromResult(IdentityResult.Failed(new IdentityError
                {
                    Code = "UserDeleteFailed",
                    Description = "Failed to delete user: user not found.",
                }));

            return Task.FromResult(IdentityResult.Success);
        }
        catch (LiteException ex)
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "LiteDbDeleteError",
                Description = $"Failed to delete user: {ex.Message}",
            }));
        }
    }

    public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken ct) =>
        Task.FromResult<ApplicationUser?>(_users.FindById(userId));

    public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken ct) =>
        Task.FromResult<ApplicationUser?>(_users.FindOne(u => u.NormalizedUserName == normalizedUserName));

    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.Id);

    public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.UserName);

    public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken ct)
    {
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken ct)
    {
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    // ── IUserPasswordStore ──────────────────────────────────────────────────

    public Task SetPasswordHashAsync(ApplicationUser user, string? passwordHash, CancellationToken ct)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.PasswordHash is not null);

    // ── IUserEmailStore ─────────────────────────────────────────────────────

    public Task SetEmailAsync(ApplicationUser user, string? email, CancellationToken ct)
    {
        user.Email = email;
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.Email);

    public Task<bool> GetEmailConfirmedAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.EmailConfirmed);

    public Task SetEmailConfirmedAsync(ApplicationUser user, bool confirmed, CancellationToken ct)
    {
        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public Task<ApplicationUser?> FindByEmailAsync(string normalizedEmail, CancellationToken ct) =>
        Task.FromResult<ApplicationUser?>(_users.FindOne(u => u.NormalizedEmail == normalizedEmail));

    public Task<string?> GetNormalizedEmailAsync(ApplicationUser user, CancellationToken ct) =>
        Task.FromResult(user.NormalizedEmail);

    public Task SetNormalizedEmailAsync(ApplicationUser user, string? normalizedEmail, CancellationToken ct)
    {
        user.NormalizedEmail = normalizedEmail;
        return Task.CompletedTask;
    }

    public void Dispose() { /* LiteDatabase lifetime is managed by the DI container. */ }
}
