using ErrorOr;
using LiveService.Data;
using LiveService.Models;
using LiveService.Protos;

namespace LiveService.Services;

public class LivestreamService(
    LiveDbContext db,
    Livestream.LivestreamClient grpc
) {

    private static string GeneratePassphrase(int length) {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        Random random = new();
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());

    }

    public async Task<ErrorOr<StreamInfoResponse>> StartLivestreamAsync(
        string liveId,
        CancellationToken ct = default
    ) {
        if (await db.Lives.FindAsync([liveId], ct) is not {} live) {
            return Error.NotFound("LiveNotFound", $"Live with id {liveId} not found.");
        }

        if (live.Status != LiveStatus.Created) {
            return Error.Conflict(
                "LiveInvalidStatus",
                $"Live with id {liveId} is not in a valid status to start. Current status: {live.Status}"
            );
        }

        StreamInfoResponse? resp;
        try {
            resp = await grpc.StartPullStreamAsync(
                new StartPullStreamRequest {
                    LiveId     = liveId,
                    Passphrase = GeneratePassphrase(32)
                },
                cancellationToken: ct
            )!;
        }
        catch (Exception e) {
            return Error.Failure(
                "LivestreamStartFailed",
                $"Failed to start livestream for live {liveId}: {e.Message}"
            );
        }

        live.Status = LiveStatus.Starting;
        await db.SaveChangesAsync(ct);

        return resp;
    }

    public async Task<ErrorOr<bool>> StopLivestreamAsync(
        string liveId,
        CancellationToken ct = default
    ) {
        if (await db.Lives.FindAsync([liveId], ct) is not {} live) {
            return Error.NotFound("LiveNotFound", $"Live with id {liveId} not found.");
        }

        if (live.Status != LiveStatus.Started) {
            return Error.Conflict(
                "LiveInvalidStatus",
                $"Live with id {liveId} is not in a valid status to stop. Current status: {live.Status}"
            );
        }

        StopPullStreamResponse? resp;
        try {
            resp = await grpc.StopPullStreamAsync(
                new StopPullStreamRequest {
                    LiveId = liveId
                },
                cancellationToken: ct
            )!;
        }
        catch (Exception e) {
            return Error.Failure(
                "LivestreamStopFailed",
                $"Failed to stop livestream for live {liveId}: {e.Message}"
            );
        }

        live.Status = LiveStatus.Stopping;
        await db.SaveChangesAsync(ct);

        return resp.IsSuccess;
    }

    public async Task<ErrorOr<StreamInfoResponse>> GetStreamInfoAsync(
        string liveId,
        CancellationToken ct = default
    ) {
        try {
            return await grpc.GetStreamInfoAsync(
                new GetStreamInfoRequest {
                    LiveId = liveId
                },
                cancellationToken: ct
            )!;
        }
        catch (Exception e) {
            return Error.Failure(
                "GetStreamInfoFailed",
                $"Failed to get stream info for live {liveId}: {e.Message}"
            );
        }
    }

    public async Task<ErrorOr<IEnumerable<string>>> GetActiveStreamAsync(CancellationToken ct = default) {
        try {
            ListActiveStreamsResponse resp = await grpc.ListActiveStreamsAsync(
                new ListActiveStreamsRequest(),
                cancellationToken: ct
            )!;
            return resp.LiveIds;
        }
        catch (Exception e) {
            return Error.Failure(
                "ListActiveStreamsFailed",
                $"Failed to list active streams: {e.Message}"
            );
        }
    }
}
