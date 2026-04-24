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

    public string BuildIngestUri(StreamDescriptor descriptor) {
        IngestEndpoints endpoint = descriptor.Endpoints.Ingest;

        UriBuilder pushUrlBuilder = descriptor.InputProtocol switch {
            InputProtocol.Rtmp => BuildRtmpUri(endpoint.Rtmp),
            InputProtocol.Srt  => BuildSrtIngestUri(endpoint.Srt),
            _                  => throw new ArgumentException($"Unsupported input protocol: {descriptor.InputProtocol}")
        };

        return pushUrlBuilder.Uri.ToString();
    }

    public PlaybackEndpointDto BuildPlaybackUri(StreamDescriptor descriptor) {
        PlaybackEndpoints endpoint = descriptor.Endpoints.Playback;

        UriBuilder rtmpPlaybackUrlBuilder = BuildRtmpUri(endpoint.Rtmp);
        UriBuilder? httpFlvPlaybackUrlBuilder = endpoint.HttpFlv != null
            ? BuildHttpFlvPlaybackUri(endpoint.HttpFlv)
            : null;

        return new PlaybackEndpointDto(
            rtmpPlaybackUrlBuilder.Uri.ToString(),
            httpFlvPlaybackUrlBuilder?.Uri.ToString()
        );
    }

    private UriBuilder BuildSrtIngestUri(SrtIngestEndpoint endpoint) {
        StringBuilder query = new("mode=caller");
        query.Append($"&srt_streamid={endpoint.LiveId}");
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

    private UriBuilder BuildRtmpUri(RtmpEndpoint endpoint) {
        return new UriBuilder {
            Host   = Host,
            Port   = (int)endpoint.Port,
            Path   = $"{endpoint.AppName}/{endpoint.StreamKey}",
            Scheme = "rtmp"
        };
    }

    private UriBuilder BuildHttpFlvPlaybackUri(HttpFlvPlaybackEndpoint endpoint) {
        return new UriBuilder {
            Host   = Host,
            Port   = (int)endpoint.Port,
            Path   = endpoint.Path,
            Scheme = "http"
        };
    }
}
