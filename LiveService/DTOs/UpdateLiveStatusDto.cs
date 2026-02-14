using LiveService.Validators;

namespace LiveService.DTOs;

public record UpdateLiveStatusDto(
    [UpdateStatusValidator] string Status
);
