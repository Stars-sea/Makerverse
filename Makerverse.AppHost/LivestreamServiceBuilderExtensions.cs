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
        IResourceBuilder<ParameterResource> rtmpAppname,
        IResourceBuilder<ParameterResource> bucketName,
        string dockerfilePath = "../livestream-rs",
        int grpcPort = 50051,
        int rtmpPort = 1936,
        Range? srtPorts = null,
        uint duration = 10,
        int publishPort = 1935
    ) {
        srtPorts ??= 4000..4100;

        var container = builder.AddDockerfile(name, dockerfilePath)
            .WithOtlpExporter(OtlpProtocol.Grpc)
            .WithEnvironment("GRPC_PORT", grpcPort.ToString())
            .WithEnvironment("RTMP_PORT", rtmpPort.ToString())
            .WithEnvironment("SRT_PORTS", RangeToString(srtPorts.Value))
            .WithEnvironment("PERSISTENCE_DURATION", duration.ToString())
            .WithEnvironment("PUBLISH_PORT", publishPort.ToString())
            .WithEnvironment("RTMP_APPNAME", rtmpAppname)
            .WithEnvironment("MINIO_BUCKET", bucketName)
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
