using EventHub.Core.Entities;

namespace EventHub.Core.Interfaces;

public interface IUserRepository : IRepository<User> {
    Task<User?> GetByEmailAsync(string email);
}