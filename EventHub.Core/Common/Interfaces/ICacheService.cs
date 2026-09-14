namespace EventHub.Core.Common;

public interface ICacheService {
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value, TimeSpan expiry);
    Task RemoveAsync(string key);
    Task RemoveByPrefixAsync(string prefix);
}