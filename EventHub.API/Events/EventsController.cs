using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EventHub.Core.Common;
using EventHub.Core.Events;
using EventHub.Core.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Events;

[ApiController]
[Route("events")]
public class EventsController(EventService eventService) : ControllerBase {
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] EventQuery query) {
        if (query.From.HasValue && query.To.HasValue && query.From > query.To) {
            return BadRequest(new { message = "'from' must be earlier than 'to'" });
        }
        
        var result = await eventService.GetPagedAsync(
            query.Page, 
            query.PageSize, 
            query.Search, 
            query.From, 
            query.To);
        return Ok(result.Map(EventListItemDto.FromEntity));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id) {
        var ev = await eventService.GetByIdAsync(id);
        return ev == null ? NotFound() : Ok(ev);
    }

    [Authorize(Roles = "Admin, Organizer")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateEventDto dto) {
        var orgId = User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value;
        var newEvent = await eventService.CreateAsync(new CreateEventRequest {
            Title = dto.Title,
            Description = dto.Description,
            StartsAt = dto.StartsAt,
            Location = dto.Location,
            OrganizerId = orgId,
            ImageUrl = dto.ImageUrl,
            Highlights = dto.Highlights ?? []
        });
        return CreatedAtAction(nameof(GetById), new { id = newEvent.Id }, newEvent);
    }

    [Authorize(Roles = "Admin, Organizer")]
    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id, UpdateEventDto dto) {
        var requestId = User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value;
        var requestRole = Enum.Parse<UserRole>(User.FindFirst(ClaimTypes.Role)!.Value);
        
        try {
            var updated = await eventService.UpdateAsync(requestId, requestRole, id, new UpdateEventRequest {
                Title = dto.Title,
                Description = dto.Description,
                StartsAt = dto.StartsAt,
                Location = dto.Location
            });
            return updated == null ? NotFound() : Ok(updated);
        } catch (UnauthorizedAccessException) {
            return Forbid();
        }
    }
}