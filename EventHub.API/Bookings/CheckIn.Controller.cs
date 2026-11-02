using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EventHub.Core.Bookings;
using EventHub.Core.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Bookings;

public class CheckInDto {
    [Required]
    [MaxLength(300)]
    public required string Code { get; set; }
}

[ApiController]
[Route("checkin")]
[Authorize(Roles = "Admin, Organizer")]
public class CheckInController(CheckInService checkInService) : ControllerBase {
    /// <summary>
    /// Scans a ticket's QR. The status code tells the scanner what to show: 200 let them in,
    /// 409 already used or not paid, 400 not a real ticket, 403 not your event.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CheckIn(CheckInDto dto) {
        var staffId = User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value;
        var role = Enum.Parse<UserRole>(User.FindFirst(ClaimTypes.Role)!.Value);

        var result = await checkInService.CheckInAsync(dto.Code, staffId, role);
        var body = new {
            status = result.Status.ToString(),
            eventTitle = result.EventTitle,
            ticketType = result.TicketType,
            number = result.Number,
            checkedInAt = result.CheckedInAt,
        };

        return result.Status switch {
            CheckInStatus.Admitted => Ok(body),
            CheckInStatus.AlreadyUsed or CheckInStatus.NotPaid => Conflict(body),
            CheckInStatus.Forbidden => StatusCode(403, body),
            _ => BadRequest(body),
        };
    }
}
