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
        int grpcPort = 50051,
        int rtmpPort = 1936,
        Range? srtPorts = null,
        uint duration = 10,
        int publishPort = 1935,
        string publishAppname = "lives",
        string minioBucket = "videos"
    ) {
        srtPorts ??= 4000..4100;

        var container = builder.AddDockerfile(name, dockerfilePath)
            .WithOtlpExporter(OtlpProtocol.Grpc)
            .WithEnvironment("INGEST_GRPCPORT", grpcPort.ToString())
            .WithEnvironment("INGEST_RTMPPORT", rtmpPort.ToString())
            .WithEnvironment("INGEST_SRTPORTS", RangeToString(srtPorts.Value))
            .WithEnvironment("INGEST_DURATION", duration.ToString())
            .WithEnvironment("PUBLISH_PORT", publishPort.ToString())
            .WithEnvironment("PUBLISH_APPNAME", publishAppname)
            .WithEnvironment("MINIO_BUCKET", minioBucket)
            .WithEndpoint(port: grpcPort, targetPort: grpcPort, scheme: "http", name: "grpc")
            .WithEndpoint(port: rtmpPort, targetPort: rtmpPort, scheme: "rtmp", name: "rtmp-ingest")
            .WithEndpoint(port: publishPort, targetPort: publishPort, scheme: "rtmp", name: "rtmp-publish");

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
