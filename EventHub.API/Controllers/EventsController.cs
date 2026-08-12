using EventHub.Core.Enums;
using EventHub.Core.Services;
using EventHub.Core.Services.Models;
using EventHub.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Controllers;

[ApiController]
[Route("events")]
public class EventsController(EventService eventService) : ControllerBase {
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] PaginationQuery query) {
        var result = await eventService.GetPagedAsync(query.Page, query.PageSize);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id) {
        var ev = await eventService.GetByIdAsync(id);
        return ev == null ? NotFound() : Ok(ev);
    }

    [Authorize(Roles = "Admin, Organizer")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateEventDto dto) {
        var newEvent = await eventService.CreateAsync(new CreateEventRequest {
            Title = dto.Title,
            Description = dto.Description,
            StartsAt = dto.StartsAt,
            Location = dto.Location,
            OrganizerId = dto.OrganizerId
        });
        return CreatedAtAction(nameof(GetById), new { id = newEvent.Id }, newEvent);
    }

    [Authorize(Roles = "Admin, Organizer")]
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateEventDto dto) {
        var updated = await eventService.UpdateAsync(id, new UpdateEventRequest {
            Title = dto.Title,
            Description = dto.Description,
            StartsAt = dto.StartsAt,
            Location = dto.Location
        });
        return updated == null ? NotFound() : Ok(updated);
    }
}