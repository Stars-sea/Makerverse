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
        IResourceBuilder<ParameterResource> rtmpAppName,
        IResourceBuilder<ParameterResource> bucketName,
        string dockerfilePath = "../livestream-rs",
        int grpcPort = 50051,
        int rtmpPort = 1936,
        int rtmpTtl = 30,
        bool httpFlvEnabled = true,
        int httpFlvPort = 8080,
        Range? srtPorts = null,
        uint duration = 10
    ) {
        srtPorts ??= 4000..4100;

        var container = builder.AddDockerfile(name, dockerfilePath)
            .WithOtlpExporter(OtlpProtocol.Grpc)
            .WithEnvironment("GRPC__PORT", grpcPort.ToString())
            .WithEnvironment("RTMP__PORT", rtmpPort.ToString())
            .WithEnvironment("RTMP__APP_NAME", rtmpAppName)
            .WithEnvironment("RTMP__SESSION_TTL_SECS", rtmpTtl.ToString())
            .WithEnvironment("HTTP_FLV__ENABLED", httpFlvEnabled.ToString())
            .WithEnvironment("HTTP_FLV__PORT", httpFlvPort.ToString())
            .WithEnvironment("SRT__PORTS", RangeToString(srtPorts.Value))
            .WithEnvironment("PERSISTENCE__DURATION", duration.ToString())
            .WithEnvironment("MINIO_BUCKET", bucketName)
            .WithEndpoint(port: grpcPort, targetPort: grpcPort, scheme: "http", name: "grpc")
            .WithEndpoint(port: rtmpPort, targetPort: rtmpPort, scheme: "rtmp", name: "rtmp")
            .WithEndpoint(port: httpFlvPort, targetPort: httpFlvPort, scheme: "http", name: "http-flv");

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
