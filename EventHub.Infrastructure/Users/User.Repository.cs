using EventHub.Core.Users;
using EventHub.Infrastructure.Data;
using EventHub.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Users;

public class UserRepository(AppDbContext db): Repository<User>(db), IUserRepository {
    public Task<User?> GetByEmailAsync(string email) => DbSet.FirstOrDefaultAsync(x => x.Email == email);
}
