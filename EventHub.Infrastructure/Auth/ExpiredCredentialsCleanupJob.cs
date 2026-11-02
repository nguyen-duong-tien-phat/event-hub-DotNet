using EventHub.Core.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventHub.BackgroundJobs;

public class ExpiredCredentialsCleanupJob(IServiceScopeFactory scopeFactory, ILogger<ExpiredCredentialsCleanupJob> logger): BackgroundService {
    // Nothing here is urgent, so once an hour is plenty.
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                using var scope = scopeFactory.CreateScope();
                var cleaner = scope.ServiceProvider.GetRequiredService<ExpiredCredentialsCleaner>();

                var (tokens, codes) = await cleaner.CleanAsync(DateTime.UtcNow);
                if (tokens + codes > 0) {
                    logger.LogInformation("Removed {Tokens} expired refresh tokens and {Codes} expired reset codes", tokens, codes);
                }
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                logger.LogError(ex, "Cleaning up expired credentials failed");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }
}
