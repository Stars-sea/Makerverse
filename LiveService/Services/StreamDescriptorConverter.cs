using System.Text;
using LiveService.DTOs;
using LiveService.Options;
using LiveService.Protos;
using Microsoft.Extensions.Options;

namespace LiveService.Services;

public class StreamDescriptorConverter(
    IOptions<LivestreamOptions> options
) {
    private string Host => options.Value.Hostname;

    public string BuildPushUri(StreamDescriptor descriptor) {
        string         liveId   = descriptor.LiveId;
        StreamEndpoint endpoint = descriptor.Endpoint;

        UriBuilder pushUrlBuilder = descriptor.InputProtocol switch {
            InputProtocol.Rtmp => BuildRtmpUri(liveId, endpoint),
            InputProtocol.Srt  => BuildSrtUri(liveId, endpoint),
            _                  => throw new ArgumentException($"Unsupported input protocol: {descriptor.InputProtocol}")
        };

        return pushUrlBuilder.Uri.ToString();
    }

    public string BuildPullUri(string liveId, StreamEndpoint endpoint) {
        UriBuilder pullUrlBuilder = new() {
            Host   = Host,
            Port   = (int)endpoint.RtmpPort,
            Path   = $"{endpoint.RtmpAppname}/{liveId}",
            Scheme = "rtmp"
        };
        
        return pullUrlBuilder.Uri.ToString();
    }

    private UriBuilder BuildSrtUri(string liveId, StreamEndpoint endpoint) {
        StringBuilder query = new("mode=caller");
        query.Append($"&srt_streamid={liveId}");
        if (endpoint.HasPassphrase) {
            query.Append($"&passphrase={endpoint.Passphrase}");
            query.Append("&pbkeylen=32");
        }
        return new UriBuilder {
            Host   = Host,
            Port   = (int)endpoint.Port,
            Query  = query.ToString(),
            Scheme = "srt"
        };
    }

    private UriBuilder BuildRtmpUri(string liveId, StreamEndpoint endpoint) {
        return new UriBuilder {
            Host   = Host,
            Port   = (int)endpoint.Port,
            Path   = $"{endpoint.RtmpAppname}/{liveId}",
            Scheme = "rtmp"
        };
    }
}
