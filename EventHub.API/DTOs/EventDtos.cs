using System.ComponentModel.DataAnnotations;
using EventHub.Annotations;

namespace EventHub.DTOs;

public class CreateEventDto
{
    [Required]
    public string Title { get; set; } = string.Empty;
    
    [Required]
    public string Description { get; set; } = string.Empty;
    
    [Required]
    [FutureDate(1, ErrorMessage = "Events must be scheduled at least 1 day in advance")]
    public DateTime StartsAt { get; set; }
    
    [Required]
    public string Location { get; set; } = string.Empty;
    
    [Required]
    public Guid OrganizerId { get; set; }
}

public class UpdateEventDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public DateTime? StartsAt { get; set; }
    public string? Location { get; set; }
}