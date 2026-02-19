using System.Runtime.CompilerServices;
using ErrorOr;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace LiveService.Services;

// TODO: Add caching layer to avoid hitting MinIO for every segment request.

public class LivestreamPersistentService(
    IMinioClient minio,
    ILogger<LivestreamPersistentService> logger
) {
    private const string BucketName = "videos";

    private static string ObjectName(string id, int num) => $"{id}/segment_{num:0000}.mp4";

    public async IAsyncEnumerable<string> ListSegmentsAsync(string liveId, [EnumeratorCancellation] CancellationToken ct = default) {
        ListObjectsArgs args = new ListObjectsArgs()
            .WithBucket(BucketName)
            .WithPrefix($"{liveId}/");
        IAsyncEnumerable<Item>? enumerable = minio.ListObjectsEnumAsync(args, ct);

        await foreach (Item item in enumerable) {
            yield return item.Key;
        }
    }

    public async Task<Error?> GetSegmentAsync(string liveId, int segmentNum, Stream outputStream, CancellationToken ct = default) {
        try {
            GetObjectArgs args = new GetObjectArgs()
                .WithBucket(BucketName)
                .WithObject(ObjectName(liveId, segmentNum))
                .WithCallbackStream(CallbackStream);
            await minio.GetObjectAsync(args, ct);
        }
        catch (MinioException e) {
            logger.LogWarning(e, "MinIO error while getting segment {id}: {message}", liveId, e.Message);
            return Error.NotFound("Segment not found.");
        }

        return null;

        async void CallbackStream(Stream stream) {
            try {
                await stream.CopyToAsync(outputStream, ct);
            }
            catch (Exception e) {
                logger.LogError(e, "Error while copying segment {id} to output stream", liveId);
            }
        }
    }
}
