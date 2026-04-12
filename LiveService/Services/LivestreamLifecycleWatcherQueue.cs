using System.Threading.Channels;

namespace LiveService.Services;

public class LivestreamLifecycleWatcherQueue {
    private readonly Channel<string> _queue;

    public LivestreamLifecycleWatcherQueue(int capacity) {
        BoundedChannelOptions options = new(capacity) {
            FullMode = BoundedChannelFullMode.Wait
        };
        _queue = Channel.CreateBounded<string>(options);
    }

    public LivestreamLifecycleWatcherQueue() : this(100) {
    }

    public async ValueTask QueueWatcherAsync(string liveId) {
        await _queue.Writer.WriteAsync(liveId);
    }

    public async ValueTask<string> DequeueAsync(CancellationToken cancellationToken) {
        string liveId = await _queue.Reader.ReadAsync(cancellationToken);
        return liveId;
    }
}
