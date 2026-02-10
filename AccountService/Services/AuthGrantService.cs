using System.Security.Claims;
using AuthService.Models;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AuthService.Services;

public class AuthGrantService(
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictScopeManager scopeManager,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager
) : IAuthGrantService {

    public async Task<AuthResult> AuthenticatePasswordGrantAsync(OpenIddictRequest request) {
        if (await userManager.FindByNameAsync(request.Username ?? string.Empty) is not {} user ||
            !await userManager.CheckPasswordAsync(user, request.Password ?? string.Empty))
            return AuthResult.Failed(Errors.InvalidGrant, "The username/password couple is invalid.");

        if (!await signInManager.CanSignInAsync(user))
            return AuthResult.Failed(Errors.InvalidGrant, "The user is no longer allowed to sign in.");

        ClaimsPrincipal principal = await signInManager.CreateUserPrincipalAsync(user);

        principal.SetScopes(request.GetScopes());

        var resources = await scopeManager.ListResourcesAsync(principal.GetScopes()).ToListAsync();
        principal.SetResources(resources);

        // Set the destinations for the claims based on the scopes granted to the client application.
        foreach (Claim claim in principal.Claims)
            claim.SetDestinations(GetDestinations(claim, principal));

        return AuthResult.Success(principal);
    }

    public async Task<AuthResult> AuthenticateClientCredentialsGrantAsync(OpenIddictRequest request) {
        if (request.ClientId is null || await applicationManager.FindByClientIdAsync(request.ClientId) is not {} application)
            return AuthResult.Failed(Errors.InvalidClient, "Not found application details.");

        ClaimsIdentity identity = new(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            Claims.Name,
            Claims.Role
        );

        // Use the client_id as the subject identifier.
        identity.SetClaim(Claims.Subject, await applicationManager.GetClientIdAsync(application));
        identity.SetClaim(Claims.Name, await applicationManager.GetDisplayNameAsync(application));

        identity.SetDestinations(static claim => claim.Type switch {
            Claims.Name when claim.Subject != null && claim.Subject.HasScope(Permissions.Scopes.Profile)
                => [Destinations.AccessToken, Destinations.IdentityToken],

            _ => [Destinations.AccessToken]
        });

        return AuthResult.Success(new ClaimsPrincipal(identity));
    }

    public async Task<AuthResult> AuthenticateRefreshTokenGrantAsync(ClaimsPrincipal principal, OpenIddictRequest request) {
        // Retrieve the user profile corresponding to the authorization code/refresh token.
        if (await userManager.GetUserAsync(principal) is not {} user)
            return AuthResult.Failed(Errors.InvalidGrant, "The token is no longer valid.");

        // Ensure the user is still allowed to sign in.
        if (!await signInManager.CanSignInAsync(user))
            return AuthResult.Failed(Errors.InvalidGrant, "The user is no longer allowed to sign in.");

        // Create a new authentication principal from the user details.
        // This process automatically retrieves the latest claims (roles, email, etc.).
        ClaimsPrincipal newPrincipal = await signInManager.CreateUserPrincipalAsync(user);

        // Set the list of scopes granted to the client application.
        newPrincipal.SetScopes(principal.GetScopes());

        var resources = await scopeManager.ListResourcesAsync(newPrincipal.GetScopes()).ToListAsync();
        newPrincipal.SetResources(resources);

        foreach (Claim claim in newPrincipal.Claims) {
            claim.SetDestinations(GetDestinations(claim, newPrincipal));
        }

        return AuthResult.Success(newPrincipal);
    }

    private static IEnumerable<string> GetDestinations(Claim claim, ClaimsPrincipal principal) {
        switch (claim.Type) {
            case Claims.Name:
                yield return Destinations.AccessToken;
                if (principal.HasScope(Scopes.Profile)) yield return Destinations.IdentityToken;
                yield break;

            case Claims.Email:
                yield return Destinations.AccessToken;
                if (principal.HasScope(Scopes.Email)) yield return Destinations.IdentityToken;
                yield break;

            case Claims.Role:
                yield return Destinations.AccessToken;
                if (principal.HasScope(Scopes.Roles)) yield return Destinations.IdentityToken;
                yield break;

            case Claims.Subject:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;

            case "asp_net_core_identity_security_stamp":
                yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
