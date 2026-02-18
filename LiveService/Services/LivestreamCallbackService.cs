using Contracts;
using Grpc.Core;
using LiveService.Data;
using LiveService.Models;
using LiveService.Protos;
using Wolverine;

namespace LiveService.Services;

public class LivestreamCallbackService(
    IMessageBus bus,
    LiveDbContext db
) : LivestreamCallback.LivestreamCallbackBase {

    public override async Task<NotifyResponse> NotifyLivestreamConnected(NotifyConnectedRequest request, ServerCallContext context) {
        if (await db.Lives.FindAsync(request.LiveId) is not {} live)
            return new NotifyResponse();

        bool isValidTransition = live.Status == LiveStatus.Starting;
        live.Status    = LiveStatus.Started;
        live.StartedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        await bus.PublishAsync(new LiveConnected(live.Id, isValidTransition));

        return new NotifyResponse();
    }

    public override async Task<NotifyResponse> NotifyLivestreamTerminate(NotifyTerminateRequest request, ServerCallContext context) {
        if (await db.Lives.FindAsync(request.LiveId) is not {} live)
            return new NotifyResponse();

        bool isValidTransition = live.Status == LiveStatus.Stopping;
        live.Status    = LiveStatus.Stopped;
        live.StoppedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        await bus.PublishAsync(new LiveTerminate(live.Id, isValidTransition, request.ErrorMessage));

        return new NotifyResponse();
    }
}
