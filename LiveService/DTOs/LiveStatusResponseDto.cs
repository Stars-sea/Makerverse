using LiveService.Protos;

namespace LiveService.DTOs;

public record LiveStatusResponseDto(
    string UploadUrl,
    string Passphrase
) {
    public static LiveStatusResponseDto FromResp(StartPullStreamResponse response) {
        UriBuilder builder = new() {
            Host   = response.Host,
            Port   = (int)response.Port,
            Query  = "mode=caller",
            Scheme = "srt"
        };
        return new LiveStatusResponseDto(
            builder.Uri.ToString(),
            response.Passphrase
        );
    }

    public static LiveStatusResponseDto FromResp(GetStreamInfoResponse response) {
        UriBuilder builder = new() {
            Host   = response.Host,
            Port   = (int)response.Port,
            Query  = "mode=caller",
            Scheme = "srt"
        };
        return new LiveStatusResponseDto(
            builder.Uri.ToString(),
            response.Passphrase
        );
    }
}
