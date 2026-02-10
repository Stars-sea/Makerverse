using System.Security.Claims;

namespace AuthService.Services;

public record AuthResult(
    ClaimsPrincipal? Principal = null,
    string? Error = null,
    string? ErrorDescription = null
) {
    public bool Succeeded => Principal != null;
    public static AuthResult Success(ClaimsPrincipal principal) => new(Principal: principal);
    public static AuthResult Failed(string error, string description) => new(Error: error, ErrorDescription: description);
}

