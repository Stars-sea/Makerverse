using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Sdk;

namespace Makerverse.AppHost.Tests;

/// <summary>测试共享 HTTP 工具：登录、带 Bearer 的请求、极小 PNG 与轮询。</summary>
internal static class TestHttp {
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    /// <summary>1×1 透明 PNG（67 字节），满足 avatar 允许的 image/png。</summary>
    public static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=="
    );

    /// <summary>POST /account/auth/token（密码授权，client_id=makerverse，由 AccountService 透传 Keycloak）。</summary>
    public static async Task<(string AccessToken, string RefreshToken)> LoginAsync(HttpClient client, string username,
        string                                                                                password) {
        using HttpResponseMessage response =
            await client.PostAsJsonAsync("/account/auth/token", new { username, password });
        Assert.True(
            response.IsSuccessStatusCode,
            $"Login failed for '{username}': HTTP {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}"
        );

        using JsonDocument doc  = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement        root = doc.RootElement;
        return (
            root.GetProperty("access_token").GetString()!,
            root.GetProperty("refresh_token").GetString()!
        );
    }

    /// <summary>发送带可选 Bearer token 的请求。调用方负责 dispose 返回值。</summary>
    public static Task<HttpResponseMessage> SendAsync(
        HttpClient   client,
        HttpMethod   method,
        string       requestUri,
        string?      accessToken = null,
        HttpContent? content     = null
    ) {
        var request = new HttpRequestMessage(method, requestUri) { Content = content };
        if (accessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client.SendAsync(request);
    }

    /// <summary>
    ///     轮询 GET 直到响应成功且 hits 满足数组级 stop 条件（stopCondition 为 null 时任意成功响应即返回，含空数组）。
    ///     超时抛 <see cref="Xunit.Sdk.XunitException" />。用于 Wolverine/RabbitMQ 异步断言的统一轮询。
    /// </summary>
    public static async Task<JsonElement[]> PollAsync(
        HttpClient                 client,
        string                     requestUri,
        Func<JsonElement[], bool>? stopCondition,
        TimeSpan                   timeout,
        string?                    accessToken = null
    ) {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline) {
            using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, requestUri, accessToken);
            if (response.IsSuccessStatusCode) {
                string        body = await response.Content.ReadAsStringAsync();
                JsonElement[] hits = JsonSerializer.Deserialize<JsonElement[]>(body, WebJson) ?? [];
                if (stopCondition is null || stopCondition(hits)) return hits;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new XunitException($"Poll timed out after {timeout.TotalSeconds:0}s: {requestUri}");
    }

    public static T Deserialize<T>(string json) {
        return JsonSerializer.Deserialize<T>(json, WebJson)!;
    }
}