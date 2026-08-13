using EventHub.Annotations;

namespace EventHub.DTOs;

public class CreateEventDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    [FutureDate(1, ErrorMessage = "Events must be scheduled at least 1 day in advance")]
    public DateTime StartsAt { get; set; }
    public string Location { get; set; } = string.Empty;
    public Guid OrganizerId { get; set; }
}

public class UpdateEventDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public DateTime? StartsAt { get; set; }
    public string? Location { get; set; }
}