using System.Security.Claims;
using AccountService.DTOs.Avatars;
using AccountService.Services;
using Common;
using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Minio.DataModel;

namespace AccountService.Controllers;

[ApiController]
[Route("account/users")]
public sealed class AvatarsController(
    KeycloakAdminService adminService,
    AvatarStorageService storageService,
    AccountProfileService profileService
) : ControllerBase {
    [Authorize]
    [HttpPost("me/avatar")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ActionResult<AvatarUploadResponseDto>> UploadMyAvatar(IFormFile file, CancellationToken ct) {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest("Cannot get user details.");

        var userResult = await adminService.GetUserByIdAsync(userId, ct);
        if (userResult.IsError)
            return userResult.Errors[0].ToActionResult();

        var uploadResult = await storageService.UploadAsync(userId, file, ct);
        if (uploadResult.IsError)
            return uploadResult.Errors[0].ToActionResult();

        var updateResult = await adminService.UpdateAvatarAsync(userId, uploadResult.Value, ct);
        if (updateResult.IsError)
            return updateResult.Errors[0].ToActionResult();

        Error? cleanupError = await storageService.DeleteOtherVariantsAsync(userId, uploadResult.Value.ObjectKey, ct);
        if (cleanupError is Error cleanup)
            return cleanup.ToActionResult();

        return Ok(profileService.ToAvatarUploadResponse(userId, uploadResult.Value));
    }

    [AllowAnonymous]
    [HttpGet("{userId}/avatar")]
    public async Task GetAvatar(string userId, CancellationToken ct) {
        var storedAvatar = await storageService.FindByUserIdAsync(userId, ct);
        if (storedAvatar is null) {
            Response.StatusCode = 404;
            return;
        }

        var statResult = await storageService.GetStatAsync(storedAvatar.ObjectKey, ct);
        if (statResult.IsError) {
            Response.StatusCode = 404;
            return;
        }

        ObjectStat stat = statResult.Value;
        Response.ContentType          = storedAvatar.ContentType;
        Response.ContentLength        = stat.Size;
        Response.Headers.AcceptRanges = "bytes";
        Response.Headers.ETag         = stat.ETag;
        Response.Headers.LastModified = stat.LastModified.ToString("R");
        Response.Headers.CacheControl = "public, max-age=3600";

        var readError = await storageService.WriteToStreamAsync(storedAvatar.ObjectKey, Response.Body, ct);
        if (readError is not null) {
            Response.StatusCode = 404;
        }
    }

    [Authorize]
    [HttpDelete("me/avatar")]
    public async Task<IActionResult> DeleteMyAvatar(CancellationToken ct) {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest("Cannot get user details.");

        var userResult = await adminService.GetUserByIdAsync(userId, ct);
        if (userResult.IsError)
            return userResult.Errors[0].ToActionResult();

        var updateResult = await adminService.RemoveAvatarAsync(userId, ct);
        if (updateResult.IsError)
            return updateResult.Errors[0].ToActionResult();

        Error? deleteError = await storageService.DeleteByUserIdAsync(userId, ct);
        if (deleteError is Error error)
            return error.ToActionResult();

        return NoContent();
    }
}
