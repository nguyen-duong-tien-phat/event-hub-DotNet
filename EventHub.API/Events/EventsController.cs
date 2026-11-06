using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EventHub.Core.Common;
using EventHub.Core.Events;
using EventHub.Core.Files;
using EventHub.Core.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Events;

[ApiController]
[Route("events")]
public class EventsController(EventService eventService, UploadService uploadService) : ControllerBase {
    /// <summary>Returns a presigned URL to PUT a poster to, and the imageUrl to send when creating the event.</summary>
    [Authorize(Roles = "Admin, Organizer")]
    [HttpPost("image-upload")]
    public ActionResult<UploadDto> CreateImageUpload(UploadRequestDto dto) {
        try {
            return Ok(uploadService.CreateUpload(UploadKind.EventImage, dto.ContentType));
        } catch (ArgumentException e) {
            return BadRequest(new { message = e.Message });
        }
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<EventListItemDto>>> GetAll([FromQuery] EventQuery query) {
        if (query.From.HasValue && query.To.HasValue && query.From > query.To) {
            return BadRequest(new { message = "'from' must be earlier than 'to'" });
        }
        
        if (query.MinPrice.HasValue && query.MaxPrice.HasValue && query.MinPrice > query.MaxPrice) {
            return BadRequest(new { message = "'minPrice' must not be higher than 'maxPrice'" });
        }

        var result = await eventService.GetPagedAsync(
            query.Page, 
            query.PageSize, 
            query.Search, 
            query.Category,
            query.MinPrice,
            query.MaxPrice,
            query.From,
            query.To,
            query.OrganizerId,
            query.Sort);
        return Ok(result.Map(EventListItemDto.FromEntity));
    }
    
    

    [HttpGet("featured")]
    public async Task<ActionResult<List<EventListItemDto>>> GetFeatured([FromQuery] int limit = 5) {
        var result = await eventService.GetFeaturedEventsAsync(Math.Clamp(limit, 1, 10));
        return Ok(result.Select(EventListItemDto.FromEntity).ToList());
    }

    [HttpGet("upcoming")]
    public async Task<ActionResult<List<EventListItemDto>>> GetUpcoming([FromQuery] int limit = 5) {
        var result = await eventService.GetUpcomingEventsAsync(Math.Clamp(limit, 1, 10));
        return Ok(result.Select(EventListItemDto.FromEntity).ToList());
    }

    /// <summary>Categories to offer in the filters: only those with an upcoming event.</summary>
    [HttpGet("categories")]
    public async Task<ActionResult<List<string>>> GetCategories() =>
        Ok(await eventService.GetCategoriesAsync());

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id) {
        var ev = await eventService.GetByIdAsync(id);
        return ev == null ? NotFound() : Ok(ev);
    }

    [Authorize(Roles = "Admin, Organizer")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateEventDto dto) {
        var requestId = User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value;
        var requestRole = Enum.Parse<UserRole>(User.FindFirst(ClaimTypes.Role)!.Value);
        try {
            var orgId = await eventService.ResolveOrganizerAsync(requestId, requestRole, dto.OrganizerId);
            var newEvent = await eventService.CreateAsync(new CreateEventRequest {
                Title = dto.Title,
                Description = dto.Description,
                StartsAt = dto.StartsAt,
                Location = dto.Location,
                OrganizerId = orgId,
                ImageUrl = dto.ImageUrl,
                Highlights = dto.Highlights,
                Category = dto.Category,
                EndsAt = dto.EndsAt,
                DoorsOpenAt = dto.DoorsOpenAt,
                AgeRestriction = dto.AgeRestriction,
                RefundPolicy = dto.RefundPolicy
            });
            return CreatedAtAction(nameof(GetById), new { id = newEvent.Id }, newEvent);
        } catch (ArgumentException e) {
            return BadRequest(new { message = e.Message });
        }
    }

    [Authorize(Roles = "Admin, Organizer")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id) {
        var requestId = User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value;
        var requestRole = Enum.Parse<UserRole>(User.FindFirst(ClaimTypes.Role)!.Value);

        try {
            return await eventService.DeleteAsync(requestId, requestRole, id) ? NoContent() : NotFound();
        } catch (UnauthorizedAccessException) {
            return Forbid();
        } catch (EventHasBookingsException e) {
            return Conflict(new { message = e.Message });
        }
    }

    /// <summary>Bookings by status, revenue, tickets sold and people checked in. The event's organizer or an admin.</summary>
    [Authorize(Roles = "Admin, Organizer")]
    [HttpGet("{id}/stats")]
    public async Task<ActionResult<EventStatsDto>> GetStats(string id) {
        var requestId = User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value;
        var requestRole = Enum.Parse<UserRole>(User.FindFirst(ClaimTypes.Role)!.Value);

        try {
            var stats = await eventService.GetStatsAsync(requestId, requestRole, id);
            return stats == null ? NotFound() : Ok(stats);
        } catch (UnauthorizedAccessException) {
            return Forbid();
        }
    }

    [Authorize(Roles = "Admin, Organizer")]
    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id, UpdateEventDto dto) {
        var requestId = User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value;
        var requestRole = Enum.Parse<UserRole>(User.FindFirst(ClaimTypes.Role)!.Value);
        
        try {
            var updated = await eventService.UpdateAsync(requestId, requestRole, id, new UpdateEventRequest {
                Title = dto.Title,
                ImageUrl = dto.ImageUrl,
                Description = dto.Description,
                StartsAt = dto.StartsAt,
                Location = dto.Location,
                Category = dto.Category,
                EndsAt = dto.EndsAt,
                DoorsOpenAt = dto.DoorsOpenAt,
                AgeRestriction = dto.AgeRestriction,
                RefundPolicy = dto.RefundPolicy,
                Highlights = dto.Highlights
            });
            return updated == null ? NotFound() : Ok(updated);
        } catch (UnauthorizedAccessException) {
            return Forbid();
        } catch (ArgumentException e) {
            return BadRequest(new { message = e.Message });
        }
    }
}