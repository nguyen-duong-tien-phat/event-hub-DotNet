using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EventHub.Core.Bookings;
using EventHub.Core.Common;
using EventHub.Core.Users;

namespace EventHub.Bookings;

[ApiController]
[Route("bookings")]
[Authorize]
public class BookingsController(BookingService bookingService) : ControllerBase {
    [Authorize(Roles = "Admin, Attendee")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateBookingDto dto) {
        var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value;

        var result = await bookingService.CreateAsync(new CreateBookingRequest {
            UserId = userId,
            EventId = dto.EventId,
            Tickets = dto.Tickets
        });

        if (result == null) return Conflict(new { message = "Not enough tickets remaining" });

        return CreatedAtAction(nameof(GetById), new { id = result.Booking.Id }, BookingResponseDto.FromEntity(result.Booking));
    }
    
    // Attendee: own bookings only
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id) {
        var requestId = User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value;
        var requestRole = Enum.Parse<UserRole>(User.FindFirst(ClaimTypes.Role)!.Value);

        try {  
            var booking = await bookingService.GetByIdAsync(id, requestId,  requestRole);
            if (booking == null) return NotFound();
            return Ok(BookingResponseDto.FromEntity(booking));
        }
        catch (UnauthorizedAccessException) {
            return Forbid();
        }
    }
    
    [Authorize(Roles = "Attendee")]
    [HttpGet("me")]
    public async Task<IActionResult> GetMyBookings([FromQuery] PaginationQuery query) {
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub)!;
        var userId = userIdClaim.Value;

        var result = await bookingService.GetPagedByUserIdAsync(userId, query.Page, query.PageSize);
        return Ok(result.Map(BookingResponseDto.FromEntity));
    }
    
    [Authorize(Roles = "Admin, Organizer")]
    [HttpGet("by-event/{eventId}")]
    public async Task<IActionResult> GetByEvent([FromQuery] PaginationQuery query, string eventId) {
        var result = await bookingService.GetPagedByEventIdAsync(eventId, query.Page, query.PageSize);
        return Ok(result.Map(BookingResponseDto.FromEntity));
    }
    
    [Authorize(Roles = "Admin, Attendee")]
    [HttpPatch("{id}/cancel")]
    public async Task<IActionResult> Cancel(string id) {
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub);
        if (userIdClaim == null) return Unauthorized();
        var userId = userIdClaim.Value;

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