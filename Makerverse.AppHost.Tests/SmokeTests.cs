namespace Makerverse.AppHost.Tests;

/// <summary>TESTING.md §9 SmokeTests：网关可达 / 全栈健康 / 服务健康端点。</summary>
[Collection("AppHost")]
public sealed class SmokeTests(AppHostFixture fixture) {
    /// <summary>S1 网关可达：GET /activities 经 YARP 路由到 activity-svc。</summary>
    [Fact]
    public async Task S1_GatewayRoutesToActivityService() {
        using HttpResponseMessage response = await fixture.Gateway.GetAsync("/activities");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>S2 各服务健康：依次等待全栈资源 healthy（无健康检查注解的资源 Running 即视为 healthy）。</summary>
    [Fact]
    public async Task S2_AllResourcesHealthy() {
        await fixture.WaitForAllHealthyAsync();
    }

    /// <summary>S3 服务健康端点：activity-svc 直连 /health（ServiceDefaults MapDefaultEndpoints）。</summary>
    [Fact]
    public async Task S3_ServiceHealthEndpoints() {
        using HttpResponseMessage response = await fixture.ActivityService.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}