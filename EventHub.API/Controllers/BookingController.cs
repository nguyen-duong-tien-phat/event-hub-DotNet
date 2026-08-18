using EventHub.Core.Services;
using EventHub.Core.Services.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using EventHub.DTOs;

namespace EventHub.Controllers;

[ApiController]
[Route("bookings")]
[Authorize(Roles = "Admin, Attendee")]
public class BookingsController(BookingService bookingService) : ControllerBase {
    [Authorize(Roles = "Admin, Attendee")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateBookingDto dto) {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

        var result = await bookingService.CreateAsync(new CreateBookingRequest {
            UserId = userId,
            TicketId = dto.TicketId,
            Quantity = dto.Quantity
        });

        if (result == null) return Conflict(new { message = "Not enough tickets remaining" });

        return CreatedAtAction(nameof(GetById), new { id = result.Booking.Id }, BookingResponseDto.FromEntity(result.Booking));
    }

    [Authorize(Roles = "Admin, Organizer")]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id) {
        var booking = await bookingService.GetByIdAsync(id);
        return booking == null ? NotFound() : Ok(BookingResponseDto.FromEntity(booking));
    }
    
    [Authorize(Roles = "Attendee")]
    [HttpGet("me")]
    public async Task<IActionResult> GetMyBookings([FromQuery] PaginationQuery query) {
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub)!;
        var userId = Guid.Parse(userIdClaim.Value);

        var result = await bookingService.GetPagedByUserIdAsync(userId, query.Page, query.PageSize);
        return Ok(result.Map(BookingResponseDto.FromEntity));
    }
    
    [Authorize(Roles = "Admin, Organizer")]
    [HttpGet("by-event/{eventId}")]
    public async Task<IActionResult> GetByEvent([FromQuery] PaginationQuery query, Guid eventId) {
        var result = await bookingService.GetPagedByEventIdAsync(eventId, query.Page, query.PageSize);
        return Ok(result.Map(BookingResponseDto.FromEntity));
    }
    
    [HttpPatch("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id) {
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub);
        if (userIdClaim == null) return Unauthorized();
        var userId = Guid.Parse(userIdClaim.Value);

        try {
            var booking = await bookingService.CancelAsync(id, userId);
            return booking == null ? NotFound() : Ok(BookingResponseDto.FromEntity(booking));
        }
        catch (UnauthorizedAccessException) {
            return Forbid();
        }
        catch (InvalidOperationException ex) {
            return BadRequest(new { message = ex.Message });
        }
    }
}