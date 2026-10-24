using System.IdentityModel.Tokens.Jwt;
using EventHub.Core.Events;
using EventHub.Core.SavedEvents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.SavedEvents;

[ApiController]
[Route("saved-events")]
[Authorize]
public class SavedEventsController(SavedEventService savedEventService) : ControllerBase {
    private string UserId => User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value;

    /// <summary>The user's saved events, most recently saved first.</summary>
    [HttpGet]
    public async Task<ActionResult<List<EventListItemDto>>> GetAll([FromQuery] int limit = 50) {
        var events = await savedEventService.GetEventsAsync(UserId, Math.Clamp(limit, 1, 100));
        return Ok(events.Select(EventListItemDto.FromEntity).ToList());
    }

    /// <summary>Just the ids, so lists can show which events are saved.</summary>
    [HttpGet("ids")]
    public async Task<ActionResult<List<string>>> GetIds() =>
        Ok(await savedEventService.GetEventIdsAsync(UserId));

    [HttpPut("{eventId}")]
    public async Task<IActionResult> Save(string eventId) =>
        await savedEventService.SaveAsync(UserId, eventId) ? NoContent() : NotFound();

    [HttpDelete("{eventId}")]
    public async Task<IActionResult> Remove(string eventId) {
        await savedEventService.RemoveAsync(UserId, eventId);
        return NoContent();
    }
}
