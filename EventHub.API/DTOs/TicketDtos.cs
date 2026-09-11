using System.ComponentModel.DataAnnotations;

namespace EventHub.DTOs;

public class CreateTicketDto {
    [Required(ErrorMessage = "EventId is required")]
    public required string EventId { get; set; }

    [Required(ErrorMessage = "Type is required")]
    public string Type { get; set; } = string.Empty;

    [Range(0.01, double.MaxValue, ErrorMessage = "Price must be greater than zero")]
    public decimal Price { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "TotalQuantity must be at least 1")]
    public int TotalQuantity { get; set; }
    
    public List<string> Highlights { get; set; } = [];
    
    [Range(1, int.MaxValue, ErrorMessage = "MaxPerOrder must be at least 1")]
    public required int MaxPerOrder { get; set; }
}

public class UpdateTicketDto {
    public string? Type { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Price must be greater than zero")]
    public decimal? Price { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "TotalQuantity must be at least 1")]
    public int? TotalQuantity { get; set; }
}