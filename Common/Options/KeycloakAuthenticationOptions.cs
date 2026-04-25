namespace Common.Options;

public sealed class KeycloakAuthenticationOptions {
    public string? Issuer { get; set; }
    public string? MetadataAddress { get; set; }
}
