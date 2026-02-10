using System.ComponentModel.DataAnnotations;
using AuthService.Validators;
using OpenIddict.EntityFrameworkCore.Models;

namespace AuthService.DTOs;

public record RegisterClientDto(
    [Required] string ClientId,
    string? ClientSecret,
    [Required] string DisplayName,
    string? RedirectUri,
    string? PostLogoutRedirectUri,
    [PermissionsValidator] HashSet<string>? Permissions
);

public record RegisterClientResponseDto(
    string? ClientId,
    string? DisplayName,
    string? ClientType,
    string? ClientSecret
) {
    public RegisterClientResponseDto(OpenIddictEntityFrameworkCoreApplication application)
        : this(application.ClientId, application.DisplayName, application.ClientType, application.ClientSecret) { }
}
