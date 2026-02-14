using LiveService.Data;
using LiveService.Models;
using StackExchange.Redis;

namespace LiveService.Services;

public sealed class LivestreamWatchWorker(
    IServiceProvider services,
    IConnectionMultiplexer redis,
    ILogger<LivestreamWatchWorker> logger
) : BackgroundService {

    private ISubscriber Subscriber => redis.GetSubscriber();

    private static RedisChannel ChannelPattern => RedisChannel.Pattern("livestream:events:*");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        await Subscriber.SubscribeAsync(
            ChannelPattern,
            (channel, message) => RedisChannelSubscriber(channel, message, stoppingToken)
        );

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async void RedisChannelSubscriber(RedisChannel channel, RedisValue message, CancellationToken ct) {
        try {
            using IServiceScope scope = services.CreateAsyncScope();
            LiveDbContext       db    = scope.ServiceProvider.GetRequiredService<LiveDbContext>();

            string[] parts = channel.ToString().Split(':');
            if (parts.Length != 4) return;

            string liveId    = parts[3];
            string eventType = message.ToString();

            if (await db.Lives.FindAsync([liveId], ct) is not {} live) {
                logger.LogWarning("Received unknown live id: {LiveId}, ignored", liveId);
                return;
            }
            if (eventType != "connected" && eventType != "terminate") {
                logger.LogWarning("Received unknown event type: {EventType} for live {LiveId}, ignored", eventType, liveId);
                return;
            }

            logger.LogDebug("Received event {EventType} for live {LiveId}", eventType, liveId);

            live.Status = eventType switch {
                "connected" when live.Status == LiveStatus.Started  => LiveStatus.Started,
                "terminate" when live.Status == LiveStatus.Stopping => LiveStatus.Stopped,
                _                                                   => LiveStatus.Invalid
            };
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e) {
            logger.LogError(e, "Error processing Redis message on channel {Channel}: {Message}", channel, message);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken) {
        await Subscriber.UnsubscribeAsync(ChannelPattern);
        await base.StopAsync(cancellationToken);
    }

}
