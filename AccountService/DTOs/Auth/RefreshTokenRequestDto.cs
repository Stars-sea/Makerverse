using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AccountService.DTOs.Auth;

public sealed class RefreshTokenRequestDto {
    [Required]
    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = string.Empty;
}
