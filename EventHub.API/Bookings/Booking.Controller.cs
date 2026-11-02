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
public class BookingsController(BookingService bookingService, IRateLimiter rateLimiter) : ControllerBase {
    [Authorize(Roles = "Admin, Attendee")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateBookingDto dto) {
        var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value;

        // Each booking holds tickets for 15 minutes, so a script could empty an event by
        // creating bookings in a loop. A real buyer never comes close to this.
        if (!await rateLimiter.IsAllowedAsync($"rateLimit:booking:{userId}", maxAttempts: 10, window: TimeSpan.FromMinutes(1))) {
            return StatusCode(429, new { message = "Too many booking attempts. Try again in a moment." });
        }

        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (idempotencyKey is { Length: > 100 }) {
            return BadRequest(new { message = "Idempotency-Key is too long" });
        }

        try {
            var result = await bookingService.CreateAsync(new CreateBookingRequest {
                UserId = userId,
                EventId = dto.EventId,
                Tickets = dto.Tickets,
                IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey
            });

            if (result == null) return Conflict(new { message = "Not enough tickets remaining" });

            var response = await bookingService.ToResponseAsync(result.Booking, result.ClientSecret);
            // A replay is not a new resource, so 200 rather than 201.
            if (result.Replayed) return Ok(response);
            return CreatedAtAction(nameof(GetById), new { id = result.Booking.Id }, response);
        }
        catch (KeyNotFoundException e) {
            return NotFound(new { message = e.Message });
        }
        catch (ArgumentException e) {
            return BadRequest(new { message = e.Message });
        }
        catch (BookingInProgressException e) {
            return Conflict(new { message = e.Message });
        }
    }
    
    // Attendee: own bookings only
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id) {
        var requestId = User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value;
        var requestRole = Enum.Parse<UserRole>(User.FindFirst(ClaimTypes.Role)!.Value);

        try {  
            var booking = await bookingService.GetByIdAsync(id, requestId,  requestRole);
            if (booking == null) return NotFound();
            return Ok(await bookingService.ToResponseAsync(booking));
        }
        catch (UnauthorizedAccessException) {
            return Forbid();
        }
    }
    
    // Admins can book (see Create), so they need to see their own bookings too.
    [Authorize(Roles = "Admin, Attendee")]
    [HttpGet("me")]
    public async Task<IActionResult> GetMyBookings([FromQuery] PaginationQuery query) {
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub)!;
        var userId = userIdClaim.Value;

        var result = await bookingService.GetPagedByUserIdAsync(userId, query.Page, query.PageSize);
        return Ok(new PagedResult<BookingResponseDto> {
            Items = await bookingService.ToResponsesAsync(result.Items),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount
        });
    }
    
    [Authorize(Roles = "Admin, Organizer")]
    [HttpGet("by-event/{eventId}")]
    public async Task<IActionResult> GetByEvent([FromQuery] PaginationQuery query, string eventId) {
        var result = await bookingService.GetPagedByEventIdAsync(eventId, query.Page, query.PageSize);
        return Ok(new PagedResult<BookingResponseDto> {
            Items = await bookingService.ToResponsesAsync(result.Items),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount
        });
    }
    
    /// <summary>Checks the payment with Stripe and confirms the booking if it was paid.</summary>
    [Authorize(Roles = "Admin, Attendee")]
    [HttpPost("{id}/confirm")]
    public async Task<IActionResult> Confirm(string id) {
        var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value;

        try {
            var booking = await bookingService.ConfirmPaymentAsync(id, userId);
            return booking == null ? NotFound() : Ok(await bookingService.ToResponseAsync(booking));
        }
        catch (UnauthorizedAccessException) {
            return Forbid();
        }
    }

    [Authorize(Roles = "Admin, Attendee")]
    [HttpPatch("{id}/cancel")]
    public async Task<IActionResult> Cancel(string id) {
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub);
        if (userIdClaim == null) return Unauthorized();
        var userId = userIdClaim.Value;

        try {
            var booking = await bookingService.CancelAsync(id, userId);
            return booking == null ? NotFound() : Ok(await bookingService.ToResponseAsync(booking));
        }
        catch (UnauthorizedAccessException) {
            return Forbid();
        }
        catch (InvalidOperationException ex) {
            return BadRequest(new { message = ex.Message });
        }
    }
}