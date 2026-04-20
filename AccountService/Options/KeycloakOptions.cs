namespace AccountService.Options;

public sealed class KeycloakOptions {
    public string Realm { get; set; } = "makerverse";
    public string PublicClientId { get; set; } = "makerverse";
}
