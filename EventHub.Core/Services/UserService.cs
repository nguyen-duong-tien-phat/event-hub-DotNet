using EventHub.Core.Common;
using EventHub.Core.Entities;
using EventHub.Core.Enums;
using EventHub.Core.Interfaces;
using EventHub.Core.Services.Models;
using Microsoft.AspNetCore.Identity;

namespace EventHub.Core.Services;

public class UserService(
    IRepository<User> userRepository, 
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

    public Task<User?> GetByIdAsync(Guid id) => userRepository.GetByIdAsync(id);

    public async Task<User> CreateAsync(CreateUserRequest request) {
        var user = new User {
            Email = request.Email,
            FullName = request.FullName,
            Role = UserRole.Attendee
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        await userRepository.AddAsync(user);
        await userRepository.SaveChangesAsync();
        return user;
    }
    
    public async Task<User?> BecomeOrganizerAsync(Guid userId) {
        var user = await userRepository.GetByIdAsync(userId);
        if (user == null) return null;

        user.Role = UserRole.Organizer;
        userRepository.Update(user);
        await userRepository.SaveChangesAsync();
        return user;
    }
}