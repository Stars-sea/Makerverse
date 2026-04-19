using System.Security.Claims;
using Common;
using Contracts;
using LiveService.Data;
using LiveService.DTOs;
using LiveService.Models;
using LiveService.Protos;
using LiveService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Minio.DataModel;
using Wolverine;

namespace LiveService.Controllers;

[ApiController]
[Route("[controller]")]
public class LivesController(
    LiveDbContext db,
    LivestreamService livestreamService,
    LivestreamPersistentService persistentService,
    LivestreamLifecycleWatcherQueue queue,
    IMessageBus bus,
    StreamDescriptorConverter descriptorConverter
) : ControllerBase {

    #region Live Management (CRUD)

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<Live>> CreateLive(CreateLiveDto dto) {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return BadRequest("Cannot get user details.");

        Live live = new() {
            Title      = dto.Title,
            StreamerId = userId,
        };
        db.Lives.Add(live);
        await db.SaveChangesAsync();

        await bus.PublishAsync(new LiveCreated(live.Id, live.Title, live.CreatedAt));

        return CreatedAtAction(
            nameof(GetLive),
            new {
                id = live.Id
            },
            live
        );
    }

    [HttpGet]
    public async Task<ActionResult<List<Live>>> GetLives() {
        return await db.Lives.AsQueryable()
            .OrderByDescending(x => x.StartedAt ?? x.CreatedAt).ToListAsync();
    }

    [HttpGet("streamer/{streamerId}")]
    public async Task<ActionResult<List<Live>>> GetLivesByStreamer(string streamerId) {
        return await db.Lives.AsQueryable()
            .Where(live => live.StreamerId == streamerId)
            .OrderByDescending(x => x.StartedAt ?? x.CreatedAt).ToListAsync();
    }

    [Authorize]
    [HttpGet("streamer/me")]
    public ActionResult<List<Live>> GetMyLives() {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return BadRequest("Cannot get user details.");

        return RedirectToAction(nameof(GetLivesByStreamer),
            new {
                streamerId = userId
            }
        );
    }

    [HttpGet("online")]
    public async Task<ActionResult<List<Live>>> ListOnlineLives() {
        var ret = await livestreamService.GetActiveStreamAsync();
        if (ret.IsError) return ret.FirstError.ToActionResult();

        if (!ret.Value.Any()) return Ok(new List<Live>());

        var onlineLives = ret.Value.Select(d => d.LiveId);

        return await db.Lives.AsQueryable()
            .Where(live => onlineLives.Contains(live.Id))
            .OrderByDescending(x => x.StartedAt ?? x.CreatedAt).ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Live>> GetLive(string id) {
        Live? live = await db.Lives.FindAsync(id);
        if (live is null) return NotFound();
        return live;
    }

    [Authorize]
    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateLive(string id, CreateLiveDto dto) {
        if (await db.Lives.FindAsync(id) is not {} live) return NotFound();

        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null || live.StreamerId != userId) return Forbid();

        live.Title = dto.Title;

        await db.SaveChangesAsync();

        await bus.PublishAsync(new LiveUpdated(live.Id, live.Title));

        return NoContent();
    }

    [Authorize]
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteLive(string id) {
        if (await db.Lives.FindAsync(id) is not {} live) return NotFound();

        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null || live.StreamerId != userId) return Forbid();

        if (live.Status != LiveStatus.Stopped && live.Status != LiveStatus.Invalid)
            return BadRequest("Cannot delete the active live.");

        db.Lives.Remove(live);
        await db.SaveChangesAsync();

        await bus.PublishAsync(new LiveDeleted(live.Id));

        return NoContent();
    }

    #endregion

    #region Livestream Control and Status

    [Authorize]
    [HttpPut("{id}/status")]
    public async Task<ActionResult<LivestreamEndpointDto>> UpdateLiveStatus(string id, UpdateLiveStatusDto dto) {
        if (await db.Lives.FindAsync(id) is not {} live) return NotFound();

        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null || live.StreamerId != userId) return Forbid();

        if (dto.Status == "start") {
            // TODO: Due to instability in the current SRT protocol implementation, it is temporarily hardcoded to RTMP.
            var ret = await livestreamService.StartLivestreamAsync(live.Id, InputProtocol.Rtmp);
            await queue.QueueWatcherAsync(live.Id);

            if (ret.IsError) return ret.FirstError.ToActionResult();

            StreamDescriptor descriptor = ret.Value.Stream;

            string  pullUrl    = descriptorConverter.BuildPullUri(live.Id, descriptor.Endpoint);
            string  pushUrl    = descriptorConverter.BuildPushUri(descriptor);
            string? passphrase = descriptor.Endpoint.HasPassphrase ? descriptor.Endpoint.Passphrase : null;

            return new LivestreamEndpointDto(pushUrl, pullUrl, passphrase);
        }

        if (dto.Status == "stop") {
            var ret = await livestreamService.StopLivestreamAsync(live.Id);
            return ret.MatchFirst(
                _ => NoContent(),
                error => error.ToActionResult()
            );
        }

        return BadRequest($"Invalid status value '{dto.Status}'.");
    }

    [Authorize]
    [HttpGet("{id}/endpoint")]
    [ProducesResponseType(typeof(LivestreamEndpointDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(VodEndpointDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetLiveEndpoint(string id) {
        if (await db.Lives.FindAsync(id) is not {} live) return NotFound();

        string? userId  = User.FindFirstValue(ClaimTypes.NameIdentifier);
        bool    isOwner = live.StreamerId == userId;

        if (live.Status is not (LiveStatus.Starting or LiveStatus.Started))
            return Ok(new VodEndpointDto(
                Url: $"/lives/{live.Id}/segments"
            ));

        var ret = await livestreamService.GetStreamInfoAsync(live.Id);
        if (ret.IsError) // TODO: Add telemetry of the error for debugging
            return ret.FirstError.ToActionResult();

        StreamDescriptor descriptor = ret.Value.Stream;

        string  pullUrl    = descriptorConverter.BuildPullUri(live.Id, descriptor.Endpoint);
        string? pushUrl    = isOwner ? descriptorConverter.BuildPushUri(descriptor) : null;
        string? passphrase = isOwner && descriptor.Endpoint.HasPassphrase ? descriptor.Endpoint.Passphrase : null;
        return Ok(new LivestreamEndpointDto(pushUrl, pullUrl, passphrase));
    }

    #endregion

    #region Live viewing (for viewers)

    // TODO:
    // - Generate the HLS manifest file for the live stream (LiveTerminateHandler)
    // - Preview live stream (generate preview image from the first segment or use a placeholder image)
    [HttpGet("{id}/segments")]
    public async Task<ActionResult<List<string>>> ListLiveSegments(string id) {
        if (await db.Lives.FindAsync(id) is not {} live) return NotFound();
        if (live.Status is LiveStatus.Created or LiveStatus.Starting)
            return BadRequest("Live is not started yet.");

        List<string> segments = [];
        await foreach (string segment in persistentService.ListSegmentsAsync(id)) {
            segments.Add(segment);
        }
        return segments;
    }

    [HttpGet("{id}/segments/{num:int}")]
    [HttpGet("{id}/segments/segment_{num:int}.ts")]
    public async Task GetLiveSegment(string id, int num) {
        if (await db.Lives.FindAsync(id) is not {} live) {
            Response.StatusCode = 404;
            return;
        }
        if (live.Status is LiveStatus.Created or LiveStatus.Starting) {
            Response.StatusCode = 400;
            await Response.WriteAsync("Live is not started yet.");
            return;
        }

        var statRet = await persistentService.GetSegmentStatAsync(id, num);
        if (statRet.IsError) {
            Response.StatusCode = 404;
            await Response.WriteAsJsonAsync(statRet.Errors);
            return;
        }

        ObjectStat stat = statRet.Value;

        Response.ContentType          = "video/MP2T";
        Response.ContentLength        = stat.Size;
        Response.Headers.AcceptRanges = "bytes";
        Response.Headers.ETag         = stat.ETag;
        Response.Headers.LastModified = stat.LastModified.ToString("R");
        Response.Headers.CacheControl = "public, max-age=600";// Cache for 10 mins

        var ret = await persistentService.GetSegmentAsync(id, num, Response.Body);
        if (ret.IsError) {
            Response.StatusCode = 404;
            await Response.WriteAsJsonAsync(ret.Errors);
        }
    }

    #endregion

}
