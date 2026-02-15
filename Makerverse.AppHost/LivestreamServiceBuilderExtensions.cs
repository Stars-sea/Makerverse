using System.Net.Sockets;

namespace Makerverse.AppHost;

public static class LivestreamServiceBuilderExtensions {

    private static string RangeToString(Range range) {
        if (range.Start.IsFromEnd || range.End.IsFromEnd) {
            throw new ArgumentException("Range values must be from the start.");
        }
        return $"{range.Start.Value}-{range.End.Value}";
    }

    public static IResourceBuilder<ContainerResource> AddLivestreamService(
        this IDistributedApplicationBuilder builder,
        string name,
        string dockerfilePath = "../livestream-rs",
        int port = 50051,
        string minioBucket = "videos",
        Range? srtPorts = null,
        uint segmentDuration = 10
    ) {
        srtPorts ??= 4000..4100;

        var container = builder.AddDockerfile(name, dockerfilePath)
            .WithEnvironment("GRPC_PORT", port.ToString())
            .WithEnvironment("MINIO_BUCKET", minioBucket)
            .WithEnvironment("SRT_PORTS", RangeToString(srtPorts.Value))
            .WithEnvironment("SEGMENT_DURATION", segmentDuration.ToString())
            .WithEndpoint(port: port, targetPort: port, scheme: "http", name: "grpc");

        (int offset, int length) = srtPorts.Value.GetOffsetAndLength(int.MaxValue);
        foreach (int srtPort in Enumerable.Range(offset, length)) {
            container.WithEndpoint(
                srtPort,
                srtPort,
                "srt",
                $"srt-{srtPort}",
                protocol: ProtocolType.Udp,
                isExternal: true
            );
        }

        return container;
    }
}
