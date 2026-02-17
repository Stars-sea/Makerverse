using System.Text;
using LiveService.Protos;

namespace LiveService.DTOs;

public record LiveStatusResponseDto(
    string UploadUrl,
    string Passphrase
) {
    public static LiveStatusResponseDto FromResp(StreamInfoResponse response) {
        StringBuilder query = new("mode=caller");
        query.Append($"&srt_streamid={response.LiveId}");
        if (!string.IsNullOrEmpty(response.Passphrase)) {
            query.Append($"&passphrase={response.Passphrase}");
            query.Append("&pbkeylen=32");
        }
        UriBuilder builder = new() {
            Host   = response.Host,
            Port   = (int)response.Port,
            Query  = query.ToString(),
            Scheme = "srt"
        };
        return new LiveStatusResponseDto(
            builder.Uri.ToString(),
            response.Passphrase
        );
    }
}
