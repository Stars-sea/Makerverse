using ActivityService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ActivityService.Services;

public class TagService(
    IMemoryCache cache,
    ActivityDbContext db
) {
    private const string CacheKey = "Tags";

    public async ValueTask<List<string>> GetTagSlugsAsync() {
        return await cache.GetOrCreateAsync(CacheKey, Factory) ?? [];

        async Task<List<string>> Factory(ICacheEntry entry) {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            var tags = await db.Tags.AsNoTracking()
                .Select(t => t.Slug)
                .ToListAsync();
            return tags;
        }
    }

    public void InvalidateCache() {
        cache.Remove(CacheKey);
    }

    public async ValueTask<bool> AreTagsValidAsync(IEnumerable<string> tags) {
        var tagsSet = (await GetTagSlugsAsync()).ToHashSet();
        return tags.All(t => tagsSet.Contains(t));
    }
}
