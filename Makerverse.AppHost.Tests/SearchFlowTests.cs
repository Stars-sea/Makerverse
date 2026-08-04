using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;

namespace Makerverse.AppHost.Tests;

/// <summary>
///     TESTING.md §9 SearchFlowTests：Wolverine/RabbitMQ 异步，全部轮询断言（超时 30s）。
///     经 gateway 搜索（SearchController 参数名为 query；tag 过滤为 query 内 [tag] 语法，SearchController.TagRegex 实测）。
/// </summary>
[Collection("AppHost")]
public sealed class SearchFlowTests(AppHostFixture fixture) {
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private Task<(string AccessToken, string RefreshToken)> UserLogin() {
        return fixture.LoginAsync(TestConstants.TestUserName, TestConstants.TestPassword);
    }

    private Task<(string AccessToken, string RefreshToken)> AdminLogin() {
        return fixture.LoginAsync(TestConstants.TestAdminUserName, TestConstants.TestPassword);
    }

    /// <summary>M1 创建→索引：活动标题含唯一词 → 轮询 gateway /search/activities?query=… → 出现该活动。</summary>
    [Fact]
    public async Task M1_CreatedActivityAppearsInSearch() {
        string suffix = TestConstants.UniqueSuffix();
        var    term   = $"zzq-{suffix}";
        (string userToken, _) = await UserLogin();
        string activityId = await CreateActivityAsync($"Search {term}", new string[0], userToken);

        JsonElement[] hits = await TestHttp.PollAsync(
            fixture.Gateway,
            $"/search/activities?query={Uri.EscapeDataString(term)}",
            hits => hits.Any(h => h.GetProperty("id").GetString() == activityId),
            TestConstants.SearchPollTimeout
        );

        Assert.Contains(hits, h => h.GetProperty("id").GetString() == activityId);
    }

    /// <summary>M2 更新→重索引：改标题为新唯一词 → 轮询新词可搜到。</summary>
    [Fact]
    public async Task M2_UpdatedActivityIsReindexed() {
        string suffix  = TestConstants.UniqueSuffix();
        var    oldTerm = $"zzq-{suffix}";
        var    newTerm = $"zzqm-{suffix}";
        (string userToken, _) = await UserLogin();
        string activityId = await CreateActivityAsync($"Search {oldTerm}", new string[0], userToken);

        using (HttpResponseMessage update = await TestHttp.SendAsync(
                   fixture.ActivityService,
                   HttpMethod.Put,
                   $"/activities/{activityId}",
                   userToken,
                   JsonContent.Create(new
                       { title = $"Search {newTerm}", content = "Updated content", tags = new string[0] })
               )) {
            Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        }

        JsonElement[] hits = await TestHttp.PollAsync(
            fixture.Gateway,
            $"/search/activities?query={Uri.EscapeDataString(newTerm)}",
            hits => hits.Any(h => h.GetProperty("id").GetString() == activityId),
            TestConstants.SearchPollTimeout
        );

        Assert.Contains(hits, h => h.GetProperty("id").GetString() == activityId);
    }

