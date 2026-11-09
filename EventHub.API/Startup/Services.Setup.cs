using EventHub.BackgroundJobs;
using EventHub.Core.Auth;
using EventHub.Core.Bookings;
using EventHub.Core.Common;
using EventHub.Core.Events;
using EventHub.Core.SavedEvents;
using EventHub.Core.Tickets;
using EventHub.Core.Users;
using EventHub.Infrastructure.Auth;
using EventHub.Infrastructure.Bookings;
using EventHub.Infrastructure.Common;
using EventHub.Infrastructure.Data;
using EventHub.Infrastructure.Events;
using EventHub.Infrastructure.SavedEvents;
using EventHub.Infrastructure.Tickets;
using EventHub.Infrastructure.Users;

namespace EventHub.Startup;

public static class ServicesSetup {
    /// <summary>The business services, their repositories, and the background jobs.</summary>
    public static void AddAppServices(this WebApplicationBuilder builder) {
        var services = builder.Services;

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<UserService>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<EventService>();
        services.AddScoped<ITicketRepository, TicketRepository>();
        services.AddScoped<TicketService>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<BookingService>();
        services.AddScoped<IAdmissionRepository, AdmissionRepository>();
        services.AddScoped<AdmissionService>();
        services.AddScoped<CheckInService>();
        services.AddScoped<ISavedEventRepository, SavedEventRepository>();
        services.AddScoped<SavedEventService>();

        // Signs the QR codes on tickets. Kept apart from the JWT key so rotating one does not break the other.
        var ticketSecret = builder.Configuration["Tickets:Secret"];
        if (string.IsNullOrEmpty(ticketSecret))
            throw new InvalidOperationException("Tickets__Secret is not configured.");
        services.AddSingleton(new TicketCodeSigner(ticketSecret));

        services.AddHostedService<BookingExpiryJob>();
        services.AddScoped<ExpiredCredentialsCleaner>();
        services.AddHostedService<ExpiredCredentialsCleanupJob>();
    }
}
