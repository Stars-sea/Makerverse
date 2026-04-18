namespace LiveService.DTOs;

public record LivestreamEndpointDto(
    string? PushUrl,
    string PullUrl,
    string? Passphrase
);

public record VodEndpointDto(
    string Url
);
