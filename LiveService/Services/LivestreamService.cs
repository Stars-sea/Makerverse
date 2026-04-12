using System.Runtime.CompilerServices;
using ErrorOr;
using Grpc.Core;
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

    public async Task<ErrorOr<StartLivestreamResponse>> StartLivestreamAsync(
        string liveId,
        InputProtocol protocol,
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

        StartLivestreamResponse? resp;
        try {
            resp = await grpc.StartLivestreamAsync(
                new StartLivestreamRequest {
                    LiveId        = liveId,
                    Passphrase    = GeneratePassphrase(32),
                    InputProtocol = protocol
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

        StopLivestreamResponse? resp;
        try {
            resp = await grpc.StopLivestreamAsync(
                new StopLivestreamRequest {
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

    public async Task<ErrorOr<GetLivestreamInfoResponse>> GetStreamInfoAsync(
        string liveId,
        CancellationToken ct = default
    ) {
        try {
            return await grpc.GetLivestreamInfoAsync(
                new GetLivestreamInfoRequest {
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

    public async Task<ErrorOr<IEnumerable<StreamDescriptor>>> GetActiveStreamAsync(CancellationToken ct = default) {
        try {
            ListLivestreamsResponse resp = await grpc.ListLivestreamsAsync(
                new ListLivestreamsRequest(),
                cancellationToken: ct
            )!;
            return resp.Streams;
        }
        catch (Exception e) {
            return Error.Failure(
                "ListActiveStreamsFailed",
                $"Failed to list active streams: {e.Message}"
            );
        }
    }

    /// <summary>
    /// <para>Watches the livestream session status for the specified live and yields updates as they come in.</para>
    /// <para>For use by <c>LivestreamLifecycleWatcher</c> only.</para>
    /// </summary>
    public async IAsyncEnumerable<SessionStatus> WatchSessionStatusAsync(
        string liveId,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        var call = grpc.WatchLivestream(
            new WatchLivestreamRequest {
                LiveId = liveId
            },
            cancellationToken: ct
        );

        var stream = call.ResponseStream.ReadAllAsync(ct);
        await foreach (WatchLivestreamResponse response in stream) {
            yield return response.Stream;
        }
    }
}
