using System.IdentityModel.Tokens.Jwt;
using EventHub.Core.Common;
using EventHub.Core.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Users;

[ApiController]
[Route("users")]
[Authorize(Roles = "Admin")]
public class UsersController(UserService userService): ControllerBase {
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] PaginationQuery query) {
        var result = await userService.GetPagedAsync(query.Page, query.PageSize); 
        var response = result.Map(UserResponseDto.FromEntity);
        return Ok(response);
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<UserResponseDto>> GetById(string id) {
        var user = await userService.GetByIdAsync(id);
        if (user == null) return NotFound();
        return Ok(UserResponseDto.FromEntity(user));
    }

    [HttpPost]
    public async Task<ActionResult<UserResponseDto>> Create(CreateUserDto dto) {
        try {
            var user = await userService.CreateAsync(new CreateUserRequest {
                Email = dto.Email,
                Password = dto.Password,
                FullName = dto.FullName,
            });
            return Ok(UserResponseDto.FromEntity(user));
        } catch (EmailTakenException e) {
            return Conflict(new { message = e.Message });
        }
    }
    
    [HttpPatch("{id}/role")]
    public async Task<ActionResult<UserResponseDto>> ChangeRole(string id, UpdateUserRoleDto dto) {
        var requestId = User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value;

        try {
            var user = await userService.ChangeRoleAsync(requestId, id, dto.Role!.Value);
            return user == null ? NotFound() : Ok(UserResponseDto.FromEntity(user));
        } catch (UnauthorizedAccessException e) {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = e.Message });
        }
    }
}