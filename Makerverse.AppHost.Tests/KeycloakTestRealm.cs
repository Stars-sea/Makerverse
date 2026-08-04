using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Makerverse.AppHost.Tests;

/// <summary>
///     TESTING.md §7：在 AppHost healthy 后经 Admin REST API 自助建立 makerverse realm（幂等）。
///     Admin 凭据从 KeycloakResource 模型读取（§11 回退方案；参数名不符时以模型为准）。
/// </summary>
public sealed class KeycloakTestRealm(DistributedApplication app) {
    private readonly HttpClient _http       = new();
    private          string     _adminToken = null!;
    private          string     _baseUrl    = null!;

    public async Task InitializeAsync(CancellationToken ct = default) {
        _baseUrl = app.GetEndpoint("keycloak").ToString().TrimEnd('/');

        KeycloakResource keycloak = app.Services
                                        .GetRequiredService<DistributedApplicationModel>()
                                        .Resources
                                        .OfType<KeycloakResource>()
                                        .FirstOrDefault(r => r.Name == "keycloak")
                                    ?? throw new InvalidOperationException(
                                        "Keycloak resource not found in the application model.");

        // 优先从 KeycloakResource 模型读取（TESTING.md §11 回退方案）；模型读取不可用时回退 §5 固定凭据。
        var    adminUser     = "admin";
        string adminPassword = TestConstants.KeycloakAdminPassword;
        try {
            adminUser     = keycloak.AdminUserNameParameter.Value ?? adminUser;
            adminPassword = keycloak.AdminPasswordParameter.Value ?? adminPassword;
        }
        catch (Exception ex) {
            Console.WriteLine(
                $"[KeycloakTestRealm] Falling back to fixed admin credentials (model read failed: {ex.Message})");
        }

        await WithRetriesAsync(async () => { _adminToken = await GetAdminTokenAsync(adminUser, adminPassword, ct); });

        // 幂等清理：realm 已存在则删除重建
        await DeleteRealmAsync(TestConstants.Realm, ct);
        await CreateRealmAsync(ct);
        await CreatePublicClientAsync(ct);
        await AddAudienceMapperAsync(TestConstants.PublicClientId, ct);
        await AddRealmRoleMapperAsync(TestConstants.PublicClientId, ct);
        await CreateAdminClientAsync(ct);
        await AssignServiceAccountRolesAsync(ct);
        await CreateAdminRoleAsync(ct);
        await CreateUserAsync(TestConstants.TestUserName, "Test", "User", [], ct);
        await CreateUserAsync(TestConstants.TestAdminUserName, "Test", "Admin", [TestConstants.AdminRoleName], ct);
    }

