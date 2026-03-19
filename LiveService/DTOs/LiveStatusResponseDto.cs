using System.Text;
using LiveService.Protos;

namespace LiveService.DTOs;

public record LiveStatusResponseDto(
    string PushUrl,
    string PullUrl,
    string? Passphrase
) {
    public static LiveStatusResponseDto FromResp(StreamInfoResponse response) {
        UriBuilder? pushUrlBuilder;
        switch (response.InputProtocol) {
            case InputProtocol.Srt:
            {
                StringBuilder query = new("mode=caller");
                query.Append($"&srt_streamid={response.LiveId}");
                if (!string.IsNullOrEmpty(response.Passphrase)) {
                    query.Append($"&passphrase={response.Passphrase}");
                    query.Append("&pbkeylen=32");
                }
                pushUrlBuilder = new UriBuilder {
                    Host   = response.Host,
                    Port   = (int)response.Port,
                    Query  = query.ToString(),
                    Scheme = "srt"
                };
                break;
            }
            case InputProtocol.Rtmp:
                pushUrlBuilder = new UriBuilder {
                    Host   = response.Host,
                    Port   = (int)response.Port,
                    Path   = $"{response.RtmpAppname}/{response.LiveId}",
                    Scheme = "rtmp"
                };
                break;
            default:
                throw new InvalidOperationException("Unsupported input protocol.");
        }
        
        UriBuilder pullUrlBuilder = new() {
            Host   = response.Host,
            Port   = (int)response.PullPort,
            Path   = $"{response.RtmpAppname}/{response.LiveId}",
            Scheme = "rtmp"
        };

        return new LiveStatusResponseDto(
            pushUrlBuilder.Uri.ToString(),
            pullUrlBuilder.Uri.ToString(),
            response.HasPassphrase ? response.Passphrase : null
        );
    }
}
