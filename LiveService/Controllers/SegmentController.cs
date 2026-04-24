using Common;
using LiveService.Data;
using LiveService.Models;
using LiveService.Services;
using Microsoft.AspNetCore.Mvc;
using Minio.DataModel;

namespace LiveService.Controllers;

[ApiController]
[Route("lives/{liveId}/segments")]
public class SegmentController(
    LiveDbContext db,
    LivestreamPersistentService persistentService
) : ControllerBase {
    [HttpGet]
    public async Task<ActionResult<List<string>>> GetSegments(string liveId, CancellationToken ct) {
        if (await db.Lives.FindAsync([liveId], ct) is not {} live)
            return NotFound();
        if (live.Status is LiveStatus.Created or LiveStatus.Starting)
            return BadRequest("Live is not started yet.");

        List<string> segments = [];
        await foreach (string segment in persistentService.ListSegmentsAsync(liveId, ct)) {
            segments.Add(segment);
        }
        return segments;
    }

    [HttpGet("index.m3u8")]
    public async Task GetPlaylist(string liveId, CancellationToken ct) {
        if (await db.Lives.FindAsync([liveId], ct) is not {} live) {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (live.Status is LiveStatus.Created or LiveStatus.Starting) {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var statRet = await persistentService.GetPlaylistStatAsync(liveId, ct);
        if (statRet.IsError) {
            Response.StatusCode = StatusCodes.Status404NotFound;
            await Response.WriteAsJsonAsync(statRet.Errors, ct);
            return;
        }

        ObjectStat stat = statRet.Value;

        Response.ContentType   = "application/vnd.apple.mpegurl";
        Response.ContentLength = stat.Size;
        var ret = await persistentService.GetPlaylistAsync(liveId, Response.Body, ct);
        if (ret.IsError) {
            Response.StatusCode = StatusCodes.Status404NotFound;
            await Response.WriteAsJsonAsync(ret.Errors, ct);
        }
    }

    [HttpGet("{index:int}")]
    [HttpGet("segment_{index:int}.ts")]
    public async Task GetSegment(string liveId, int index, CancellationToken ct) {
        if (await db.Lives.FindAsync([liveId], ct) is not {} live) {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (live.Status is LiveStatus.Created or LiveStatus.Starting) {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsync("Live is not started yet.", ct);
            return;
        }

        var statRet = await persistentService.GetSegmentStatAsync(liveId, index, ct);
        if (statRet.IsError) {
            Response.StatusCode = StatusCodes.Status404NotFound;
            await Response.WriteAsJsonAsync(statRet.Errors, ct);
            return;
        }

        ObjectStat stat = statRet.Value;

        Response.ContentType          = "video/MP2T";
        Response.ContentLength        = stat.Size;
        Response.Headers.AcceptRanges = "bytes";
        Response.Headers.ETag         = stat.ETag;
        Response.Headers.LastModified = stat.LastModified.ToString("R");
        Response.Headers.CacheControl = "public, max-age=600";// Cache for 10 mins

        var ret = await persistentService.GetSegmentAsync(liveId, index, Response.Body, ct);
        if (ret.IsError) {
            Response.StatusCode = StatusCodes.Status404NotFound;
            await Response.WriteAsJsonAsync(ret.Errors, ct);
        }
    }
}
