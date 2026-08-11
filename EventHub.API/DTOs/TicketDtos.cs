namespace EventHub.DTOs;

public class CreateTicketDto
{
    public Guid EventId { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int TotalQuantity { get; set; }
}

public class UpdateTicketDto
{
    public string? Type { get; set; }
    public decimal? Price { get; set; }
    public int? TotalQuantity { get; set; }
}