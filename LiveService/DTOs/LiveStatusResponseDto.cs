namespace LiveService.DTOs;

public record LiveStatusResponseDto(
    string UploadUrl,
    string Passphrase
);
