namespace AccountService.Options;

public sealed class KeycloakAdminOptions {
    public string ClientId { get; set; } = "makerverse-account-service";
    public string ClientSecret { get; set; } = string.Empty;
}
