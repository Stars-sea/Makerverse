using System.Security.Claims;
using Common;
using Contracts;
using LiveService.Data;
using LiveService.DTOs;
using LiveService.Models;
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
    IMessageBus bus
) : ControllerBase {

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
            .OrderByDescending(x => x.CreatedAt).ToListAsync();
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

    [Authorize]
    [HttpPut("{id}/status")]
    public async Task<ActionResult<LiveStatusResponseDto>> UpdateLiveStatus(string id, UpdateLiveStatusDto dto) {
        if (await db.Lives.FindAsync(id) is not {} live) return NotFound();

        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null || live.StreamerId != userId) return Forbid();

        if (dto.Status == "start") {
            var ret = await livestreamService.StartLivestreamAsync(live.Id);
            return ret.MatchFirst(
                resp => Ok(LiveStatusResponseDto.FromResp(resp)),
                error => error.ToActionResult()
            );
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
    [HttpGet("{id}/status")]
    public async Task<ActionResult<LiveStatusResponseDto>> GetLiveStatus(string id) {
        if (await db.Lives.FindAsync(id) is not {} live) return NotFound();

        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null || live.StreamerId != userId) return Forbid();

        var ret = await livestreamService.GetStreamInfoAsync(live.Id);
        return ret.MatchFirst(
            resp => Ok(LiveStatusResponseDto.FromResp(resp)),
            error => error.ToActionResult()
        );
    }

    [HttpGet]
    public async Task<ActionResult<string[]>> ListOnlineLives() {
        var ret = await livestreamService.GetActiveStreamAsync();
        return ret.MatchFirst(
            streamIds => Ok(streamIds.ToArray()),
            error => error.ToActionResult()
        );
    }
}
