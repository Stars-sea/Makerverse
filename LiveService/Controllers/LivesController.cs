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
using Wolverine;

namespace LiveService.Controllers;

[ApiController]
[Route("[controller]")]
public class LivesController(
    LiveDbContext db,
    LivestreamService livestreamService,
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

        await bus.PublishAsync(new LiveCreated(live.Id, live.Title, live.CreatedAt, userId));

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
    public async Task<ActionResult<List<Live>>> GetMyLives() {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return BadRequest("Cannot get user details.");

        return await GetLivesByStreamer(userId);
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

            StreamDescriptor descriptor = ret.Value.Descriptor_;

            PlaybackEndpointDto playbackEndpoints = descriptorConverter.BuildPlaybackUri(descriptor);
            string              ingestEndpoint    = descriptorConverter.BuildIngestUri(descriptor);

            return new LivestreamEndpointDto(ingestEndpoint, playbackEndpoints);
        }

        if (dto.Status == "stop") {
            var ret = await livestreamService.StopLivestreamAsync(live.Id);
            if (ret is {} err)
                return err.ToActionResult();
        }

        return BadRequest($"Invalid status value '{dto.Status}'.");
    }

    [HttpGet("{id}/endpoint")]
    public async Task<ActionResult<LivestreamEndpointDto>> GetLiveEndpoint(string id) {
        if (await db.Lives.FindAsync(id) is not {} live) return NotFound();

        string? userId  = User.FindFirstValue(ClaimTypes.NameIdentifier);
        bool    isOwner = live.StreamerId == userId;

        if (live.Status is not (LiveStatus.Starting or LiveStatus.Started))
            return NotFound();

        var ret = await livestreamService.GetStreamInfoAsync(live.Id);
        if (ret.IsError)// TODO: Add telemetry of the error for debugging
            return ret.FirstError.ToActionResult();

        StreamDescriptor descriptor = ret.Value.Descriptor_;

        PlaybackEndpointDto pullUrl = descriptorConverter.BuildPlaybackUri(descriptor);
        string?             pushUrl = isOwner ? descriptorConverter.BuildIngestUri(descriptor) : null;
        return new LivestreamEndpointDto(pushUrl, pullUrl);
    }

    #endregion

}
