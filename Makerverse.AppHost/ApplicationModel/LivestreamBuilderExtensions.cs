namespace Makerverse.AppHost.ApplicationModel;

public static class LivestreamBuilderExtensions {
    public static IResourceBuilder<LivestreamResource> AddLivestreamService(
        this IDistributedApplicationBuilder  builder,
        string                               name,
        int                                  grpcPort       = 50050,
        int                                  rtmpPort       = 1935,
        int                                  httpFlvPort    = 8080,
        int                                  rtspPort       = 8554,
        IResourceBuilder<ParameterResource>? bucketName     = null,
        IResourceBuilder<ParameterResource>? rtmpTtl        = null,
        IResourceBuilder<ParameterResource>? duration       = null,
        string                               dockerfilePath = "../livestream-rs"
    ) {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(name);

        IResourceBuilder<ParameterResource> bucket =
            bucketName ?? builder.AddParameter($"{name}-bucket-name", "videos", true);
        IResourceBuilder<ParameterResource> ttl = rtmpTtl ??
                                                  builder.AddParameter($"{name}-rtmp-ttl",
                                                      LivestreamResource.DefaultRtmpTtl.ToString(), true);
        IResourceBuilder<ParameterResource> dur = duration ??
                                                  builder.AddParameter($"{name}-duration",
                                                      LivestreamResource.DefaultDuration.ToString(), true);

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
                grpcPort,
                grpcPort,
                "http",
                LivestreamResource.GrpcEndpointName,
                "GRPC__PORT")
            .WithEndpoint(
                rtmpPort,
                rtmpPort,
                "rtmp",
                LivestreamResource.RtmpEndpointName,
                "RTMP__PORT",
                isExternal: true)
            .WithEndpoint(
                httpFlvPort,
                httpFlvPort,
                "http",
                LivestreamResource.HttpFlvEndpointName,
                "HTTP_FLV__PORT",
                isExternal: true)
            .WithEndpoint(
                rtspPort,
                rtspPort,
                "rtsp",
                LivestreamResource.RtspEndpointName,
                "RTSP__PORT",
                isExternal: true);
    }
}