using System.Text.Json.Serialization;

namespace AuthService.DTOs;

/// <summary>
/// 用户信息响应模型
/// </summary>
public record UserInfoResponseDto(
    [property: JsonPropertyName("sub")] string Subject, // Keep special mapping for "sub" as it doesn't match snake_case of "Subject"
    string? PreferredUsername = null,
    string? Email = null,
    bool? EmailVerified = null,
    IList<string>? Roles = null
);
