using EventHub.Core.Services;
using EventHub.Core.Services.Models;
using EventHub.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Controllers;

[ApiController]
[Route("tickets")]
public class TicketsController(TicketService ticketService) : ControllerBase {
    // GET api/tickets/by-event/{eventId}
    [HttpGet("by-event/{eventId}")]
    public async Task<IActionResult> GetByEvent(Guid eventId) {
        var tickets = await ticketService.GetByEventIdAsync(eventId);
        return Ok(tickets);
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