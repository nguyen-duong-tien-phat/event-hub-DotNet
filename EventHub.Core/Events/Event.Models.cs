using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using EventHub.Core.Tickets;
using EventHub.Core.Common;
using EventHub.Core.Users;

namespace EventHub.Core.Events;

public class EventQuery : PaginationQuery {
    public string? Search { get; set; }
    public string? Category { get; set; }

    /// <summary>Filters on the price of an event's cheapest ticket.</summary>
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
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
    public string Category { get; set; } = "Other";
    public DateTime? EndsAt { get; set; }
    public DateTime? DoorsOpenAt { get; set; }
    public string? AgeRestriction { get; set; }
    public string? RefundPolicy { get; set; }
}

public class UpdateEventRequest {
    public string? Title { get; set; }
    public string? ImageUrl { get; set; }
    public string? Description { get; set; }
    public DateTime? StartsAt { get; set; }
    public string? Location { get; set; }
    public string? Category { get; set; }
    public DateTime? EndsAt { get; set; }
    public DateTime? DoorsOpenAt { get; set; }
    public string? AgeRestriction { get; set; }
    public string? RefundPolicy { get; set; }
}


public class Organizer {
    public required string Id { get; set; }
    public required string FullName { get; set; }

    /// <summary>
    /// Contact email. Only filled in on the event detail response, so it does
    /// not show up in event lists.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Email { get; set; }

    public static Organizer FromUser(User user) => new() {
        Id = user.Id,
        FullName = user.FullName,
    };
}

public class EventListItemDto {
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required Organizer Organizer { get; set; }
    public DateTime StartsAt { get; set; }
    public required string Location { get; set; }
    public required string ImageUrl { get; set; }
    public string Category { get; set; } = string.Empty;

    /// <summary>Price of the cheapest ticket, or null if the event has none.</summary>
    public decimal? MinPrice { get; set; }
    public DateTime CreatedAt { get; set; }

    public static EventListItemDto FromEntity(Event ev) => new() {
        Id = ev.Id,
        Title = ev.Title,
        Description = ev.Description,
        StartsAt = ev.StartsAt,
        Organizer = Organizer.FromUser(ev.Organizer!),
        Location = ev.Location,
        ImageUrl = ev.ImageUrl,
        Category = ev.Category,
        MinPrice = ev.Tickets.Count == 0 ? null : ev.Tickets.Min(t => t.Price),
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
    public string Category { get; set; } = string.Empty;
    public DateTime? EndsAt { get; set; }
    public DateTime? DoorsOpenAt { get; set; }
    public string? AgeRestriction { get; set; }
    public string? RefundPolicy { get; set; }
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
    
    public List<string> Highlights { get; set; } = [];

    [MaxLength(50)]
    public string Category { get; set; } = "Other";

    public DateTime? EndsAt { get; set; }
    public DateTime? DoorsOpenAt { get; set; }

    [MaxLength(100)]
    public string? AgeRestriction { get; set; }

    [MaxLength(500)]
    public string? RefundPolicy { get; set; }
}

public class UpdateEventDto {
    [MaxLength(200)]
    public string? Title { get; set; }

    public string? ImageUrl { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    [FutureDate(1, ErrorMessage = "Event must start at least 1 day from now")]
    public DateTime? StartsAt { get; set; }

    [MaxLength(300)]
    public string? Location { get; set; }

    [MaxLength(50)]
    public string? Category { get; set; }

    public DateTime? EndsAt { get; set; }
    public DateTime? DoorsOpenAt { get; set; }

    [MaxLength(100)]
    public string? AgeRestriction { get; set; }

    [MaxLength(500)]
    public string? RefundPolicy { get; set; }
}
