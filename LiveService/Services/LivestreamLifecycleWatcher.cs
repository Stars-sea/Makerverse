using Contracts;
using LiveService.Data;
using LiveService.Models;
using LiveService.Protos;
using Wolverine;

namespace LiveService.Services;

public class LivestreamLifecycleWatcher(
    IServiceProvider                    services,
    ILogger<LivestreamLifecycleWatcher> logger,
    LivestreamLifecycleWatcherQueue     queue
) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        logger.LogInformation("LivestreamLifecycleWatcher started.");
        await ProcessQueueAsync(stoppingToken);
    }

    private async Task ProcessQueueAsync(CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            string liveId = await queue.DequeueAsync(ct);

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = WatchLifecycleAsync(liveId, cts.Token);
        }
    }

    private async Task WatchLifecycleAsync(string liveId, CancellationToken ct) {
        try {
            await using AsyncServiceScope scope = services.CreateAsyncScope();

            var livestreamService = scope.ServiceProvider.GetService<LivestreamService>()!;
            var dbContext         = scope.ServiceProvider.GetService<LiveDbContext>()!;
            var bus               = scope.ServiceProvider.GetService<IMessageBus>()!;

            Live? live = await dbContext.Lives.FindAsync([liveId], ct);
            if (live == null) {
                logger.LogWarning("LivestreamLifecycleWatcher could not find Live {LiveId}, exiting.", liveId);
                return;
            }

            IAsyncEnumerable<SessionStatus> statusStream = livestreamService.WatchSessionStatusAsync(liveId, ct);

            var previousStatus = SessionStatus.Pending;
            await foreach (SessionStatus status in statusStream) {
                if (previousStatus == status) continue;

                live.Status = status switch {
                    SessionStatus.Pending      => LiveStatus.Created,
                    SessionStatus.Connecting   => LiveStatus.Starting,
                    SessionStatus.Connected    => LiveStatus.Started,
                    SessionStatus.Disconnected => LiveStatus.Stopped,
                    _                          => live.Status
                };

                switch (status) {
                    case SessionStatus.Connected: {
                        live.StartedAt = DateTime.UtcNow;

                        bool isValidTransition = previousStatus is SessionStatus.Connecting or SessionStatus.Pending;
                        await bus.PublishAsync(new LiveConnected(liveId, isValidTransition));
                        break;
                    }
                    case SessionStatus.Disconnected: {
                        live.StoppedAt ??= DateTime.UtcNow;

                        bool isValidTransition = previousStatus == SessionStatus.Connected;
                        await bus.PublishAsync(new LiveTerminate(liveId, isValidTransition, null));
                        break;
                    }
                    case SessionStatus.Pending:
                    case SessionStatus.Connecting:
                    default:
                        break;
                }

                await dbContext.SaveChangesAsync(ct);

                previousStatus = status;

                if (status == SessionStatus.Disconnected) break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) {
        }
        catch (Exception ex) {
            logger.LogError(ex, "LivestreamLifecycleWatcher failed for Live {LiveId}.", liveId);
        }
        finally {
            queue.CompleteWatcher(liveId);
        }
    }
}