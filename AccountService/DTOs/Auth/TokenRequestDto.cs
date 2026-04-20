using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AccountService.DTOs.Auth;

public sealed class TokenRequestDto {
    [Required]
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}
