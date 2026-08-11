namespace EventHub.Core.Services.Models;

public class CreateTicketRequest {
    public Guid EventId { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int TotalQuantity { get; set; }
}

public class UpdateTicketRequest {
    public string? Type { get; set; }
    public decimal? Price { get; set; }
    public int? TotalQuantity { get; set; }
}