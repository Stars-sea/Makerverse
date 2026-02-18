namespace Contracts;

public record LiveTerminate(
    string LiveId,
    bool IsValidTransition,
    string? ErrorMessage
);
