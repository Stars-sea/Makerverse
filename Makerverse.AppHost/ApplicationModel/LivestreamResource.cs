namespace Makerverse.AppHost.ApplicationModel;

public sealed class LivestreamResource : ContainerResource, IResourceWithConnectionString {
    internal const string GrpcEndpointName    = "grpc";
    internal const string RtmpEndpointName    = "rtmp";
    internal const string HttpFlvEndpointName = "http-flv";
    internal const string RtspEndpointName    = "rtsp";

    public const           int        DefaultRtmpTtl  = 30;
    public const           uint       DefaultDuration = 10;

    private EndpointReference? _grpcEndpoint;
    private EndpointReference? _rtmpEndpoint;
    private EndpointReference? _httpFlvEndpoint;
    private EndpointReference? _rtspEndpoint;

    public ReferenceExpression ConnectionStringExpression { get; }

    public EndpointReference GrpcEndpoint =>
        _grpcEndpoint ??= new EndpointReference(this, GrpcEndpointName);

    public EndpointReference RtmpEndpoint =>
        _rtmpEndpoint ??= new EndpointReference(this, RtmpEndpointName);

    public EndpointReference HttpFlvEndpoint =>
        _httpFlvEndpoint ??= new EndpointReference(this, HttpFlvEndpointName);

    public EndpointReference RtspEndpoint =>
        _rtspEndpoint ??= new EndpointReference(this, RtspEndpointName);

    public ParameterResource BucketName { get; }
    public ParameterResource RtmpTtl { get; }
    public ParameterResource Duration { get; }
    public int RtspPort { get; }

    public LivestreamResource(
        string name,
        ParameterResource bucketName,
        ParameterResource rtmpTtl,
        int rtspPort,
        ParameterResource duration
    ) : base(name) {
        BucketName = bucketName;
        RtmpTtl    = rtmpTtl;
        RtspPort   = rtspPort;
        Duration   = duration;

        ConnectionStringExpression = ReferenceExpression.Create($"{GrpcEndpoint.Property(EndpointProperty.Url)}");

        Annotations.Add(new EnvironmentCallbackAnnotation(ctx => {
            ctx.EnvironmentVariables["RTMP__APP_NAME"]         = "lives";
            ctx.EnvironmentVariables["RTMP__SESSION_TTL_SECS"] = RtmpTtl;
            ctx.EnvironmentVariables["HTTP_FLV__ENABLED"]      = true;
            ctx.EnvironmentVariables["SEGMENT__DURATION_SECS"] = Duration;
            ctx.EnvironmentVariables["MINIO_BUCKET"]           = BucketName;
        }));
    }
}
