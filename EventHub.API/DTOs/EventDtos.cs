using System.ComponentModel.DataAnnotations;
using EventHub.Annotations;

namespace EventHub.DTOs;

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

    [Required(ErrorMessage = "OrganizerId is required")]
    public Guid OrganizerId { get; set; }
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