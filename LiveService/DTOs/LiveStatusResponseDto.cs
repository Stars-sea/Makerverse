using System.Text;
using LiveService.Protos;

namespace LiveService.DTOs;

public record LiveStatusResponseDto(
    string PushUrl,
    string PullUrl,
    string? Passphrase
) {
    public static LiveStatusResponseDto FromResp(StreamDescriptor response) {
        StreamEndpoint endpoint = response.Endpoint;
        
        UriBuilder? pushUrlBuilder;
        switch (response.InputProtocol) {
            case InputProtocol.Srt:
            {
                StringBuilder query = new("mode=caller");
                query.Append($"&srt_streamid={response.LiveId}");
                if (endpoint.HasPassphrase) {
                    query.Append($"&passphrase={endpoint.Passphrase}");
                    query.Append("&pbkeylen=32");
                }
                pushUrlBuilder = new UriBuilder {
                    Host   = "TODO", // TODO
                    Port   = (int)endpoint.Port,
                    Query  = query.ToString(),
                    Scheme = "srt"
                };
                break;
            }
            case InputProtocol.Rtmp:
                pushUrlBuilder = new UriBuilder {
                    Host   = "TODO", // TODO
                    Port   = (int)endpoint.Port,
                    Path   = $"{endpoint.RtmpAppname}/{response.LiveId}",
                    Scheme = "rtmp"
                };
                break;
            default:
                throw new InvalidOperationException("Unsupported input protocol.");
        }
        
        UriBuilder pullUrlBuilder = new() {
            Host   = "TODO", // TODO
            Port   = (int)endpoint.Port,
            Path   = $"{endpoint.RtmpAppname}/{response.LiveId}",
            Scheme = "rtmp"
        };

        return new LiveStatusResponseDto(
            pushUrlBuilder.Uri.ToString(),
            pullUrlBuilder.Uri.ToString(),
            endpoint.HasPassphrase ? endpoint.Passphrase : null
        );
    }
}
