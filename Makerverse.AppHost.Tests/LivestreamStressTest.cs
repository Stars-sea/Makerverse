using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Makerverse.AppHost.Tests;

/// <summary>
///     JSON-serializable stress test report. Schema matches
///     the Rust <c>StressReport</c> struct in livestream-test-utils.
/// </summary>
public sealed record StressReport {
    [JsonPropertyName("total_streams")] public int TotalStreams { get; init; }

    [JsonPropertyName("successful")] public int Successful { get; init; }

    [JsonPropertyName("failed")] public int Failed { get; init; }

    [JsonPropertyName("total_duration_secs")]
    public double TotalDurationSecs { get; init; }

    [JsonPropertyName("per_stream")] public List<StreamResult> PerStream { get; init; } = [];
}

/// <summary>Per-stream result in the stress test report.</summary>
public sealed record StreamResult {
    [JsonPropertyName("live_id")] public string LiveId { get; init; } = "";

    [JsonPropertyName("success")] public bool Success { get; init; }

    [JsonPropertyName("push_latency_ms")] public ulong PushLatencyMs { get; init; }

    [JsonPropertyName("pull_frames_detected")]
    public bool PullFramesDetected { get; init; }

    [JsonPropertyName("errors")] public List<string> Errors { get; init; } = [];
}

/// <summary>
///     Integration hook for running the Rust stress test binary from
///     .NET Aspire integration tests. Usage:
///     <code>
///   var report = await LivestreamStressTest.RunAsync(
///       grpcEndpoint: await app.GetConnectionStringAsync("livestream-svc"),
///       testVideoPath: "testdata/sample.mp4",
///       streams: 2,
///       durationSecs: 10);
///   Assert.Equal(2, report.Successful);
/// </code>
/// </summary>
public static class LivestreamStressTest {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    ///     Run a stress test against a running livestream container.
    ///     The stress binary (<c>livestream-test-utils</c>) must be available
    ///     inside the container (or on the host with network access to it).
    /// </summary>
    /// <param name="grpcEndpoint">gRPC address of the livestream service.</param>
    /// <param name="testVideoPath">Path to a test video file accessible from the stress binary.</param>
    /// <param name="streams">Number of concurrent streams.</param>
    /// <param name="durationSecs">Duration of each stream push in seconds.</param>
    /// <param name="parallel">Maximum parallel streams (default: same as <paramref name="streams" />).</param>
    /// <param name="protocol">Protocol to use: "rtmp" or "rtsp".</param>
    /// <param name="rtmpPort">Host-reachable RTMP port; overrides the value reported by GetServiceInfo.</param>
    /// <param name="rtspPort">Host-reachable RTSP port; overrides the value reported by GetServiceInfo.</param>
    /// <param name="httpFlvPort">Host-reachable HTTP-FLV port; overrides the value reported by GetServiceInfo.</param>
    /// <param name="cancellationToken">Cancellation token for the stress run.</param>
    /// <returns>The parsed <see cref="StressReport" />.</returns>
    public static async Task<StressReport> RunAsync(
        string grpcEndpoint,
        string testVideoPath,
        int    streams      = 10,
        int    durationSecs = 30,
        int?   parallel     = null,
        string protocol     = "rtmp",
        int?   rtmpPort     = null,
        int?   rtspPort     = null,
        int?   httpFlvPort  = null,
        CancellationToken cancellationToken = default) {
        ProcessStartInfo psi = new() {
            FileName = "cargo",
            WorkingDirectory = FindLivestreamRepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add("livestream-test-utils");
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add("--grpc-addr");  psi.ArgumentList.Add(grpcEndpoint);
        psi.ArgumentList.Add("--input-file"); psi.ArgumentList.Add(testVideoPath);
        psi.ArgumentList.Add("--streams");    psi.ArgumentList.Add(streams.ToString());
        psi.ArgumentList.Add("--duration");   psi.ArgumentList.Add(durationSecs.ToString());
        psi.ArgumentList.Add("--protocol");   psi.ArgumentList.Add(protocol);
        psi.ArgumentList.Add("--json");
        if (parallel.HasValue) {
            psi.ArgumentList.Add("--parallel");
            psi.ArgumentList.Add(parallel.Value.ToString());
        }
        if (rtmpPort.HasValue) {
            psi.ArgumentList.Add("--rtmp-port");
            psi.ArgumentList.Add(rtmpPort.Value.ToString());
        }
        if (rtspPort.HasValue) {
            psi.ArgumentList.Add("--rtsp-port");
            psi.ArgumentList.Add(rtspPort.Value.ToString());
        }
        if (httpFlvPort.HasValue) {
            psi.ArgumentList.Add("--http-flv-port");
            psi.ArgumentList.Add(httpFlvPort.Value.ToString());
        }

        using Process process = Process.Start(psi)
                                ?? throw new InvalidOperationException("Failed to start stress test process");

        // 并行读两个管道，避免 stderr 缓冲满时顺序读死锁（ffmpeg 子进程日志量大）。
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string stdout = await stdoutTask;
        string stderr = await stderrTask;

        StressReport report;
        try {
            report = JsonSerializer.Deserialize<StressReport>(stdout, JsonOptions)
                     ?? throw new InvalidOperationException("Empty stress test JSON output");
        }
        catch (JsonException) {
            throw new InvalidOperationException(
                $"Failed to parse stress test JSON output (exit {process.ExitCode}).\nStderr:\n{stderr}");
        }

        if (process.ExitCode == 0 && report.Failed <= 0) return report;

        IEnumerable<string> perStream = report.PerStream
            .Where(r => !r.Success)
            .Select(r => $"  {r.LiveId}: {string.Join("; ", r.Errors)}");
        throw new InvalidOperationException(
            $"Stress test failed: {report.Failed}/{report.TotalStreams} streams.\n" +
            string.Join('\n', perStream) +
            $"\nStderr tail:\n{stderr[^Math.Min(stderr.Length, 2000)..]}");

    }

    /// <summary>从测试二进制目录向上查找 livestream-rs 仓库根（含 Cargo.toml 的目录）。</summary>
    private static string FindLivestreamRepoRoot() {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent) {
            string cargoToml = Path.Combine(dir.FullName, "livestream-rs", "Cargo.toml");
            if (File.Exists(cargoToml))
                return Path.Combine(dir.FullName, "livestream-rs");
        }
        throw new DirectoryNotFoundException(
            $"livestream-rs repo root not found above {AppContext.BaseDirectory}");
    }
}