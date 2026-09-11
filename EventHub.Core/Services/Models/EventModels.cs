using EventHub.Core.Entities;

namespace EventHub.Core.Services.Models;

public class CreateEventRequest {
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public string Location { get; set; } = string.Empty;
    public required string OrganizerId { get; set; }
    public required string ImageUrl { get; set; }
    public List<string> Highlights { get; set; }
}

public class UpdateEventRequest {
    public string? Title { get; set; }
    public string? Description { get; set; }
    public DateTime? StartsAt { get; set; }
    public string? Location { get; set; }
}


public class Organizer {
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
}

public class EventResponse {
    public string Id { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public DateTime StartsAt { get; set; }
    public string Location { get; set; } = null!;
    public string ImageUrl { get; set; } = null!;
    public List<string> Highlights { get; set; } = [];
    public Organizer Organizer { get; set; }
    public List<Ticket> Tickets { get; set; } = [];
}