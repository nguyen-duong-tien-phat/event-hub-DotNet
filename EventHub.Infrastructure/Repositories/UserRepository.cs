using EventHub.Core.Entities;
using EventHub.Core.Interfaces;
using EventHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Repositories;

public class UserRepository(AppDbContext db): Repository<User>(db), IUserRepository {
    public Task<User?> GetByEmailAsync(string email) => 
        Db.Users.FirstOrDefaultAsync(u => u.Email == email);
}