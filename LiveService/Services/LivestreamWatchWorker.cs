using LiveService.Data;
using LiveService.Models;
using StackExchange.Redis;

namespace LiveService.Services;

public sealed class LivestreamWatchWorker(
    LiveDbContext db,
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

    private async void RedisChannelSubscriber(RedisChannel channel, RedisValue message, CancellationToken token) {
        try {
            string[] parts = channel.ToString().Split(':');
            if (parts.Length != 4) return;

            string liveId    = parts[3];
            string eventType = message.ToString();

            if (await db.Lives.FindAsync([liveId], token) is not {} live) {
                logger.LogWarning("Received unknown live id: {LiveId}, ignored", liveId);
                return;
            }
            if (eventType != "connected" && eventType != "terminate") {
                logger.LogWarning("Received unknown event type: {EventType} for live {LiveId}, ignored", eventType, liveId);
                return;
            }

            logger.LogDebug("Received event {EventType} for live {LiveId}", eventType, liveId);

            // TODO: implement LivestreamService
            live.Status = eventType switch {
                "terminate" => LiveStatus.Stopped,
                "connected" => LiveStatus.Started,
                _           => live.Status
            };
            await db.SaveChangesAsync(token);
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
