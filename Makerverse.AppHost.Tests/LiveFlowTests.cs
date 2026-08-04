using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Sdk;

namespace Makerverse.AppHost.Tests;

/// <summary>
///     TESTING.md §9 LiveFlowTests：直播 CRUD + HLS 段（live-svc 直连）。
///     实测事实（LiveService/Controllers/*.cs）：endpoint 仅对 Starting/Started 状态返回 200；DELETE 仅允许 Stopped/Invalid；
///     HLS 路由为 /lives/{id}/segments/index.m3u8；状态机转移依赖 livestream-svc 预创建会话 TTL（默认 30s）与 watcher。
/// </summary>
[Collection("AppHost")]
public sealed class LiveFlowTests(AppHostFixture fixture) {
    // LiveStatus 枚举序列化为整数（无 JsonStringEnumConverter）：Created=0, Starting=1, Started=2, Stopping=3, Stopped=4, Invalid=5
    private const int StatusStopped = 4;

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private Task<(string AccessToken, string RefreshToken)> UserLogin() {
        return fixture.LoginAsync(TestConstants.TestUserName, TestConstants.TestPassword);
    }

    private Task<(string AccessToken, string RefreshToken)> AdminLogin() {
        return fixture.LoginAsync(TestConstants.TestAdminUserName, TestConstants.TestPassword);
    }

    /// <summary>L1 创建直播：登录 POST /lives（合法 CreateLiveDto）→ 201 + id。</summary>
    [Fact]
    public async Task L1_CreateLive() {
        string suffix = TestConstants.UniqueSuffix();
        (string userToken, _) = await UserLogin();

        using HttpResponseMessage response = await TestHttp.SendAsync(
            fixture.LiveService,
            HttpMethod.Post,
            "/lives",
            userToken,
            JsonContent.Create(new { title = $"Live {suffix}" })
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(string.IsNullOrEmpty((await ReadJsonAsync(response)).GetProperty("id").GetString()));
    }

    /// <summary>L2 在线列表：GET /lives/online → 200 + JSON 数组（是否包含刚创建的直播按实现语义，断言仅结构与状态码）。</summary>
    [Fact]
    public async Task L2_OnlineListIsJsonArray() {
        using HttpResponseMessage response = await fixture.LiveService.GetAsync("/lives/online");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Array, (await ReadJsonAsync(response)).ValueKind);
    }

    /// <summary>
    ///     L3 端点获取：start 后 GET /lives/{id}/endpoint → 200 + RTMP 推流地址（含 liveId）；非创建者 → 200 且 ingestUrl 为 null（以实现为准）。
    /// </summary>
    [Fact]
    public async Task L3_LiveEndpoint() {
        string suffix = TestConstants.UniqueSuffix();
        (string userToken, _) = await UserLogin();
        string liveId = await CreateLiveAsync($"Endpoint {suffix}", userToken);

        using (HttpResponseMessage start = await TestHttp.SendAsync(
                   fixture.LiveService,
                   HttpMethod.Put,
                   $"/lives/{liveId}/status",
                   userToken,
                   JsonContent.Create(new { status = "start" })
               )) {
            Assert.Equal(HttpStatusCode.OK, start.StatusCode);
            JsonElement startBody = await ReadJsonAsync(start);
            Assert.False(string.IsNullOrEmpty(startBody.GetProperty("ingestUrl").GetString()));
            Assert.False(string.IsNullOrEmpty(startBody.GetProperty("playbackEndpoints").GetProperty("rtmpUrl")
                .GetString()));
        }

        using (HttpResponseMessage endpoint = await TestHttp.SendAsync(fixture.LiveService, HttpMethod.Get,
                   $"/lives/{liveId}/endpoint", userToken)) {
            Assert.Equal(HttpStatusCode.OK, endpoint.StatusCode);
            JsonElement body      = await ReadJsonAsync(endpoint);
            string?     ingestUrl = body.GetProperty("ingestUrl").GetString();
            Assert.False(string.IsNullOrEmpty(ingestUrl));
            Assert.Contains(liveId, ingestUrl);
        }

        // 非创建者（testadmin）：200，但 IngestUrl 为 null（GetLiveEndpoint 仅向 owner 返回推流地址）
        (string adminToken, _) = await AdminLogin();
        using (HttpResponseMessage other = await TestHttp.SendAsync(fixture.LiveService, HttpMethod.Get,
                   $"/lives/{liveId}/endpoint", adminToken)) {
            Assert.Equal(HttpStatusCode.OK, other.StatusCode);
            Assert.Equal(JsonValueKind.Null, (await ReadJsonAsync(other)).GetProperty("ingestUrl").ValueKind);
        }
    }

    /// <summary>L4 HLS 未就绪 400：status=Created 的直播 GET /lives/{id}/segments/index.m3u8 → 400。</summary>
    [Fact]
    public async Task L4_PlaylistBeforeStartReturns400() {
        string suffix = TestConstants.UniqueSuffix();
        (string userToken, _) = await UserLogin();
        string liveId = await CreateLiveAsync($"Hls {suffix}", userToken);

        using HttpResponseMessage response = await fixture.LiveService.GetAsync($"/lives/{liveId}/segments/index.m3u8");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    ///     L5 删除直播：start 后等待预创建会话 TTL 过期（30s）→ watcher 置 Stopped → DELETE → 204 → GET → 404。
    /// </summary>
    [Fact]
    public async Task L5_DeleteLiveAfterStop() {
        string suffix = TestConstants.UniqueSuffix();
        (string userToken, _) = await UserLogin();
        string liveId = await CreateLiveAsync($"Delete {suffix}", userToken);

        using (HttpResponseMessage start = await TestHttp.SendAsync(
                   fixture.LiveService,
                   HttpMethod.Put,
                   $"/lives/{liveId}/status",
                   userToken,
                   JsonContent.Create(new { status = "start" })
               )) {
            Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        }

        // 无真实推流时预创建会话按 TTL（30s）过期，watcher 将 live 置为 Stopped
        await WaitForStatusAsync(liveId, StatusStopped);

        using (HttpResponseMessage delete =
               await TestHttp.SendAsync(fixture.LiveService, HttpMethod.Delete, $"/lives/{liveId}", userToken)) {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        using HttpResponseMessage gone = await fixture.LiveService.GetAsync($"/lives/{liveId}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    private async Task WaitForStatusAsync(string liveId, int expectedStatus) {
        DateTime deadline = DateTime.UtcNow + TestConstants.LiveStoppedTimeout;
        while (DateTime.UtcNow < deadline) {
            using HttpResponseMessage response = await fixture.LiveService.GetAsync($"/lives/{liveId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            if ((await ReadJsonAsync(response)).GetProperty("status").GetInt32() == expectedStatus) return;

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        throw new XunitException(
            $"Live {liveId} did not reach status {expectedStatus} within {TestConstants.LiveStoppedTimeout.TotalSeconds:0}s");
    }

    private async Task<string> CreateLiveAsync(string title, string token) {
        using HttpResponseMessage response = await TestHttp.SendAsync(
            fixture.LiveService,
            HttpMethod.Post,
            "/lives",
            token,
            JsonContent.Create(new { title })
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await ReadJsonAsync(response)).GetProperty("id").GetString()!;
    }
}