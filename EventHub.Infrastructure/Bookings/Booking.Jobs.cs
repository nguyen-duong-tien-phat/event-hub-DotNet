
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

                var result = await bookingService.ExpireAbandonedBookingAsync(ExpiryThreshold);
                if (result.Expired > 0) logger.LogInformation("Cancelled {Count} abandoned bookings", result.Expired);
                foreach (var failure in result.Failures) {
                    if (failure.Step == ExpiryStep.Cancel) {
                        logger.LogError(failure.Error, "Could not cancel abandoned booking {BookingId}; it stays Pending and is retried next run", failure.BookingId);
                    } else {
                        logger.LogError(failure.Error, "Booking {BookingId} was cancelled but its payment could not be stopped or refunded; check it in Stripe", failure.BookingId);
                    }
                }
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                // One failed run (DB down, timeout...) must not stop the job for good.
                logger.LogError(ex, "Expiring abandoned bookings failed");
            }
            
            await Task.Delay(CheckInterval, stoppingToken);
        }
    }
}