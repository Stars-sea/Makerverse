using System.Text.Json.Serialization;

namespace AccountService.Services;

public sealed class KeycloakUserRepresentation {
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("emailVerified")]
    public bool EmailVerified { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; set; }

    [JsonPropertyName("attributes")]
    public Dictionary<string, List<string>>? Attributes { get; set; }

    [JsonPropertyName("credentials")]
    public List<KeycloakCredentialRepresentation>? Credentials { get; set; }

    public string? GetAttribute(string key) {
        if (Attributes is null || !Attributes.TryGetValue(key, out List<string>? values) || values.Count == 0)
            return null;

        return values[0];
    }
}

public sealed class KeycloakCredentialRepresentation {
    [JsonPropertyName("type")]
    public string Type { get; set; } = "password";

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("temporary")]
    public bool Temporary { get; set; }
}

public sealed class KeycloakTokenPayload {
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;
}
