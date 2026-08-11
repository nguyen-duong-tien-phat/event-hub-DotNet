namespace EventHub.Core.Services.Models;

public class CreateEventRequest {
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public string Location { get; set; } = string.Empty;
    public Guid OrganizerId { get; set; }
}

public class UpdateEventRequest {
    public string? Title { get; set; }
    public string? Description { get; set; }
    public DateTime? StartsAt { get; set; }
    public string? Location { get; set; }
}