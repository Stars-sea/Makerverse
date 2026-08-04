using System.Net.Http.Json;
using System.Text.Json;

namespace Makerverse.AppHost.Tests;

/// <summary>TESTING.md §9 ActivityFlowTests：活动/评论/标签/Redis 缓存（activity-svc 直连）。</summary>
[Collection("AppHost")]
public sealed class ActivityFlowTests(AppHostFixture fixture) {
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private Task<(string AccessToken, string RefreshToken)> UserLogin() {
        return fixture.LoginAsync(TestConstants.TestUserName, TestConstants.TestPassword);
    }

    private Task<(string AccessToken, string RefreshToken)> AdminLogin() {
        return fixture.LoginAsync(TestConstants.TestAdminUserName, TestConstants.TestPassword);
    }

    /// <summary>创建唯一 tag（admin），已存在（409）视为幂等通过。</summary>
    private async Task EnsureTagAsync(string slug, string adminToken) {
        using HttpResponseMessage response = await TestHttp.SendAsync(
            fixture.ActivityService,
            HttpMethod.Post,
            "/tags",
            adminToken,
            JsonContent.Create(new { name = slug, slug, description = $"Description of {slug}" })
        );

        Assert.True(
            response.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
            $"EnsureTag {slug} failed: {(int)response.StatusCode}"
        );
    }

    /// <summary>C1 创建活动：登录后 POST /activities（合法 DTO + 已存在的 tag）→ 201 + id。</summary>
    [Fact]
    public async Task C1_CreateActivity() {
        string suffix = TestConstants.UniqueSuffix();
        (string adminToken, _) = await AdminLogin();
        var tagSlug = $"tag-{suffix}";
        await EnsureTagAsync(tagSlug, adminToken);

        (string userToken, _) = await UserLogin();
        using HttpResponseMessage response = await TestHttp.SendAsync(
            fixture.ActivityService,
            HttpMethod.Post,
            "/activities",
            userToken,
            JsonContent.Create(new {
                title   = $"Activity {suffix}",
                content = $"Content {suffix}",
                tags    = new[] { tagSlug }
            })
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement body = await ReadJsonAsync(response);
        Assert.False(string.IsNullOrEmpty(body.GetProperty("id").GetString()));
    }

    /// <summary>C2 读取+浏览量递增：连续两次 GET /activities/{id}，第二次 viewCount ≥ 第一次。</summary>
    [Fact]
    public async Task C2_ViewCountIsMonotonic() {
        string suffix     = TestConstants.UniqueSuffix();
        string activityId = await CreateActivityAsync($"Monotonic {suffix}", new string[0], await UserLogin());

        using HttpResponseMessage first = await fixture.ActivityService.GetAsync($"/activities/{activityId}");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        ulong firstCount = (await ReadJsonAsync(first)).GetProperty("viewCount").GetUInt64();

        using HttpResponseMessage second = await fixture.ActivityService.GetAsync($"/activities/{activityId}");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        ulong secondCount = (await ReadJsonAsync(second)).GetProperty("viewCount").GetUInt64();

        Assert.True(secondCount >= firstCount, $"viewCount regressed: {secondCount} < {firstCount}");
    }

    /// <summary>C3 非法 slug 拒绝：tags 含非法 slug → 400（TagSlugValidator）。</summary>
    [Fact]
    public async Task C3_InvalidTagSlugRejected() {
        (string userToken, _) = await UserLogin();

        using HttpResponseMessage response = await TestHttp.SendAsync(
            fixture.ActivityService,
            HttpMethod.Post,
            "/activities",
            userToken,
            JsonContent.Create(new {
                title   = "Invalid tags",
                content = "Body",
                tags    = new[] { "Bad Slug!" }
            })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>C4 评论：POST /activities/{id}/comments → 201；GET /activities/{id}/comments 含该评论。</summary>
    [Fact]
    public async Task C4_CommentLifecycle() {
        string suffix = TestConstants.UniqueSuffix();
        (string userToken, _) = await UserLogin();
        string activityId = await CreateActivityAsync($"Comments {suffix}", new string[0], (userToken, ""));

        using (HttpResponseMessage comment = await TestHttp.SendAsync(
                   fixture.ActivityService,
                   HttpMethod.Post,
                   $"/activities/{activityId}/comments",
                   userToken,
                   JsonContent.Create(new { content = $"Comment {suffix}" })
               )) {
            Assert.Equal(HttpStatusCode.Created, comment.StatusCode);
        }

        using HttpResponseMessage comments =
            await fixture.ActivityService.GetAsync($"/activities/{activityId}/comments");
        Assert.Equal(HttpStatusCode.OK, comments.StatusCode);

        JsonElement[] list = JsonSerializer.Deserialize<JsonElement[]>(await comments.Content.ReadAsStringAsync()) ??
                             [];
        Assert.Contains(list, c => c.GetProperty("content").GetString() == $"Comment {suffix}");
    }

    /// <summary>C5 标签重复 409：POST /tags 唯一 slug → 201；重复 → 409；GET /tags 含该 slug。</summary>
    [Fact]
    public async Task C5_DuplicateTagConflict() {
        var slug = $"dup-{TestConstants.UniqueSuffix()}";
        (string adminToken, _) = await AdminLogin();

        using (HttpResponseMessage first = await CreateTagAsync(slug, adminToken)) {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }

        using (HttpResponseMessage second = await CreateTagAsync(slug, adminToken)) {
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        }

        using HttpResponseMessage all = await fixture.ActivityService.GetAsync("/tags");
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);

        JsonElement[] tags = JsonSerializer.Deserialize<JsonElement[]>(await all.Content.ReadAsStringAsync()) ?? [];
        Assert.Contains(tags, t => t.GetProperty("slug").GetString() == slug);
    }

    /// <summary>C6 标签需 admin：普通用户 POST /tags → 403；testadmin → 201。</summary>
    [Fact]
    public async Task C6_TagCreationRequiresAdminRole() {
        string suffix = TestConstants.UniqueSuffix();
        (string userToken, _)  = await UserLogin();
        (string adminToken, _) = await AdminLogin();

        using (HttpResponseMessage forbidden = await CreateTagAsync($"nope-{suffix}", userToken)) {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using (HttpResponseMessage created = await CreateTagAsync($"ok-{suffix}", adminToken)) {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }
    }

    /// <summary>C7 标签 Redis 缓存：连续两次 GET /tags 响应体一致（缓存命中不改变响应，不做内部状态断言）。</summary>
    [Fact]
    public async Task C7_TagsCacheStableResponse() {
        string first  = await fixture.ActivityService.GetStringAsync("/tags");
        string second = await fixture.ActivityService.GetStringAsync("/tags");

        Assert.Equal(first, second);
    }

    private async Task<string> CreateActivityAsync(string title, string[] tags,
        (string AccessToken, string RefreshToken)         user) {
        using HttpResponseMessage response = await TestHttp.SendAsync(
            fixture.ActivityService,
            HttpMethod.Post,
            "/activities",
            user.AccessToken,
            JsonContent.Create(new { title, content = $"Content for {title}", tags })
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await ReadJsonAsync(response)).GetProperty("id").GetString()!;
    }

    private Task<HttpResponseMessage> CreateTagAsync(string slug, string token) {
        return TestHttp.SendAsync(
            fixture.ActivityService,
            HttpMethod.Post,
            "/tags",
            token,
            JsonContent.Create(new { name = slug, slug, description = $"Description of {slug}" })
        );
    }
}