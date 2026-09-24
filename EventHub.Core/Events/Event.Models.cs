using System.ComponentModel.DataAnnotations;
using EventHub.Core.Tickets;
using EventHub.Core.Common;

namespace EventHub.Core.Events;

public class EventQuery : PaginationQuery {
    public string? Search { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class CreateEventRequest {
    public required string Title { get; set; }
    public required string Description { get; set; }
    public DateTime StartsAt { get; set; }
    public required string Location { get; set; }
    public required string OrganizerId { get; set; }
    public required string ImageUrl { get; set; }
    public List<string> Highlights { get; set; } = [];
}

public class UpdateEventRequest {
    public string? Title { get; set; }
    public string? Description { get; set; }
    public DateTime? StartsAt { get; set; }
    public string? Location { get; set; }
}


public class Organizer {
    public required string Id { get; set; }
    public required string Email { get; set; }
    public required string FullName { get; set; }
    public required string Role { get; set; }
}

public class EventListItemDto {
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public DateTime StartsAt { get; set; }
    public required string Location { get; set; }
    public required string ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }

    public static EventListItemDto FromEntity(Event ev) => new() {
        Id = ev.Id,
        Title = ev.Title,
        Description = ev.Description,
        StartsAt = ev.StartsAt,
        Location = ev.Location,
        ImageUrl = ev.ImageUrl,
        CreatedAt = ev.CreatedAt
    };
}

public class EventResponse {
    public required string Id { get; set; } 
    public required string Title { get; set; } 
    public required string Description { get; set; } 
    public DateTime StartsAt { get; set; }
    public required string Location { get; set; } 
    public required string ImageUrl { get; set; } 
    public List<string> Highlights { get; set; } = [];
    public required Organizer Organizer { get; set; }
    public List<Ticket> Tickets { get; set; } = [];
}

// DTOs
public class CreateEventDto
{
    [Required(ErrorMessage = "Title is required")]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    [FutureDate(1, ErrorMessage = "Event must start at least 1 day from now")]
    public DateTime StartsAt { get; set; }

    [Required(ErrorMessage = "Location is required")]
    [MaxLength(300)]
    public string Location { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "ImageUrl is required")]
    public required string ImageUrl { get; set; }
    
    public List<string> Highlights { get; set; }
}

public class UpdateEventDto {
    [MaxLength(200)]
    public string? Title { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    [FutureDate(1, ErrorMessage = "Event must start at least 1 day from now")]
    public DateTime? StartsAt { get; set; }

    [MaxLength(300)]
    public string? Location { get; set; }
}