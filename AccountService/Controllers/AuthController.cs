using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using AuthService.DTOs;
using AuthService.Models;
using AuthService.Services;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AuthService.Controllers;

[ApiController]
[Route("[controller]")]
public class AuthController(
    IAuthGrantService authGrantService,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager
) : ControllerBase {

    #region Endpoints

    /// <summary>
    /// Get an access token (Token Endpoint)
    /// </summary>
    [HttpPost("token")]
    [Produces("application/json")]
    public async Task<IActionResult> Exchange() {
        if (HttpContext.GetOpenIddictServerRequest() is not {} request)
            return BadRequest(new OpenIddictResponse {
                Error            = Errors.InvalidRequest,
                ErrorDescription = "The OpenIddict server request cannot be retrieved."
            });

        if (request.IsPasswordGrantType()) {
            AuthResult result = await authGrantService.AuthenticatePasswordGrantAsync(request);
            return !result.Succeeded
                ? ForbidOpenIddict(result.Error, result.ErrorDescription)
                : SignIn(result.Principal!, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsClientCredentialsGrantType()) {
            AuthResult result = await authGrantService.AuthenticateClientCredentialsGrantAsync(request);
            return !result.Succeeded
                ? ForbidOpenIddict(result.Error, result.ErrorDescription)
                : SignIn(result.Principal!, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType()) {
            ClaimsPrincipal? principal = (await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;
            if (principal is null) {
                return ForbidOpenIddict(Errors.InvalidGrant, "The token principal is missing.");
            }

            AuthResult result = await authGrantService.AuthenticateRefreshTokenGrantAsync(principal, request);
            return !result.Succeeded
                ? ForbidOpenIddict(result.Error, result.ErrorDescription)
                : SignIn(result.Principal!, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        return BadRequest(new OpenIddictResponse {
            Error            = Errors.UnsupportedGrantType,
            ErrorDescription = "The specified grant type is not supported."
        });
    }

    /// <summary>
    /// Get user information (Userinfo Endpoint)
    /// </summary>
    [Authorize]
    [HttpGet("userinfo")]
    public async Task<ActionResult<UserInfoResponseDto>> Userinfo() {
        AuthenticateResult result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (result.Principal is not {} claimsPrincipal)
            return Challenge(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        if (await userManager.GetUserAsync(claimsPrincipal) is not {} user)
            return Challenge(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: BuildProperties(Errors.InvalidToken, "The specified access token is bound to an account that no longer exists.")
            );

        UserInfoResponseDto response = new(
            Subject: await userManager.GetUserIdAsync(user)// sub is mandatory
        ) {
            Email             = User.HasScope(Scopes.Email) ? await userManager.GetEmailAsync(user) : null,
            EmailVerified     = User.HasScope(Scopes.Email) ? await userManager.IsEmailConfirmedAsync(user) : null,
            PreferredUsername = User.HasScope(Scopes.Profile) ? await userManager.GetUserNameAsync(user) : null,
            Roles             = User.HasScope(Scopes.Roles) ? await userManager.GetRolesAsync(user) : null
        };

        return Ok(response);
    }

    /// <summary>
    /// Log out the user and revoke the access token (End Session Endpoint)
    /// </summary>
    [HttpGet("logout")]
    public async Task<IActionResult> Logout() {
        // Ask ASP.NET Core Identity to delete the local and external cookies created
        // when the user agent is redirected from the external identity provider
        // after a successful authentication flow (e.g Google or Facebook).
        await signInManager.SignOutAsync();

        // Returning a SignOutResult will ask OpenIddict to redirect the user agent
        // to the post_logout_redirect_uri specified by the client application or to
        // the RedirectUri specified in the authentication properties if none was set.
        return SignOut(
            authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            properties: new AuthenticationProperties {
                RedirectUri = "/"
            }
        );
    }

    #endregion

    [NonAction]
    private ForbidResult ForbidOpenIddict(string? error, string? description) {
        return Forbid(
            authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            properties: BuildProperties(error, description)
        );
    }

    private static AuthenticationProperties BuildProperties(string? error, string? description) {
        return new AuthenticationProperties(new Dictionary<string, string?> {
            [OpenIddictServerAspNetCoreConstants.Properties.Error]            = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
        });
    }

}
