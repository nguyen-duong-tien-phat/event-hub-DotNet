namespace EventHub.Core.Entities;

public class Event(): BaseEntity("event") {
    public required string Title { get; set; }
    public required string Description { get; set; }
    public DateTime StartsAt { get; set; }
    public required string Location { get; set; }
    public required string OrganizerId { get; set; }
    public User Organizer { get; set; }
    public List<Ticket> Tickets { get; set; } = [];
    public required string ImageUrl { get; set; }
    public List<string> Highlights { get; set; } = [];

}