using EventHub.Core.Common;
using EventHub.Core.Enums;
using EventHub.Core.Services;
using EventHub.Core.Services.Models;
using EventHub.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Controllers;

[ApiController]
[Route("users")]
[Authorize(Roles = "Admin")]
public class UsersController(UserService userService): ControllerBase {
    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] PaginationQuery query) {
        var result = await userService.GetPagedAsync(query.Page, query.PageSize); 
        var response = result.Map(UserResponseDto.FromEntity);
        return Ok(response);
    }
    
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserResponseDto>> GetById(Guid id) {
        var user = await userService.GetByIdAsync(id);
        if (user == null) return NotFound();
        return Ok(UserResponseDto.FromEntity(user));
    }

    [HttpPost]
    public async Task<ActionResult<UserResponseDto>> Create(CreateUserDto dto) {
        var user = await userService.CreateAsync(new CreateUserRequest {
            Email = dto.Email,
            Password = dto.Password,
            FullName = dto.FullName,
        });
        return Ok(UserResponseDto.FromEntity(user));
    }
    
    [HttpPatch("{id:guid}/become-organizer")]
    public async Task<ActionResult<UserResponseDto>> BecomeOrganizer(Guid id) {
        var user = await userService.BecomeOrganizerAsync(id);
        if (user == null) return NotFound();
        return Ok(UserResponseDto.FromEntity(user));
    }
}