    /// <summary>M3 删除→消失：DELETE 后轮询旧词不再返回该 id。</summary>
    [Fact]
    public async Task M3_DeletedActivityDisappearsFromSearch() {
        string suffix = TestConstants.UniqueSuffix();
        var    term   = $"zzq-{suffix}";
        (string userToken, _) = await UserLogin();
        string activityId = await CreateActivityAsync($"Search {term}", new string[0], userToken);

        // 先等索引出现，再删除，避免断言"从未出现"而非"删除后消失"
        await TestHttp.PollAsync(
            fixture.Gateway,
            $"/search/activities?query={Uri.EscapeDataString(term)}",
            hits => hits.Any(h => h.GetProperty("id").GetString() == activityId),
            TestConstants.SearchPollTimeout
        );

        using (HttpResponseMessage delete = await TestHttp.SendAsync(fixture.ActivityService, HttpMethod.Delete,
                   $"/activities/{activityId}", userToken)) {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        // 空数组也满足（删除后旧词不再命中该 id）
        JsonElement[] hits = await TestHttp.PollAsync(
            fixture.Gateway,
            $"/search/activities?query={Uri.EscapeDataString(term)}",
            hits => hits.All(h => h.GetProperty("id").GetString() != activityId),
            TestConstants.SearchPollTimeout
        );

        Assert.DoesNotContain(hits, h => h.GetProperty("id").GetString() == activityId);
    }

    /// <summary>M4 直查 Typesense：GetConnectionString("typesense") + test-typesense-key 直查集合（与 M1 互证）。</summary>
    [Fact]
    public async Task M4_DirectTypesenseQuery() {
        string suffix = TestConstants.UniqueSuffix();
        var    term   = $"zzq-{suffix}";
        (string userToken, _) = await UserLogin();
        string activityId = await CreateActivityAsync($"Search {term}", new string[0], userToken);

        using var typesense = new HttpClient();
        string    baseUrl   = fixture.App.GetEndpoint("typesense", "typesense").ToString().TrimEnd('/');
        typesense.DefaultRequestHeaders.Add("X-TYPESENSE-API-KEY", TestConstants.TypesenseApiKey);

        var query =
            $"{baseUrl}/collections/activities/documents/search?q={Uri.EscapeDataString(term)}&query_by=title,content";
        DateTime deadline = DateTime.UtcNow + TestConstants.SearchPollTimeout;
        var      found    = false;
        while (DateTime.UtcNow < deadline) {
            using HttpResponseMessage response = await typesense.GetAsync(query);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using JsonDocument doc  = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement[]      hits = doc.RootElement.GetProperty("hits").EnumerateArray().ToArray();
            if (hits.Any(h => h.GetProperty("document").GetProperty("id").GetString() == activityId)) {
                found = true;
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        Assert.True(found,
            $"Activity {activityId} not indexed in Typesense within {TestConstants.SearchPollTimeout.TotalSeconds:0}s");
    }

    /// <summary>M5 直播索引：创建直播（标题唯一词）→ 轮询 /search/lives 出现。</summary>
    [Fact]
    public async Task M5_LiveAppearsInSearch() {
        string suffix = TestConstants.UniqueSuffix();
        var    term   = $"zzl-{suffix}";
        (string userToken, _) = await UserLogin();

        using HttpResponseMessage create = await TestHttp.SendAsync(
            fixture.LiveService,
            HttpMethod.Post,
            "/lives",
            userToken,
            JsonContent.Create(new { title = $"Live {term}" })
        );
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        string liveId = (await ReadJsonAsync(create)).GetProperty("id").GetString()!;

        JsonElement[] hits = await TestHttp.PollAsync(
            fixture.Gateway,
            $"/search/lives?query={Uri.EscapeDataString(term)}",
            hits => hits.Any(h => h.GetProperty("id").GetString() == liveId),
            TestConstants.SearchPollTimeout
        );

        Assert.Contains(hits, h => h.GetProperty("id").GetString() == liveId);
    }

    /// <summary>M6 标签过滤搜索：带标签活动经 query 内 [tag] 语法过滤命中。</summary>
    [Fact]
    public async Task M6_TagFilteredSearch() {
        string suffix  = TestConstants.UniqueSuffix();
        var    tagSlug = $"tag-{suffix}";
        var    term    = $"zzm-{suffix}";
        (string adminToken, _) = await AdminLogin();

        using (HttpResponseMessage tag = await TestHttp.SendAsync(
                   fixture.ActivityService,
                   HttpMethod.Post,
                   "/tags",
                   adminToken,
                   JsonContent.Create(new { name = tagSlug, slug = tagSlug, description = $"Description of {tagSlug}" })
               )) {
            Assert.True(tag.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
                $"tag create: {(int)tag.StatusCode}");
        }

        (string userToken, _) = await UserLogin();
        string activityId = await CreateActivityAsync($"Search {term}", new[] { tagSlug }, userToken);

        var query = $"/search/activities?query={Uri.EscapeDataString($"{term} [{tagSlug}]")}";
        JsonElement[] hits = await TestHttp.PollAsync(
            fixture.Gateway,
            query,
            hits => hits.Any(h => h.GetProperty("id").GetString() == activityId
                                  && h.GetProperty("tags").EnumerateArray().Any(t => t.GetString() == tagSlug)),
            TestConstants.SearchPollTimeout
        );

        JsonElement hit = Assert.Single(hits, h => h.GetProperty("id").GetString() == activityId);
        Assert.Contains(hit.GetProperty("tags").EnumerateArray(), t => t.GetString() == tagSlug);
    }

    private async Task<string> CreateActivityAsync(string title, string[] tags, string token) {
        using HttpResponseMessage response = await TestHttp.SendAsync(
            fixture.ActivityService,
            HttpMethod.Post,
            "/activities",
            token,
            JsonContent.Create(new { title, content = $"Content for {title}", tags })
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await ReadJsonAsync(response)).GetProperty("id").GetString()!;
    }
}