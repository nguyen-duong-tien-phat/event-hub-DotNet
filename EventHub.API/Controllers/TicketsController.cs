using EventHub.Core.Services;
using EventHub.Core.Services.Models;
using EventHub.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Controllers;

[ApiController]
[Route("tickets")]
[Authorize(Roles = "Admin, Organizer")]
public class TicketsController(TicketService ticketService) : ControllerBase {
    [HttpGet("by-event/{eventId}")]
    public async Task<IActionResult> GetByEvent([FromQuery] PaginationQuery query, Guid eventId) {
        var result = await ticketService.GetPagedByEventIdAsync(eventId,  query.Page,  query.PageSize);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id) {
        var ticket = await ticketService.GetByIdAsync(id);
        return ticket == null ? NotFound() : Ok(ticket);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateTicketDto dto) {
        var ticket = await ticketService.CreateAsync(new CreateTicketRequest {
            EventId = dto.EventId,
            Type = dto.Type,
            Price = dto.Price,
            TotalQuantity = dto.TotalQuantity
        });
        return CreatedAtAction(nameof(GetById), new { id = ticket.Id }, ticket);
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateTicketDto dto) {
        var updated = await ticketService.UpdateAsync(id, new UpdateTicketRequest {
            Type = dto.Type,
            Price = dto.Price,
            TotalQuantity = dto.TotalQuantity
        });
        return updated == null ? NotFound() : Ok(updated);
    }
}