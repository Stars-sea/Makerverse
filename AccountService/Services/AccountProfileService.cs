using System.Security.Claims;
using AccountService.DTOs.Avatars;
using AccountService.DTOs.Users;

namespace AccountService.Services;

public sealed class AccountProfileService {
    public UserProfileDto ToUserProfile(KeycloakUserRepresentation user, ClaimsPrincipal principal) {
        return new UserProfileDto {
            Id            = user.Id ?? string.Empty,
            Username      = user.Username,
            Email         = user.Email,
            EmailVerified = user.EmailVerified,
            FirstName     = user.FirstName,
            LastName      = user.LastName,
            AvatarUrl     = string.IsNullOrWhiteSpace(user.Id) ? null : $"/account/users/{user.Id}/avatar",
            AvatarVersion = null,
            Roles = principal.Claims
                .Where(claim => claim.Type is ClaimTypes.Role or "roles")
                .Select(claim => claim.Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };
    }

    public SimpleUserProfileDto ToSimpleUserProfile(KeycloakUserRepresentation user) {
        return new SimpleUserProfileDto {
            Id        = user.Id ?? string.Empty,
            Username  = user.Username,
            FirstName = user.FirstName,
            LastName  = user.LastName,
            AvatarUrl = string.IsNullOrWhiteSpace(user.Id) ? null : $"/account/users/{user.Id}/avatar"
        };
    }

    public AvatarUploadResponseDto ToAvatarUploadResponse(string userId, StoredAvatar avatar) {
        return new AvatarUploadResponseDto {
            AvatarUrl   = $"/account/users/{userId}/avatar?v={avatar.Version}",
            ContentType = avatar.ContentType,
            Size        = avatar.Size,
            Version     = avatar.Version
        };
    }
}
