namespace AuthService.DTOs;

public record UserProfileDto(
    string Id,
    string UserName,
    string Email,
    string? PhoneNumber
);

public record UpdateProfileDto(
    string? Email,
    string? PhoneNumber
);
