using Contracts;
using Grpc.Core;
using LiveService.Data;
using LiveService.Models;
using LiveService.Protos;
using Wolverine;

namespace LiveService.Services;

public class LivestreamCallbackService(
    IMessageBus bus,
    LiveDbContext db,
    ILogger<LivestreamCallbackService> logger
) : LivestreamCallback.LivestreamCallbackBase {

    public override async Task<NotifyResponse> NotifyLivestreamConnected(NotifyConnectedRequest request, ServerCallContext context) {
        if (await db.Lives.FindAsync(request.LiveId) is not {} live)
            return new NotifyResponse();

        bool isValidTransition = live.Status == LiveStatus.Starting;

        if (isValidTransition)
            logger.LogInformation("Received livestream connected notification for live {LiveId}", live.Id);
        else
            logger.LogWarning("Received livestream connected notification for live {LiveId} which is in status {Status}", live.Id, live.Status);

        live.Status    = LiveStatus.Started;
        live.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await bus.PublishAsync(new LiveConnected(live.Id, isValidTransition));

        return new NotifyResponse();
    }

    public override async Task<NotifyResponse> NotifyLivestreamTerminate(NotifyTerminateRequest request, ServerCallContext context) {
        if (await db.Lives.FindAsync(request.LiveId) is not {} live)
            return new NotifyResponse();

        bool isValidTransition = live.Status == LiveStatus.Stopping && request.ErrorMessage == null;

        if (isValidTransition)
            logger.LogInformation("Received livestream terminate notification for live {LiveId}", live.Id);
        else
            logger.LogWarning("Received livestream terminate notification for live {LiveId} which is in status {Status}", live.Id, live.Status);

        live.Status    = isValidTransition ? LiveStatus.Stopped : LiveStatus.Invalid;
        live.StoppedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await bus.PublishAsync(new LiveTerminate(live.Id, isValidTransition, request.ErrorMessage));

        return new NotifyResponse();
    }
}
