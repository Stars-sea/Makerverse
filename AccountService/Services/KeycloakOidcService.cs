using System.Net.Http.Headers;
using System.Text.Json;
using AccountService.DTOs.Auth;
using AccountService.Options;
using Microsoft.Extensions.Options;

namespace AccountService.Services;

public sealed class KeycloakOidcService(
    HttpClient httpClient,
    IOptions<KeycloakOptions> options
) {
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    private string Realm => options.Value.Realm;
    private string ClientId => options.Value.PublicClientId;
    private string InternalBaseUrl => options.Value.InternalBaseUrl;

    public Task<KeycloakResponse> LoginAsync(TokenRequestDto request, CancellationToken ct = default) {
        Dictionary<string, string> form = new() {
            ["grant_type"] = "password",
            ["client_id"]  = ClientId,
            ["username"]   = request.Username,
            ["password"]   = request.Password,
            ["scope"]      = "openid"
        };

        return PostFormAsync($"/realms/{Realm}/protocol/openid-connect/token", form, ct);
    }

    public Task<KeycloakResponse> RefreshAsync(RefreshTokenRequestDto request, CancellationToken ct = default) {
        Dictionary<string, string> form = new() {
            ["grant_type"]    = "refresh_token",
            ["client_id"]     = ClientId,
            ["refresh_token"] = request.RefreshToken,
            ["scope"]         = "openid"
        };

        return PostFormAsync($"/realms/{Realm}/protocol/openid-connect/token", form, ct);
    }

    public Task<KeycloakResponse> LogoutAsync(LogoutRequestDto request, CancellationToken ct = default) {
        Dictionary<string, string> form = new() {
            ["client_id"]     = ClientId,
            ["refresh_token"] = request.RefreshToken
        };

        return PostFormAsync($"/realms/{Realm}/protocol/openid-connect/logout", form, ct);
    }

    public async Task<KeycloakResponse> GetUserInfoAsync(string bearerToken, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/realms/{Realm}/protocol/openid-connect/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
        string                    content  = await response.Content.ReadAsStringAsync(ct);
        return new KeycloakResponse((int)response.StatusCode, content, response.Content.Headers.ContentType?.MediaType);
    }

    public async Task<KeycloakTokenPayload> GetAdminAccessTokenAsync(string clientId, string clientSecret, CancellationToken ct = default) {
        Dictionary<string, string> form = new() {
            ["grant_type"]    = "client_credentials",
            ["client_id"]     = clientId,
            ["client_secret"] = clientSecret
        };

        using HttpResponseMessage response = await httpClient.PostAsync(
            $"/realms/{Realm}/protocol/openid-connect/token",
            new FormUrlEncodedContent(form),
            ct
        );

        string content = await response.Content.ReadAsStringAsync(ct);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<KeycloakTokenPayload>(content, jsonOptions) ?? throw new InvalidOperationException("Failed to deserialize Keycloak admin token response.");
    }

    private async Task<KeycloakResponse> PostFormAsync(string path, Dictionary<string, string> form, CancellationToken ct) {
        using HttpResponseMessage response = await httpClient.PostAsync(path, new FormUrlEncodedContent(form), ct);
        string                    content  = await response.Content.ReadAsStringAsync(ct);
        return new KeycloakResponse((int)response.StatusCode, content, response.Content.Headers.ContentType?.MediaType);
    }
}
