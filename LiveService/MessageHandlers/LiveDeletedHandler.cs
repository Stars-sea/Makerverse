using Contracts;
using LiveService.Services;

namespace LiveService.MessageHandlers;

public class LiveDeletedHandler(
    LivestreamPersistentService persistentService,
    ILogger<LiveDeletedHandler> logger
) {

    public async Task HandleAsync(LiveDeleted message) {
        if (await persistentService.DeleteSegmentsAsync(message.LiveId) is {} error) {
            logger.LogError("Failed to delete segments for live {LiveId}: {Error}", message.LiveId, error.ToString());
        }
    }

}
