using EventHub.Core.Interfaces;
using EventHub.Core.Services;

namespace EventHub.BackgroundJobs;

public class BookingExpiryJob(IServiceScopeFactory scopeFactory): BackgroundService {
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ExpiryThreshold = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            using var scope = scopeFactory.CreateScope();
            var bookingService = scope.ServiceProvider.GetRequiredService<BookingService>();

            await bookingService.ExpireAbandonedBookingAsync(ExpiryThreshold);
            
            await Task.Delay(CheckInterval, stoppingToken);
        }
    }
}