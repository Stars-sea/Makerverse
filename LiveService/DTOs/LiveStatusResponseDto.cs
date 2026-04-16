namespace LiveService.DTOs;

public record LiveStatusResponseDto(
    string PushUrl,
    string PullUrl,
    string? Passphrase
);
