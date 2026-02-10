using System.Security.Claims;
using OpenIddict.Abstractions;

namespace AuthService.Services;

public interface IAuthGrantService {

    Task<AuthResult> AuthenticatePasswordGrantAsync(OpenIddictRequest request);

    Task<AuthResult> AuthenticateClientCredentialsGrantAsync(OpenIddictRequest request);

    Task<AuthResult> AuthenticateRefreshTokenGrantAsync(ClaimsPrincipal principal, OpenIddictRequest request);


}
