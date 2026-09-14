using EventHub.Core.Common;
using EventHub.Core.Tickets;
using EventHub.Core.Users;

namespace EventHub.Core.Events;

public class Event(): BaseEntity("event") {
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required DateTime StartsAt { get; set; }
    public required string Location { get; set; }
    public required string OrganizerId { get; set; }
    public User? Organizer { get; set; }
    public required string ImageUrl { get; set; }
    public required List<string> Highlights { get; set; } = [];
    public ICollection<Ticket> Tickets { get; set; } = [];
}