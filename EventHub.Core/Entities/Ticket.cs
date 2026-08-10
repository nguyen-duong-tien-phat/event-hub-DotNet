namespace EventHub.Core.Entities;

public class Ticket: BaseEntity {
    public Guid EventId { get; set; }
    public Event? Event { get; set; }
    public string Type { get; set; } = string.Empty; // e.g. "General", "VIP"
    public decimal Price { get; set; }
    public int TotalQuantity { get; set; }
    public int RemainingQuantity { get; set; }
}