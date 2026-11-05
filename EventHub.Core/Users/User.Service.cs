using EventHub.Core.Common;
using Microsoft.AspNetCore.Identity;

namespace EventHub.Core.Users;

public class UserService(
    IUserRepository userRepository, 
    IPasswordHasher<User> passwordHasher) 
{
    public async Task<PagedResult<User>> GetPagedAsync(int page, int pageSize) {
        var (items, totalCount) = await userRepository.GetPagedAsync(page, pageSize);
        return new PagedResult<User> {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public Task<User?> GetByIdAsync(string id) => userRepository.GetByIdAsync(id);

    public async Task<User> CreateAsync(CreateUserRequest request) {
        var user = new User {
            Email = request.Email,
            FullName = request.FullName,
            Role = request.Role ?? UserRole.Attendee
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        await userRepository.AddAsync(user);
        await userRepository.SaveChangesAsync();
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

    public async Task<User?> VerifyPasswordAsync(string email, string password) {
        var user = await userRepository.GetByEmailAsync(email);
        if (user == null) return null;

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result ==  PasswordVerificationResult.Success ? user : null;
    }
}