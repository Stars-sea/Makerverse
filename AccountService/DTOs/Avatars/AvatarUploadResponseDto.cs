using System.Text.Json.Serialization;

namespace AccountService.DTOs.Avatars;

public sealed class AvatarUploadResponseDto {
    [JsonPropertyName("avatarUrl")]
    public string AvatarUrl { get; init; } = string.Empty;

    [JsonPropertyName("contentType")]
    public string ContentType { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;
}
