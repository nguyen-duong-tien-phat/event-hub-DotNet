
using EventHub.Core.Bookings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventHub.BackgroundJobs;

public class BookingExpiryJob(IServiceScopeFactory scopeFactory, ILogger<BookingExpiryJob> logger): BackgroundService {
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ExpiryThreshold = BookingService.HoldDuration;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                using var scope = scopeFactory.CreateScope();
                var bookingService = scope.ServiceProvider.GetRequiredService<BookingService>();

                await bookingService.ExpireAbandonedBookingAsync(ExpiryThreshold);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                // One failed run (DB down, timeout...) must not stop the job for good.
                logger.LogError(ex, "Expiring abandoned bookings failed");
            }
            
            await Task.Delay(CheckInterval, stoppingToken);
        }
    }
}