using System.Globalization;

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
    
    /// <summary>
    /// Wires MinIO into the livestream service: injects the connection string
    /// (ConnectionStrings__minio) plus the MINIO__* env vars the Rust binary
    /// reads (config-rs `__` nesting). More specific than the Aspire generic
    /// <c>WithReference</c> overload, so <c>WithReference(minio)</c> on a
    /// LivestreamResource resolves here.
    /// </summary>
    public static IResourceBuilder<LivestreamResource> WithReference(
        this IResourceBuilder<LivestreamResource> builder,
        IResourceBuilder<MinioContainerResource>  minio) {
        builder.WithReference((IResourceBuilder<IResourceWithConnectionString>)minio);
        return builder
            .WithEnvironment("MINIO__URI", minio.Resource.PrimaryEndpoint)
            .WithEnvironment("MINIO__ACCESS_KEY", minio.Resource.RootUser)
            .WithEnvironment("MINIO__SECRET_KEY", minio.Resource.PasswordParameter)
            .WithEnvironment("MINIO__BUCKET", builder.Resource.BucketName);
    }

    public static IResourceBuilder<LivestreamResource> WithTranscodeConfig(
        this IResourceBuilder<LivestreamResource> builder, 
        LivestreamTranscodeConfig config
    ) {
        if (config.Fps.HasValue)
            builder.WithEnvironment("TRANSCODE__FPS", config.Fps.Value.ToString(CultureInfo.InvariantCulture));
        
        return builder
            .WithEnvironment("TRANSCODE__BITRATE_KBPS", config.Bitrate.ToString())
            .WithEnvironment("TRANSCODE__PRESET", config.PresetString)
            .WithEnvironment("TRANSCODE__GOP_SECS", config.GopSecs.ToString(CultureInfo.InvariantCulture));
    }

    public static IResourceBuilder<LivestreamResource> WithTranscodeConfig(
        this IResourceBuilder<LivestreamResource>                  builder,
        Func<LivestreamTranscodeConfig, LivestreamTranscodeConfig> configure
    ) {
        return builder.WithTranscodeConfig(configure(new LivestreamTranscodeConfig()));
    }
}