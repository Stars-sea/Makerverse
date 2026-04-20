namespace AccountService.Services;

public sealed record KeycloakResponse(
    int StatusCode,
    string Content,
    string? ContentType
);
