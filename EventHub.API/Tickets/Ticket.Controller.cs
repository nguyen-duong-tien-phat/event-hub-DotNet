using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EventHub.Core.Common;
using EventHub.Core.Tickets;
using EventHub.Core.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Tickets;

[ApiController]
[Route("tickets")]
[Authorize(Roles = "Admin, Organizer")]
public class TicketsController(TicketService ticketService) : ControllerBase {
    [HttpGet("by-event/{eventId}")]
    public async Task<IActionResult> GetByEvent([FromQuery] PaginationQuery query, string eventId) {
        var result = await ticketService.GetPagedByEventIdAsync(eventId,  query.Page,  query.PageSize);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id) {
        var ticket = await ticketService.GetByIdAsync(id);
        return ticket == null ? NotFound() : Ok(ticket);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateTicketDto dto) {
        var requestId = User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value;
        var requestRole = Enum.Parse<UserRole>(User.FindFirst(ClaimTypes.Role)!.Value);

        try {
            var ticket = await ticketService.CreateAsync(requestId, requestRole, new CreateTicketRequest {
                EventId = dto.EventId,
                Type = dto.Type,
                Price = dto.Price,
                TotalQuantity = dto.TotalQuantity,
                Highlights = dto.Highlights,
                MaxPerOrder = dto.MaxPerOrder
            });
            return ticket == null ? NotFound() : CreatedAtAction(nameof(GetById), new { id = ticket.Id }, ticket);
        } catch (UnauthorizedAccessException) {
            return Forbid();
        }
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id, UpdateTicketDto dto) {
        var requestId = User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value;
        var requestRole = Enum.Parse<UserRole>(User.FindFirst(ClaimTypes.Role)!.Value);

        try {
            var updated = await ticketService.UpdateAsync(requestId, requestRole, id, new UpdateTicketRequest {
                Type = dto.Type,
                Price = dto.Price,
                TotalQuantity = dto.TotalQuantity
            });
            return updated == null ? NotFound() : Ok(updated);
        } catch (UnauthorizedAccessException) {
            return Forbid();
        } catch (ArgumentException e) {
            return BadRequest(new { message = e.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id) {
        var requestId = User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value;
        var requestRole = Enum.Parse<UserRole>(User.FindFirst(ClaimTypes.Role)!.Value);

        try {
            return await ticketService.DeleteAsync(requestId, requestRole, id) ? NoContent() : NotFound();
        } catch (UnauthorizedAccessException) {
            return Forbid();
        } catch (TicketHasSalesException e) {
            return Conflict(new { message = e.Message });
        }
    }
}