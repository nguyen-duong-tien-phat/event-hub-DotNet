using EventHub.Core.Auth;
using EventHub.Core.Common;
using Microsoft.AspNetCore.Identity;

namespace EventHub.Core.Users;

public class EmailTakenException() : Exception("An account with this email already exists.");

public class UserService(
    IUserRepository userRepository, 
    IPasswordHasher<User> passwordHasher,
    IRefreshTokenRepository refreshTokens) 
{
    public async Task<PagedResult<User>> GetPagedAsync(int page, int pageSize, string? search = null, bool? isActive = null) {
        var (items, totalCount) = await userRepository.SearchPagedAsync(search, isActive, page, pageSize);
        return new PagedResult<User> {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public Task<User?> GetByIdAsync(string id) => userRepository.GetByIdAsync(id);

    /// <exception cref="EmailTakenException">Another account already uses this email.</exception>
    public async Task<User> CreateAsync(CreateUserRequest request) {
        var user = new User {
            Email = request.Email.Trim().ToLowerInvariant(), // one spelling per address, so the unique index means what it says
            FullName = request.FullName,
            Role = request.Role ?? UserRole.Attendee
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        if (!await userRepository.TryAddAsync(user)) throw new EmailTakenException();
        return user;
    }
    
    /// <summary>
    /// Sets a user's role. An admin cannot lower their own role, so at least one admin always remains.
    /// The new role reaches the user's token when it is next refreshed (up to the access token's lifetime).
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The caller tried to take their own admin role away.</exception>
    public async Task<User?> ChangeRoleAsync(string requestId, string userId, UserRole role) {
        if (requestId == userId && role != UserRole.Admin) {
            throw new UnauthorizedAccessException("You cannot lower your own role.");
        }

        var user = await userRepository.GetByIdAsync(userId);
        if (user == null) return null;

        // No Update() call: EF then writes only the Role column, not a possibly stale password hash.
        user.Role = role;
        await userRepository.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Blocks or unblocks an account. Blocking ends every session (refresh tokens), but an access token
    /// already issued keeps working until it expires (up to its lifetime). An admin cannot block themselves.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The caller tried to block their own account.</exception>
    public async Task<User?> SetActiveAsync(string requestId, string userId, bool isActive) {
        if (requestId == userId && !isActive) {
            throw new UnauthorizedAccessException("You cannot block your own account.");
        }

        var user = await userRepository.GetByIdAsync(userId);
        if (user == null) return null;

        user.IsActive = isActive; // no Update(): only this column is written, never a stale password hash
        await userRepository.SaveChangesAsync();
        if (!isActive) await refreshTokens.RevokeAllForUserAsync(userId);
        return user;
    }

    public async Task<User?> VerifyPasswordAsync(string email, string password) {
        var user = await userRepository.GetByEmailAsync(email);
        if (user == null) return null;

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result ==  PasswordVerificationResult.Success ? user : null;
    }
}