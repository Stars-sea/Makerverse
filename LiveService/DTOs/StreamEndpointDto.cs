namespace LiveService.DTOs;

public record StreamEndpointDto(
    string PushUrl,
    string PullUrl,
    string? Passphrase
);
