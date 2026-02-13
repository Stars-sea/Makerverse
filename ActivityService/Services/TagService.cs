using ActivityService.Data;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace ActivityService.Services;

public class TagService(
    IConnectionMultiplexer redis,
    ActivityDbContext db,
    ILogger<TagService> logger
) {
    private const string CacheKey = "Tags";

    private IDatabase Cache => redis.GetDatabase();

    private static readonly SemaphoreSlim Semaphore = new(1, 1);

    private async ValueTask<IEnumerable<string>> UpdateTagsAsync() {
        List<string>? tags = null;
        try {
            tags = await db.Tags.AsNoTracking()
                .Select(t => t.Slug)
                .ToListAsync();

            if (tags.Count == 0) return tags;

            IBatch batch = Cache.CreateBatch();

            var delTask = batch.KeyDeleteAsync(CacheKey);
            var addTask = batch.SetAddAsync(CacheKey, tags.Select(s => (RedisValue)s).ToArray());
            var expTask = batch.KeyExpireAsync(CacheKey, TimeSpan.FromMinutes(10));

            batch.Execute();

            await Task.WhenAll(delTask, addTask, expTask);

            return tags;
        }
        catch (Exception e) {
            logger.LogError(e, "An error occurred while updating tags cache.");
            return tags ??
                   await db.Tags.AsNoTracking()
                       .Select(t => t.Slug)
                       .ToListAsync();
        }
    }

    public async ValueTask<IEnumerable<string>> GetOrUpdateTagsAsync() {
        if (await Cache.KeyExistsAsync(CacheKey))
            return (await Cache.SetMembersAsync(CacheKey)).Select(v => v.ToString());

        try {
            await Semaphore.WaitAsync();

            if (await Cache.KeyExistsAsync(CacheKey))
                return (await Cache.SetMembersAsync(CacheKey)).Select(v => v.ToString());

            return await UpdateTagsAsync();
        }
        finally {
            Semaphore.Release();
        }
    }

    public async ValueTask InvalidateCacheAsync() {
        await Cache.KeyDeleteAsync(CacheKey);
    }

    public async ValueTask<bool> AreTagsValidAsync(IEnumerable<string> tags) {
        string[] tagArray = tags as string[] ?? tags.ToArray();
        if (tagArray.Length == 0) return true;

        if (!await Cache.KeyExistsAsync(CacheKey))
            await GetOrUpdateTagsAsync();

        var    redisValues = tagArray.Select(s => (RedisValue)s).ToArray();
        bool[] results     = await Cache.SetContainsAsync(CacheKey, redisValues);

        return results.All(r => r);
    }
}
