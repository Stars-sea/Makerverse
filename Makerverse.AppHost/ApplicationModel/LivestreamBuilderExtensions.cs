namespace Makerverse.AppHost.ApplicationModel;

public static class LivestreamBuilderExtensions {
    public static IResourceBuilder<LivestreamResource> AddLivestreamService(
        this IDistributedApplicationBuilder builder,
        string name,
        int grpcPort = 50050,
        int rtmpPort = 1935,
        int httpFlvPort = 8080,
        int rtspPort = 8554,
        IResourceBuilder<ParameterResource>? bucketName = null,
        IResourceBuilder<ParameterResource>? rtmpTtl = null,
        IResourceBuilder<ParameterResource>? duration = null,
        string dockerfilePath = "../livestream-rs"
    ) {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(name);

        var bucket = bucketName ?? builder.AddParameter($"{name}-bucket-name", "videos", true);
        var ttl    = rtmpTtl ?? builder.AddParameter($"{name}-rtmp-ttl", LivestreamResource.DefaultRtmpTtl.ToString(), true);
        var dur    = duration ?? builder.AddParameter($"{name}-duration", LivestreamResource.DefaultDuration.ToString(), true);

        LivestreamResource resource = new(
            name,
            bucket.Resource,
            ttl.Resource,
            rtspPort,
            dur.Resource
        );

        return builder.AddResource(resource)
            .WithImage("livestream-svc")
            .WithDockerfile(dockerfilePath)
            .WithOtlpExporter()
            .WithEndpoint(
                port: grpcPort,
                targetPort: grpcPort,
                scheme: "http",
                name: LivestreamResource.GrpcEndpointName,
                env: "GRPC__PORT")
            .WithEndpoint(
                port: rtmpPort,
                targetPort: rtmpPort,
                scheme: "rtmp",
                name: LivestreamResource.RtmpEndpointName,
                env: "RTMP__PORT",
                isExternal: true)
            .WithEndpoint(
                port: httpFlvPort,
                targetPort: httpFlvPort,
                scheme: "http",
                name: LivestreamResource.HttpFlvEndpointName,
                env: "HTTP_FLV__PORT",
                isExternal: true)
            .WithEndpoint(
                port: rtspPort,
                targetPort: rtspPort,
                scheme: "rtsp",
                name: LivestreamResource.RtspEndpointName,
                env: "RTSP__PORT",
                isExternal: true);
    }
}
