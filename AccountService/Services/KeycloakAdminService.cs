using System.Text.Json;
using AccountService.DTOs.Users;
using AccountService.Options;
using ErrorOr;
using Microsoft.Extensions.Options;

namespace AccountService.Services;

public sealed class KeycloakAdminService(
    HttpClient httpClient,
    KeycloakOidcService oidcService,
    IOptions<KeycloakOptions> keycloakOptions,
    IOptions<KeycloakAdminOptions> adminOptions,
    ILogger<KeycloakAdminService> logger
) {
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    private string Realm => keycloakOptions.Value.Realm;
    private string ClientId => adminOptions.Value.ClientId;
    private string ClientSecret => adminOptions.Value.ClientSecret;

    public async Task<ErrorOr<KeycloakUserRepresentation>> RegisterUserAsync(RegisterUserDto request, CancellationToken ct = default) {
        var tokenResult = await GetAdminAccessTokenAsync(ct);
        if (tokenResult.IsError)
            return tokenResult.Errors[0];

        string token = tokenResult.Value;
        KeycloakUserRepresentation user = new() {
            Username      = request.Username,
            Email         = request.Email,
            FirstName     = request.FirstName,
            LastName      = request.LastName,
            EmailVerified = false,
            Enabled       = true,
            Credentials = [
                new KeycloakCredentialRepresentation {
                    Value     = request.Password,
                    Temporary = false
                }
            ]
        };

        using HttpRequestMessage  httpRequest = CreateRequest(HttpMethod.Post, $"/admin/realms/{Realm}/users", token, user);
        using HttpResponseMessage response    = await httpClient.SendAsync(httpRequest, ct);
        string                    content     = await response.Content.ReadAsStringAsync(ct);

        if (response.IsSuccessStatusCode) {
            string? location = response.Headers.Location?.ToString();
            string? userId   = location?.Split('/').LastOrDefault();
            if (string.IsNullOrWhiteSpace(userId))
                return Error.Failure("Failed to resolve created user id.");

            var created = await GetUserByIdAsync(userId, ct);
            return created;
        }

        logger.LogWarning("Keycloak register user failed with status {status}: {content}", (int)response.StatusCode, content);
        return ToError(response.StatusCode, content);
    }

    public async Task<ErrorOr<KeycloakUserRepresentation>> GetUserByIdAsync(string userId, CancellationToken ct = default) {
        var tokenResult = await GetAdminAccessTokenAsync(ct);
        if (tokenResult.IsError)
            return tokenResult.Errors[0];

        string                    token    = tokenResult.Value;
        using HttpRequestMessage  request  = CreateRequest(HttpMethod.Get, $"/admin/realms/{Realm}/users/{userId}", token);
        using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
        string                    content  = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            return ToError(response.StatusCode, content);

        KeycloakUserRepresentation? user = JsonSerializer.Deserialize<KeycloakUserRepresentation>(content, jsonOptions);
        return user is null ? Error.Failure("Failed to deserialize Keycloak user response.") : user;
    }

    public async Task<ErrorOr<KeycloakUserRepresentation>> UpdateUserProfileAsync(string userId, UpdateMyProfileDto request, CancellationToken ct = default) {
        var existingResult = await GetUserByIdAsync(userId, ct);
        if (existingResult.IsError)
            return existingResult.Errors[0];

        KeycloakUserRepresentation user = existingResult.Value;
        user.FirstName = request.FirstName;
        user.LastName  = request.LastName;

        var tokenResult = await GetAdminAccessTokenAsync(ct);
        if (tokenResult.IsError)
            return tokenResult.Errors[0];

        string                    token       = tokenResult.Value;
        using HttpRequestMessage  httpRequest = CreateRequest(HttpMethod.Put, $"/admin/realms/{Realm}/users/{userId}", token, user);
        using HttpResponseMessage response    = await httpClient.SendAsync(httpRequest, ct);
        string                    content     = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            return ToError(response.StatusCode, content);

        return await GetUserByIdAsync(userId, ct);
    }

    public async Task<ErrorOr<KeycloakUserRepresentation>> UpdateAvatarAsync(string userId, StoredAvatar avatar, CancellationToken ct = default) {
        var existingResult = await GetUserByIdAsync(userId, ct);
        if (existingResult.IsError)
            return existingResult.Errors[0];

        var tokenResult = await GetAdminAccessTokenAsync(ct);
        if (tokenResult.IsError)
            return tokenResult.Errors[0];

        string                    token    = tokenResult.Value;
        using HttpRequestMessage  request  = CreateRequest(HttpMethod.Put, $"/admin/realms/{Realm}/users/{userId}", token, existingResult.Value);
        using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
        string                    content  = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            return ToError(response.StatusCode, content);

        return await GetUserByIdAsync(userId, ct);
    }

    public async Task<ErrorOr<KeycloakUserRepresentation>> RemoveAvatarAsync(string userId, CancellationToken ct = default) {
        var existingResult = await GetUserByIdAsync(userId, ct);
        if (existingResult.IsError)
            return existingResult.Errors[0];

        var tokenResult = await GetAdminAccessTokenAsync(ct);
        if (tokenResult.IsError)
            return tokenResult.Errors[0];

        string                    token    = tokenResult.Value;
        using HttpRequestMessage  request  = CreateRequest(HttpMethod.Put, $"/admin/realms/{Realm}/users/{userId}", token, existingResult.Value);
        using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
        string                    content  = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            return ToError(response.StatusCode, content);

        return await GetUserByIdAsync(userId, ct);
    }

    private async Task<ErrorOr<string>> GetAdminAccessTokenAsync(CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(ClientSecret))
            return Error.Failure(description: "Keycloak admin client secret is not configured.");

        try {
            KeycloakTokenPayload token = await oidcService.GetAdminAccessTokenAsync(ClientId, ClientSecret, ct);
            return token.AccessToken;
        }
        catch (HttpRequestException ex) {
            logger.LogWarning(ex, "Failed to get Keycloak admin token for client {clientId}", ClientId);
            return Error.Failure(
                description: $"Failed to get Keycloak admin token for client '{ClientId}'. Confirm the client exists in realm '{Realm}' and that its secret matches ACCOUNT_SERVICE_CLIENT_SECRET."
            );
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, string bearerToken, object? body = null) {
        HttpRequestMessage request = new(method, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);

        if (body is not null) {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static Error ToError(System.Net.HttpStatusCode statusCode, string content) {
        return statusCode switch {
            System.Net.HttpStatusCode.Conflict     => Error.Conflict(description: string.IsNullOrWhiteSpace(content) ? "Resource already exists." : content),
            System.Net.HttpStatusCode.NotFound     => Error.NotFound(description: string.IsNullOrWhiteSpace(content) ? "Resource not found." : content),
            System.Net.HttpStatusCode.Unauthorized => Error.Unauthorized(description: string.IsNullOrWhiteSpace(content) ? "Unauthorized." : content),
            System.Net.HttpStatusCode.Forbidden    => Error.Forbidden(description: string.IsNullOrWhiteSpace(content) ? "Forbidden." : content),
            _                                      => Error.Failure(description: string.IsNullOrWhiteSpace(content) ? "Keycloak request failed." : content)
        };
    }
}
