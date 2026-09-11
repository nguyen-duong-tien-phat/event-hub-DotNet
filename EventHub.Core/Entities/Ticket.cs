namespace EventHub.Core.Entities;

public class Ticket(): BaseEntity("ticket") {
    public required string EventId { get; set; }
    public string Type { get; set; } = string.Empty; // e.g. "General", "VIP"
    public decimal Price { get; set; }
    public int TotalQuantity { get; set; }
    public int RemainingQuantity { get; set; }
    public List<string> Highlights { get; set; } = [];
    public required int MaxPerOrder {get; set;}
}