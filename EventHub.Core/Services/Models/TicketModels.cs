namespace EventHub.Core.Services.Models;

public class CreateTicketRequest {
    public required string EventId { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int TotalQuantity { get; set; }
    public List<string> Highlights { get; set; } = [];
    public required int MaxPerOrder { get; set; }
}

public class UpdateTicketRequest {
    public string? Type { get; set; }
    public decimal? Price { get; set; }
    public int? TotalQuantity { get; set; }
}