    private async Task<string> GetAdminTokenAsync(string username, string password, CancellationToken ct) {
        using HttpResponseMessage response = await _http.PostAsync(
            $"{_baseUrl}/realms/master/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["grant_type"] = "password",
                ["client_id"]  = TestConstants.AdminCliClientId,
                ["username"]   = username,
                ["password"]   = password
            }),
            ct
        );
        response.EnsureSuccessStatusCode();

        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    private async Task DeleteRealmAsync(string realm, CancellationToken ct) {
        using HttpResponseMessage response = await SendAdminAsync(HttpMethod.Delete, $"/admin/realms/{realm}", ct);
        // 404 = realm 不存在，幂等通过
        Assert.True(response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound,
            $"DELETE realm {realm} failed: {(int)response.StatusCode}");
    }

    private Task CreateRealmAsync(CancellationToken ct) {
        return SendAdminAsync(
            HttpMethod.Post,
            "/admin/realms",
            ct,
            new {
                realm       = TestConstants.Realm,
                enabled     = true,
                sslRequired = "none"
            }).EnsureSuccess();
    }

    private Task CreatePublicClientAsync(CancellationToken ct) {
        return SendAdminAsync(
            HttpMethod.Post,
            $"/admin/realms/{TestConstants.Realm}/clients",
            ct,
            new {
                clientId                  = TestConstants.PublicClientId,
                enabled                   = true,
                publicClient              = true,
                standardFlowEnabled       = true,
                directAccessGrantsEnabled = true
            }).EnsureSuccess();
    }

    /// <summary>
    ///     实测：Keycloak 对 public client 的 access token 默认 aud="account"，而服务端
    ///     AddKeycloakJwtBearer 校验 Audience="makerverse"（Common/AuthExtensions.cs）。
    ///     给 makerverse client 加 audience mapper，使 token 的 aud 包含 "makerverse"。
    /// </summary>
    private async Task AddAudienceMapperAsync(string clientId, CancellationToken ct) {
        string clientUuid = await GetClientIdAsync(clientId, ct);

        using HttpResponseMessage response = await SendAdminAsync(
            HttpMethod.Post,
            $"/admin/realms/{TestConstants.Realm}/clients/{clientUuid}/protocol-mappers/models",
            ct,
            new {
                name           = "audience-makerverse",
                protocol       = "openid-connect",
                protocolMapper = "oidc-audience-mapper",
                config = new Dictionary<string, string> {
                    ["included.client.audience"]  = TestConstants.PublicClientId,
                    ["access.token.claim"]        = "true",
                    ["id.token.claim"]            = "false",
                    ["introspection.token.claim"] = "false",
                    ["userinfo.token.claim"]      = "false"
                }
            }
        );
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Add audience mapper to {clientId} failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}"
            );
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    ///     实测：token 的 realm 角色只在 realm_access.roles 嵌套里，ASP.NET [Authorize(Roles=...)] 需要顶层角色 claim。
    ///     给 makerverse client 加 realm-role mapper，把 realm 角色输出为顶层 roles claim
    ///     （JwtBearer 默认将 roles 映射为 ClaimTypes.Role；IsInRole 大小写不敏感，admin 匹配 Admin）。
    /// </summary>
    private async Task AddRealmRoleMapperAsync(string clientId, CancellationToken ct) {
        string clientUuid = await GetClientIdAsync(clientId, ct);

        using HttpResponseMessage response = await SendAdminAsync(
            HttpMethod.Post,
            $"/admin/realms/{TestConstants.Realm}/clients/{clientUuid}/protocol-mappers/models",
            ct,
            new {
                name           = "realm-roles",
                protocol       = "openid-connect",
                protocolMapper = "oidc-usermodel-realm-role-mapper",
                config = new Dictionary<string, string> {
                    ["claim.name"]           = "roles",
                    ["multivalued"]          = "true",
                    ["access.token.claim"]   = "true",
                    ["id.token.claim"]       = "false",
                    ["userinfo.token.claim"] = "false"
                }
            }
        );
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Add realm role mapper to {clientId} failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}"
            );
        response.EnsureSuccessStatusCode();
    }

    private Task CreateAdminClientAsync(CancellationToken ct) {
        return SendAdminAsync(
            HttpMethod.Post,
            $"/admin/realms/{TestConstants.Realm}/clients",
            ct,
            new {
                clientId               = TestConstants.AdminClientId,
                enabled                = true,
                publicClient           = false,
                serviceAccountsEnabled = true,
                secret                 = TestConstants.AccountServiceClientSecret
            }).EnsureSuccess();
    }

    /// <summary>
    ///     授予 makerverse-account-service 的 service account realm-management 客户端角色
    ///     （manage-users / view-users）——AccountService 经 client_credentials 调 Admin API 的权限来源。
    /// </summary>
    private async Task AssignServiceAccountRolesAsync(CancellationToken ct) {
        string adminClientId           = await GetClientIdAsync(TestConstants.AdminClientId, ct);
        string realmManagementClientId = await GetClientIdAsync("realm-management", ct);

        using HttpResponseMessage serviceAccount = await SendAdminAsync(
            HttpMethod.Get,
            $"/admin/realms/{TestConstants.Realm}/clients/{adminClientId}/service-account-user",
            ct
        );
        serviceAccount.EnsureSuccessStatusCode();
        string serviceAccountUserId = (await ReadJsonAsync(serviceAccount, ct)).GetProperty("id").GetString()!;

        using HttpResponseMessage rolesResponse = await SendAdminAsync(
            HttpMethod.Get,
            $"/admin/realms/{TestConstants.Realm}/clients/{realmManagementClientId}/roles",
            ct
        );
        rolesResponse.EnsureSuccessStatusCode();

        JsonElement[] roles =
            JsonSerializer.Deserialize<JsonElement[]>(await rolesResponse.Content.ReadAsStringAsync(ct)) ?? [];
        string[] wanted = ["manage-users", "view-users"];
        List<object> payload = roles
            .Where(r => wanted.Contains(r.GetProperty("name").GetString()))
            .Select(r => (object)new {
                id   = r.GetProperty("id").GetString(),
                name = r.GetProperty("name").GetString()
            })
            .ToList();
        Assert.Equal(wanted.Length, payload.Count);

        using HttpResponseMessage grant = await SendAdminAsync(
            HttpMethod.Post,
            $"/admin/realms/{TestConstants.Realm}/users/{serviceAccountUserId}/role-mappings/clients/{realmManagementClientId}",
            ct,
            payload
        );
        if (!grant.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Grant service account roles failed: {(int)grant.StatusCode} {await grant.Content.ReadAsStringAsync(ct)}"
            );
        grant.EnsureSuccessStatusCode();
    }

    private async Task<string> GetClientIdAsync(string clientId, CancellationToken ct) {
        using HttpResponseMessage response = await SendAdminAsync(
            HttpMethod.Get,
            $"/admin/realms/{TestConstants.Realm}/clients?clientId={clientId}",
            ct
        );
        response.EnsureSuccessStatusCode();

        using JsonDocument doc   = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        JsonElement        match = Assert.Single(doc.RootElement.EnumerateArray());
        return match.GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct) {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement.Clone();
    }

    private Task CreateAdminRoleAsync(CancellationToken ct) {
        return SendAdminAsync(
            HttpMethod.Post,
            $"/admin/realms/{TestConstants.Realm}/roles",
            ct,
            new { name = TestConstants.AdminRoleName }).EnsureSuccess();
    }

    private async Task CreateUserAsync(string username, string firstName, string lastName, string[] roles,
        CancellationToken                     ct) {
        using HttpResponseMessage create = await SendAdminAsync(
            HttpMethod.Post,
            $"/admin/realms/{TestConstants.Realm}/users",
            ct,
            new {
                username,
                email         = $"{username}@test.local",
                enabled       = true,
                emailVerified = true,
                firstName,
                lastName,
                credentials = new[] {
                    new { type = "password", value = TestConstants.TestPassword, temporary = false }
                }
            });
        create.EnsureSuccessStatusCode();

        if (roles.Length == 0) return;

        string userId = await GetUserIdAsync(username, ct);
        await GrantRealmRolesAsync(userId, roles, ct);
    }

    private async Task<string> GetUserIdAsync(string username, CancellationToken ct) {
        using HttpResponseMessage response = await SendAdminAsync(
            HttpMethod.Get,
            $"/admin/realms/{TestConstants.Realm}/users?username={username}",
            ct
        );
        response.EnsureSuccessStatusCode();

        using JsonDocument doc   = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        JsonElement[]      users = doc.RootElement.EnumerateArray().ToArray();
        JsonElement        match = Assert.Single(users);
        return match.GetProperty("id").GetString()!;
    }

    private async Task GrantRealmRolesAsync(string userId, string[] roles, CancellationToken ct) {
        var rolePayloads = new List<object>();
        foreach (string role in roles) {
            using HttpResponseMessage roleResponse = await SendAdminAsync(
                HttpMethod.Get,
                $"/admin/realms/{TestConstants.Realm}/roles/{role}",
                ct
            );
            roleResponse.EnsureSuccessStatusCode();

            using JsonDocument roleDoc = JsonDocument.Parse(await roleResponse.Content.ReadAsStringAsync(ct));
            rolePayloads.Add(new {
                id   = roleDoc.RootElement.GetProperty("id").GetString(),
                name = role
            });
        }

        // Keycloak 26 Admin API 该端点仅支持 GET/POST/DELETE（文档 §7-6 的 PUT 已不存在，实测 405）；POST 为追加且幂等
        using HttpResponseMessage grant = await SendAdminAsync(
            HttpMethod.Post,
            $"/admin/realms/{TestConstants.Realm}/users/{userId}/role-mappings/realm",
            ct,
            rolePayloads
        );
        if (!grant.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Grant realm roles [{string.Join(", ", roles)}] to {userId} failed: {(int)grant.StatusCode} {await grant.Content.ReadAsStringAsync(ct)}"
            );
        grant.EnsureSuccessStatusCode();
    }

    /// <summary>Keycloak 就绪后个别请求可能 5xx：3 次指数退避重试（TESTING.md §7-7）。</summary>
    private async Task WithRetriesAsync(Func<Task> action) {
        TimeSpan delay = TimeSpan.FromSeconds(1);
        for (var attempt = 1;; attempt++)
            try {
                await action();
                return;
            }
            catch (Exception ex) when (attempt < 3 && ex is HttpRequestException or TaskCanceledException) {
                await Task.Delay(delay);
                delay *= 2;
            }
    }

    private async Task<HttpResponseMessage> SendAdminAsync(HttpMethod method, string path, CancellationToken ct,
        object?                                                       body = null) {
        using var request = new HttpRequestMessage(method, _baseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        HttpResponseMessage response = await _http.SendAsync(request, ct);
        if ((int)response.StatusCode >= 500)
            // 供 WithRetriesAsync 捕获
            throw new HttpRequestException(
                $"Keycloak admin request {method} {path} failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}"
            );

        return response;
    }
}

internal static class KeycloakTestRealmExtensions {
    public static async Task EnsureSuccess(this Task<HttpResponseMessage> task) {
        using HttpResponseMessage response = await task;
        response.EnsureSuccessStatusCode();
    }
}