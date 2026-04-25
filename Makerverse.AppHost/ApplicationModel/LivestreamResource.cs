using System.Net.Sockets;

namespace Makerverse.AppHost.ApplicationModel;

public sealed class LivestreamResource : ContainerResource, IResourceWithConnectionString {
    internal const string GrpcEndpointName    = "grpc";
    internal const string RtmpEndpointName    = "rtmp";
    internal const string HttpFlvEndpointName = "http-flv";

    public const           int        DefaultRtmpTtl  = 30;
    public const           uint       DefaultDuration = 10;
    public static readonly (int, int) DefaultSrtPorts = (40000, 40100);

    private EndpointReference? _grpcEndpoint;
    private EndpointReference? _rtmpEndpoint;
    private EndpointReference? _httpFlvEndpoint;

    public ReferenceExpression ConnectionStringExpression { get; }

    public EndpointReference GrpcEndpoint =>
        _grpcEndpoint ??= new EndpointReference(this, GrpcEndpointName);

    public EndpointReference RtmpEndpoint =>
        _rtmpEndpoint ??= new EndpointReference(this, RtmpEndpointName);

    public EndpointReference HttpFlvEndpoint =>
        _httpFlvEndpoint ??= new EndpointReference(this, HttpFlvEndpointName);

    public ParameterResource BucketName { get; }
    public ParameterResource RtmpTtl { get; }
    public ParameterResource SrtPorts { get; }
    public ParameterResource Duration { get; }

    public LivestreamResource(
        string name,
        ParameterResource bucketName,
        ParameterResource rtmpTtl,
        ParameterResource srtPorts,
        ParameterResource duration
    ) : base(name) {
        BucketName = bucketName;
        RtmpTtl    = rtmpTtl;
        SrtPorts   = srtPorts;
        Duration   = duration;

        ConnectionStringExpression = ReferenceExpression.Create($"{GrpcEndpoint.Property(EndpointProperty.Url)}");

        Annotations.Add(new EnvironmentCallbackAnnotation(ctx => {
            ctx.EnvironmentVariables["RTMP__APP_NAME"]         = "lives";
            ctx.EnvironmentVariables["RTMP__SESSION_TTL_SECS"] = RtmpTtl;
            ctx.EnvironmentVariables["HTTP_FLV__ENABLED"]      = true;
            ctx.EnvironmentVariables["SRT__PORTS"]             = SrtPorts;
            ctx.EnvironmentVariables["PERSISTENCE__DURATION"]  = Duration;
            ctx.EnvironmentVariables["MINIO_BUCKET"]           = BucketName;
        }));

        (int start, int end) = ParseRange(SrtPorts);
        for (int srtPort = start; srtPort <= end; srtPort++) {
            Annotations.Add(new EndpointAnnotation(
                ProtocolType.Udp,
                uriScheme: "srt",
                name: $"srt-{srtPort}",
                port: srtPort,
                targetPort: srtPort,
                isExternal: true));
        }
    }

    private static (int, int) ParseRange(ParameterResource param) {
#pragma warning disable CS0618
        string val = param.Value;
#pragma warning restore CS0618
        if (string.IsNullOrEmpty(val))
            return DefaultSrtPorts;

        string[] parts = val.Split('-');
        if (parts.Length == 2 && int.TryParse(parts[0], out int start) && int.TryParse(parts[1], out int end))
            return (start, end);

        return DefaultSrtPorts;
    }
}
