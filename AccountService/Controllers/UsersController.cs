using System.Security.Claims;
using AccountService.DTOs.Users;
using AccountService.Services;
using Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AccountService.Controllers;

[ApiController]
[Route("account/users")]
public sealed class UsersController(
    KeycloakAdminService adminService,
    AccountProfileService profileService
) : ControllerBase {
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<UserProfileDto>> Register([FromBody] RegisterUserDto request, CancellationToken ct) {
        var result = await adminService.RegisterUserAsync(request, ct);
        if (result.IsError)
            return result.Errors[0].ToActionResult();

        return Ok(profileService.ToUserProfile(result.Value, new ClaimsPrincipal()));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserProfileDto>> GetMe(CancellationToken ct) {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest("Cannot get user details.");

        var result = await adminService.GetUserByIdAsync(userId, ct);
        if (result.IsError)
            return result.Errors[0].ToActionResult();

        return Ok(profileService.ToUserProfile(result.Value, User));
    }

    [AllowAnonymous]
    [HttpGet("{userId}")]
    public async Task<ActionResult<SimpleUserProfileDto>> GetUserProfileById(string userId, CancellationToken ct) {
        var result = await adminService.GetUserByIdAsync(userId, ct);
        if (result.IsError)
            return result.Errors[0].ToActionResult();

        return Ok(profileService.ToSimpleUserProfile(result.Value));
    }

    [Authorize]
    [HttpPut("me")]
    public async Task<ActionResult<UserProfileDto>> UpdateMe([FromBody] UpdateMyProfileDto request, CancellationToken ct) {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest("Cannot get user details.");

        var result = await adminService.UpdateUserProfileAsync(userId, request, ct);
        if (result.IsError)
            return result.Errors[0].ToActionResult();

        return Ok(profileService.ToUserProfile(result.Value, User));
    }
}
