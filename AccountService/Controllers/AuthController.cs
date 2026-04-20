using AccountService.DTOs.Auth;
using AccountService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AccountService.Controllers;

[ApiController]
[Route("account/auth")]
public sealed class AuthController(KeycloakOidcService oidcService) : ControllerBase {
    [AllowAnonymous]
    [HttpPost("token")]
    public async Task<IActionResult> Login([FromBody] TokenRequestDto request, CancellationToken ct) {
        KeycloakResponse response = await oidcService.LoginAsync(request, ct);
        return BuildResponse(response);
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequestDto request, CancellationToken ct) {
        KeycloakResponse response = await oidcService.RefreshAsync(request, ct);
        return BuildResponse(response);
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequestDto request, CancellationToken ct) {
        KeycloakResponse response = await oidcService.LogoutAsync(request, ct);
        return BuildResponse(response);
    }

    [AllowAnonymous]
    [HttpGet("userinfo")]
    public async Task<IActionResult> UserInfo(CancellationToken ct) {
        string? authorization = Request.Headers.Authorization;
        if (string.IsNullOrWhiteSpace(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Unauthorized();

        string bearerToken = authorization[7..].Trim();
        KeycloakResponse response = await oidcService.GetUserInfoAsync(bearerToken, ct);
        return BuildResponse(response);
    }

    private IActionResult BuildResponse(KeycloakResponse response) {
        if (string.IsNullOrWhiteSpace(response.Content))
            return StatusCode(response.StatusCode);

        return new ContentResult {
            StatusCode = response.StatusCode,
            Content = response.Content,
            ContentType = response.ContentType ?? "application/json"
        };
    }
}
