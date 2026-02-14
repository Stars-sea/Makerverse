using Contracts;
using LiveService.Services;

namespace LiveService.MessageHandlers;

public class LiveConnectedHandler(
    LivestreamService livestreamService,
    ILogger<LiveConnectedHandler> logger
) {

    public async Task HandleAsync(LiveConnected message) {
        if (message.IsValidTransition) return;
        
        logger.LogWarning("Received invalid live connected event for live {LiveId}, terminating the stream", message.LiveId);
        var ret = await livestreamService.StopLivestreamAsync(message.LiveId);
        if (!ret.Match(r => r, _ => false)) {
            logger.LogError("Failed to stop livestream for live {LiveId} after receiving invalid live connected event", message.LiveId);
        }
    }

}
