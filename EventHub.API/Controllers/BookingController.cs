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

        var booking = await bookingService.CreateAsync(new CreateBookingRequest {
            UserId = userId,
            TicketId = dto.TicketId,
            Quantity = dto.Quantity
        });

        if (booking == null) return Conflict(new { message = "Not enough tickets remaining" });

        return CreatedAtAction(nameof(GetById), new { id = booking.Id }, BookingResponseDto.FromEntity(booking));
    }

    [Authorize(Roles = "Admin, Organizer")]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id) {
        var booking = await bookingService.GetByIdAsync(id);
        return booking == null ? NotFound() : Ok(BookingResponseDto.FromEntity(booking));
    }
    
    [Authorize(Roles = "Admin, Attendee")]
    [HttpGet("me")]
    public async Task<IActionResult> GetMyBookings() {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var bookings = await bookingService.GetByUserIdAsync(userId);
        return Ok(bookings.Select(BookingResponseDto.FromEntity));
    }
    
    [Authorize(Roles = "Admin, Organizer")]
    [HttpGet("by-event/{eventId}")]
    public async Task<IActionResult> GetByEvent(Guid eventId) {
        var bookings = await bookingService.GetByEventIdAsync(eventId);
        return Ok(bookings.Select(BookingResponseDto.FromEntity));
    }
}