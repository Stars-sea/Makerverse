namespace LiveService.Options;

public class LivestreamOptions {
    public string Hostname { get; init; } = null!;

    public string BucketName { get; init; } = null!;

    public string SegmentPrefix { get; init; } = "hls";
}