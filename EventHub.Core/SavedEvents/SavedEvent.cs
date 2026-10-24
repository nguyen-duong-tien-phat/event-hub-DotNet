using EventHub.Core.Common;
using EventHub.Core.Events;

namespace EventHub.Core.SavedEvents;

/// <summary>An event a user has saved (the heart button).</summary>
public class SavedEvent(): BaseEntity("saved") {
    public required string UserId { get; set; }
    public required string EventId { get; set; }
    public Event? Event { get; set; } 
}
