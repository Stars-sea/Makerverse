using System.Text.Json;
using System.Text.Json.Serialization;

namespace Makerverse.AppHost;

/// <summary>
/// JSON-serializable stress test report. Schema matches
/// the Rust <c>StressReport</c> struct in livestream-test-utils.
/// </summary>
public sealed record StressReport
{
    [JsonPropertyName("total_streams")]
    public int TotalStreams { get; init; }

    [JsonPropertyName("successful")]
    public int Successful { get; init; }

    [JsonPropertyName("failed")]
    public int Failed { get; init; }

    [JsonPropertyName("total_duration_secs")]
    public double TotalDurationSecs { get; init; }

    [JsonPropertyName("per_stream")]
    public List<StreamResult> PerStream { get; init; } = [];
}

/// <summary>Per-stream result in the stress test report.</summary>
public sealed record StreamResult
{
    [JsonPropertyName("live_id")]
    public string LiveId { get; init; } = "";

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("push_latency_ms")]
    public ulong PushLatencyMs { get; init; }

    [JsonPropertyName("pull_frames_detected")]
    public bool PullFramesDetected { get; init; }

    [JsonPropertyName("errors")]
    public List<string> Errors { get; init; } = [];
}

/// <summary>
/// Integration hook for running the Rust stress test binary from
/// .NET Aspire integration tests or Testcontainers.
///
/// Usage from an Aspire integration test:
/// <code>
///   var app = await DistributedApplicationTestingBuilder
///       .CreateAsync&lt;Projects.Makerverse_AppHost&gt;();
///   await using var host = await app.BuildAsync();
///   await host.StartAsync();
///
///   var report = await LivestreamStressTest.RunAsync(
///       livestreamContainerName: "livestream-svc",
///       grpcPort: 50050,
///       streams: 5,
///       durationSecs: 20,
///       testVideoPath: "/testdata/sample.mp4");
///   Assert.Equal(5, report.Successful);
/// </code>
/// </summary>
public static class LivestreamStressTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Run a stress test against a running livestream container.
    /// The stress binary (<c>livestream-test-utils</c>) must be available
    /// inside the container (or on the host with network access to it).
    /// </summary>
    /// <param name="grpcEndpoint">gRPC address of the livestream service.</param>
    /// <param name="testVideoPath">Path to a test video file accessible from the stress binary.</param>
    /// <param name="streams">Number of concurrent streams.</param>
    /// <param name="durationSecs">Duration of each stream push in seconds.</param>
    /// <param name="parallel">Maximum parallel streams (default: same as <paramref name="streams"/>).</param>
    /// <param name="protocol">Protocol to use: "rtmp" or "rtsp".</param>
    /// <returns>The parsed <see cref="StressReport"/>.</returns>
    public static async Task<StressReport> RunAsync(
        string grpcEndpoint,
        string testVideoPath,
        int streams = 10,
        int durationSecs = 30,
        int? parallel = null,
        string protocol = "rtmp")
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cargo",
            Arguments = $"run -p livestream-test-utils -- " +
                        $"--grpc-addr {grpcEndpoint} " +
                        $"--input-file {testVideoPath} " +
                        $"--streams {streams} " +
                        $"--duration {durationSecs} " +
                        $"--protocol {protocol} " +
                        $"--json" +
                        (parallel.HasValue ? $" --parallel {parallel.Value}" : ""),
            WorkingDirectory = Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..", "livestream-rs"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start stress test process");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException(
                $"Stress test exited with code {process.ExitCode}.\nStderr: {stderr}");
        }

        return JsonSerializer.Deserialize<StressReport>(stdout, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse stress test JSON output");
    }
}
