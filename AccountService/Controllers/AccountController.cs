using AuthService.DTOs;
using AuthService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.Controllers;

[ApiController]
[Route("[controller]")]
public class AccountController(
    UserManager<ApplicationUser> userManager
) : ControllerBase {

    /// <summary>
    /// 用户注册
    /// </summary>
    [HttpPost("register")]
    public async Task<ActionResult<UserProfileDto>> Register([FromBody] RegisterDto dto) {
        if (await userManager.FindByNameAsync(dto.Username) is not null)
            return Conflict(new {
                Error            = "UserAlreadyExists",
                ErrorDescription = "The username is already taken."
            });

        if (await userManager.FindByEmailAsync(dto.Email) is not null)
            return Conflict(new {
                Error            = "UserAlreadyExists",
                ErrorDescription = "The email is already taken."
            });

        ApplicationUser user = new() {
            UserName = dto.Username,
            Email    = dto.Email,
        };

        IdentityResult result = await userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
            return BadRequest(new {
                Error            = "RegistrationFailed",
                ErrorDescription = result.Errors.Select(e => e.Description)
            });

        result = await userManager.AddToRoleAsync(user, "Member");
        if (!result.Succeeded) {
            await userManager.DeleteAsync(user);
            return BadRequest(new {
                Error            = "RegistrationFailed",
                ErrorDescription = result.Errors.Select(e => e.Description)
            });
        }

        return CreatedAtAction(nameof(GetProfile),
            new UserProfileDto(
                user.Id,
                user.UserName,
                user.Email,
                user.PhoneNumber
            )
        );
    }

    /// <summary>
    /// 获取个人资料
    /// </summary>
    [Authorize]
    [HttpGet("profile")]
    public async Task<ActionResult<UserProfileDto>> GetProfile() {
        if (await userManager.GetUserAsync(User) is not {} user)
            return NotFound();

        return new UserProfileDto(
            user.Id,
            user.UserName!,
            user.Email!,
            user.PhoneNumber
        );
    }

    /// <summary>
    /// 更新个人资料
    /// </summary>
    [Authorize]
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto) {
        if (await userManager.GetUserAsync(User) is not {} user)
            return NotFound();

        var changed = false;

        if (!string.IsNullOrEmpty(dto.Email) && dto.Email != user.Email) {
            ApplicationUser? existingUser = await userManager.FindByEmailAsync(dto.Email);
            if (existingUser != null && existingUser.Id != user.Id)
                return Conflict(new {
                    Error            = "UserAlreadyExists",
                    ErrorDescription = "The email is already taken."
                });

            await userManager.SetEmailAsync(user, dto.Email);
            changed = true;
        }

        if (dto.PhoneNumber != user.PhoneNumber) {
            await userManager.SetPhoneNumberAsync(user, dto.PhoneNumber);
            changed = true;
        }

        if (!changed)
            return Ok(new {
                Message = "No changes were made."
            });

        IdentityResult result = await userManager.UpdateAsync(user);

        if (result.Succeeded)
            return Ok(new {
                Message = "Profile updated successfully."
            });

        var errors = result.Errors.Select(e => e.Description);
        return BadRequest(new {
            Error            = "UpdateFailed",
            ErrorDescription = errors
        });
    }
}
