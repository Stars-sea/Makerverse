using System.Runtime.CompilerServices;
using ErrorOr;
using LiveService.Options;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace LiveService.Services;

// TODO: Add caching layer to avoid hitting MinIO for every segment request.

public class LivestreamPersistentService(
    IMinioClient minio,
    ILogger<LivestreamPersistentService> logger,
    IOptions<LivestreamOptions> options
) {
    private string BucketName => options.Value.BucketName;

    private static string ObjectName(string id, int num) => $"{id}/segment_{num:0000}.ts";

    public async IAsyncEnumerable<string> ListSegmentsAsync(string liveId, [EnumeratorCancellation] CancellationToken ct = default) {
        ListObjectsArgs args = new ListObjectsArgs()
            .WithBucket(BucketName)
            .WithPrefix($"{liveId}/");
        IAsyncEnumerable<Item>? enumerable = minio.ListObjectsEnumAsync(args, ct);

        int prefixLen = liveId.Length + 1;
        await foreach (Item item in enumerable) {
            yield return item.Key[prefixLen..];
        }
    }

    public async Task<ErrorOr<ObjectStat>> GetSegmentStatAsync(string liveId, int segmentNum, CancellationToken ct = default) {
        try {
            StatObjectArgs args = new StatObjectArgs()
                .WithBucket(BucketName)
                .WithObject(ObjectName(liveId, segmentNum));
            return await minio.StatObjectAsync(args, ct);
        }
        catch (MinioException e) {
            logger.LogWarning(e, "MinIO error while getting stat of segment {id}: {message}", liveId, e.Message);
            return Error.NotFound("Segment not found.");
        }
    }

    public async Task<ErrorOr<ObjectStat>> GetSegmentAsync(string liveId, int segmentNum, Stream outputStream, CancellationToken ct = default) {
        string objectName = ObjectName(liveId, segmentNum);

        try {
            GetObjectArgs args = new GetObjectArgs()
                .WithBucket(BucketName)
                .WithObject(objectName)
                .WithCallbackStream(CallbackStream);
            return await minio.GetObjectAsync(args, ct);
        }
        catch (MinioException e) {
            logger.LogWarning(e, "MinIO error while getting segment {id}: {message}", liveId, e.Message);
            return Error.NotFound("Segment not found.");
        }

        async Task CallbackStream(Stream stream, CancellationToken token) {
            try {
                await stream.CopyToAsync(outputStream, token);
            }
            catch (Exception e) {
                logger.LogError(e, "Error while copying object {objectName} to output stream", objectName);
            }
        }
    }

    public async Task<Error?> DeleteSegmentsAsync(string liveId, CancellationToken ct = default) {
        List<string>? segmentKeys;
        try {
            segmentKeys = await ListSegmentsAsync(liveId, ct).ToListAsync(ct);
        }
        catch (MinioException e) {
            logger.LogWarning(e, "MinIO error while listing segments for live {id}: {message}", liveId, e.Message);
            return Error.Failure("Failed to list segments for deletion.");
        }

        if (segmentKeys.Count == 0)
            return null;

        RemoveObjectsArgs args = new RemoveObjectsArgs()
            .WithBucket(BucketName)
            .WithObjects(segmentKeys);
        try {
            await minio.RemoveObjectsAsync(args, ct);
        }
        catch (Exception e) {
            logger.LogWarning(e, "MinIO error while deleting segments for live {id}: {message}", liveId, e.Message);
            return Error.Failure("Failed to delete some or all segments.");
        }
        await minio.RemoveObjectsAsync(args, ct);
        return null;
    }
}
