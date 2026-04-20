using System.Text.Json.Serialization;

namespace AccountService.DTOs.Users;

public sealed class UpdateMyProfileDto {
    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; set; }
}
