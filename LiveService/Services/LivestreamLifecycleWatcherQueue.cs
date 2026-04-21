using System.Collections.Concurrent;
using System.Threading.Channels;

namespace LiveService.Services;

public class LivestreamLifecycleWatcherQueue {
    private readonly Channel<string> _queue;
    private readonly ConcurrentDictionary<string, byte> _pendingOrActive = new();

    public LivestreamLifecycleWatcherQueue(int capacity) {
        BoundedChannelOptions options = new(capacity) {
            FullMode = BoundedChannelFullMode.Wait
        };
        _queue = Channel.CreateBounded<string>(options);
    }

    public LivestreamLifecycleWatcherQueue() : this(100) {
    }

    public async ValueTask QueueWatcherAsync(string liveId) {
        if (!_pendingOrActive.TryAdd(liveId, 0)) return;

        try {
            await _queue.Writer.WriteAsync(liveId);
        }
        catch {
            _pendingOrActive.TryRemove(liveId, out _);
            throw;
        }
    }

    public async ValueTask<string> DequeueAsync(CancellationToken cancellationToken) {
        string liveId = await _queue.Reader.ReadAsync(cancellationToken);
        return liveId;
    }

    public void CompleteWatcher(string liveId) {
        _pendingOrActive.TryRemove(liveId, out _);
    }
}
