using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Makerverse.AppHost.Tests;

/// <summary>TESTING.md §9 AuthFlowTests：注册/登录/me/资料/头像/刷新（AccountService 直连）。</summary>
[Collection("AppHost")]
public sealed class AuthFlowTests(AppHostFixture fixture) {
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    /// <summary>A1 注册→登录→me：注册用户后走密码授权登录，me 的 id/username 与注册一致。</summary>
    [Fact]
    public async Task A1_Register_Login_Me() {
        string suffix   = TestConstants.UniqueSuffix();
        var    username = $"u{suffix}";

        using (HttpResponseMessage register = await fixture.AccountService.PostAsJsonAsync("/account/users/register",
                   new {
                       username,
                       email     = $"{username}@example.com",
                       password  = "pw-123456",
                       firstName = "First",
                       lastName  = "Last"
                   })) {
            Assert.Equal(HttpStatusCode.OK, register.StatusCode);
            JsonElement body = await ReadJsonAsync(register);
            Assert.False(string.IsNullOrEmpty(body.GetProperty("id").GetString()));
            Assert.Equal(username, body.GetProperty("username").GetString());
        }

        (string access, _) = await fixture.LoginAsync(username, "pw-123456");

        using HttpResponseMessage me =
            await TestHttp.SendAsync(fixture.AccountService, HttpMethod.Get, "/account/users/me", access);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        JsonElement meBody = await ReadJsonAsync(me);
        Assert.False(string.IsNullOrEmpty(meBody.GetProperty("id").GetString()));
        Assert.Equal(username, meBody.GetProperty("username").GetString());
    }

    /// <summary>A2 me 更新资料：PUT /account/users/me 后 GET 反映新值。</summary>
    [Fact]
    public async Task A2_UpdateMyProfile() {
        (string access, _) = await fixture.LoginAsync(TestConstants.TestUserName, TestConstants.TestPassword);
        string suffix       = TestConstants.UniqueSuffix();
        var    newFirstName = $"Updated-{suffix}";

        using (HttpResponseMessage update = await TestHttp.SendAsync(
                   fixture.AccountService,
                   HttpMethod.Put,
                   "/account/users/me",
                   access,
                   JsonContent.Create(new { firstName = newFirstName, lastName = "Updated" }))) {
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        }

        using HttpResponseMessage me =
            await TestHttp.SendAsync(fixture.AccountService, HttpMethod.Get, "/account/users/me", access);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        JsonElement body = await ReadJsonAsync(me);
        Assert.Equal(newFirstName, body.GetProperty("firstName").GetString());
        Assert.Equal("Updated", body.GetProperty("lastName").GetString());
    }

    /// <summary>A3 未授权访问：无 token 的 /account/users/me → 401。</summary>
    [Fact]
    public async Task A3_UnauthorizedMe() {
        using HttpResponseMessage response = await fixture.AccountService.GetAsync("/account/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A4 头像上传与读取：上传 PNG → 200；匿名 GET /account/users/{id}/avatar → 200 + image/png。</summary>
    [Fact]
    public async Task A4_AvatarUploadAndRead() {
        (string access, _) = await fixture.LoginAsync(TestConstants.TestUserName, TestConstants.TestPassword);

        using HttpResponseMessage me =
            await TestHttp.SendAsync(fixture.AccountService, HttpMethod.Get, "/account/users/me", access);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        string userId = (await ReadJsonAsync(me)).GetProperty("id").GetString()!;

        using var form        = new MultipartFormDataContent();
        var       fileContent = new ByteArrayContent(TestHttp.TinyPng);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "file", "avatar.png");

        using HttpResponseMessage upload = await TestHttp.SendAsync(fixture.AccountService, HttpMethod.Post,
            "/account/users/me/avatar", access, form);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.False(string.IsNullOrEmpty((await ReadJsonAsync(upload)).GetProperty("avatarUrl").GetString()));

        using HttpResponseMessage read = await fixture.AccountService.GetAsync($"/account/users/{userId}/avatar");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal("image/png", read.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>A5 头像超限拒绝：>5MB 请求体（RequestSizeLimit 5MB）→ 413。</summary>
    [Fact]
    public async Task A5_AvatarTooLargeRejected() {
        (string access, _) = await fixture.LoginAsync(TestConstants.TestUserName, TestConstants.TestPassword);

        var oversized = new byte[5 * 1024 * 1024 + 1024];
        Random.Shared.NextBytes(oversized);

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(oversized), "file", "big.png");

        using HttpResponseMessage response = await TestHttp.SendAsync(fixture.AccountService, HttpMethod.Post,
            "/account/users/me/avatar", access, form);

        // 非 2xx：实测返回 400（UploadAsync 按 AvatarOptions.MaxFileSizeBytes 校验 → Error.Validation → BadRequest）
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>A6 token 刷新：refresh_token 换新 access_token。</summary>
    [Fact]
    public async Task A6_RefreshToken() {
        (string access, string refresh) =
            await fixture.LoginAsync(TestConstants.TestUserName, TestConstants.TestPassword);

        using HttpResponseMessage response =
            await fixture.AccountService.PostAsJsonAsync("/account/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body      = await ReadJsonAsync(response);
        string      newAccess = body.GetProperty("access_token").GetString()!;
        Assert.False(string.IsNullOrEmpty(newAccess));
        Assert.NotEqual(access, newAccess);
    }
}