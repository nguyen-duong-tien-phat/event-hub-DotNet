using EventHub.Core.Common;

namespace EventHub.Core.Users;

public interface IUserRepository : IRepository<User> {
    Task<User?> GetByEmailAsync(string email);
}