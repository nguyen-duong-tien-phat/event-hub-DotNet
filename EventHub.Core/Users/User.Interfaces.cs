using EventHub.Core.Common;

namespace EventHub.Core.Users;

public interface IUserRepository : IRepository<User> {
    /// <summary>Case does not matter: emails are stored in lower case and the lookup lowers its input.</summary>
    Task<User?> GetByEmailAsync(string email);

    /// <summary>Saves a new user. False when the email is already taken (the unique index decides, so two sign-ups at once cannot both win).</summary>
    Task<bool> TryAddAsync(User user);
}