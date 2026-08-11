using EventHub.Core.Entities;
using EventHub.Core.Services;
using EventHub.Core.Services.Models;
using EventHub.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Controllers;

[ApiController]
[Route("users")]
public class UsersController(UserService userService): ControllerBase {
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] PaginationQuery query) {
        var result = await userService.GetPagedAsync(query.Page, query.PageSize);
        return Ok(result);
    }
    
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<List<User>>> GetById(Guid id) {
        var user = await userService.GetByIdAsync(id);
        if (user == null) return NotFound();
        return Ok(user);
    }

    [HttpPost]
    public async Task<ActionResult<User>> Create(CreateUserDto dto) {
        var user = await userService.CreateAsync(new CreateUserRequest {
            Email = dto.Email,
            Password = dto.Password,
            FullName = dto.FullName,
        });
        return Ok(user);
    }
    
    [HttpPatch("{id:guid}/become-organizer")]
    public async Task<ActionResult<User>> BecomeOrganizer(Guid id) {
        var user = await userService.BecomeOrganizerAsync(id);
        if (user == null) return NotFound();
        return Ok(user);
    }
}