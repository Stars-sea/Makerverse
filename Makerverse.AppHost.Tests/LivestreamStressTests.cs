using Aspire.Hosting.Testing; // GetConnectionStringAsync / GetEndpoint 扩展

namespace Makerverse.AppHost.Tests;

/// <summary>
///     端到端压力测试：经 gRPC 控制面创建并发直播会话，ffmpeg 推流（RTMP），
///     验证拉流收到视频帧。需要 docker（AppHost 全栈）、cargo 与 PATH 中的 ffmpeg。
/// </summary>
[Collection("AppHost")]
public sealed class LivestreamStressTests(AppHostFixture fixture) {
    // 冷启动预算：首次 cargo 构建 test-utils 依赖树（tonic/prost）可耗时数分钟。
    private static readonly TimeSpan StressTimeout = TimeSpan.FromMinutes(15);

    [Fact]
    [Trait("Category", "Stress")]
    public async Task Stress_RtmpPushPull_AllStreamsSucceed() {
        string grpcEndpoint = await fixture.App.GetConnectionStringAsync("livestream-svc")
                              ?? fixture.App.GetEndpoint("livestream-svc", "grpc").ToString()
                              ?? throw new InvalidOperationException("livestream-svc connection string missing");
        string minioConnectionString = await fixture.App.GetConnectionStringAsync("minio")
                                       ?? throw new InvalidOperationException("minio connection string missing");

        using CancellationTokenSource cts = new(StressTimeout);
        StressReport report = await LivestreamStressTest.RunAsync(
            grpcEndpoint: grpcEndpoint,
            testVideoPath: "testdata/sample.mp4", // 相对 cwd（livestream-rs 仓库根）
            streams: 2,
            durationSecs: 15, // 10s 分段时长 + 0.5s 关键帧 → 首个 TS 约 10.5s 出现，15s 留足余量
            parallel: 2,
            protocol: "rtmp",
            minioConnectionString: minioConnectionString,
            // 测试模式下容器宿主端口随机分配，GetServiceInfo 只报告容器内端口；
            // 把实际宿主端口传给工具（--rtmp-port/--rtsp-port/--http-flv-port 覆盖）。
            rtmpPort: fixture.App.GetEndpoint("livestream-svc", "rtmp").Port,
            rtspPort: fixture.App.GetEndpoint("livestream-svc", "rtsp").Port,
            httpFlvPort: fixture.App.GetEndpoint("livestream-svc", "http-flv").Port,
            cancellationToken: cts.Token);

        Assert.Equal(2, report.Successful);
        Assert.All(report.PerStream, r => Assert.True(r.PullFramesDetected));
        Assert.All(report.PerStream, r => Assert.True(r.HlsVerified));
    }
}