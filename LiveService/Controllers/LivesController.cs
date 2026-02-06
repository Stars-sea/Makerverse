using System.Security.Claims;
using Contracts;
using LiveService.Data;
using LiveService.DTOs;
using LiveService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace LiveService.Controllers;

[ApiController]
[Route("[controller]")]
public class LivesController(
    LiveDbContext db,
    IMessageBus bus
) : ControllerBase {

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<Live>> CreateLive(CreateLiveDto dto) {
        var validTags = await db.Tags.Where(x => dto.Tags.Contains(x.Slug)).ToListAsync();
        var missing   = dto.Tags.Except(validTags.Select(x => x.Slug)).ToList();
        if (missing.Count != 0) {
            return BadRequest($"Invalid tags: {string.Join(", ", missing)}");
        }

        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return BadRequest("Cannot get user details.");

        Live live = new() {
            Title      = dto.Title,
            StreamerId = userId,
            TagSlugs   = dto.Tags
        };
        db.Lives.Add(live);
        await db.SaveChangesAsync();

        await bus.PublishAsync(new LiveCreated(
            live.Id,
            live.Title,
            live.TagSlugs,
            live.CreatedAt,
            live.StartTime
        ));

        return CreatedAtAction(
            "GetLive",
            new {
                id = live.Id
            },
            live
        );
    }

    [HttpGet]
    public async Task<ActionResult<List<Live>>> GetLives(string? title, string? tag) {
        var query = db.Lives.AsQueryable();

        if (!string.IsNullOrEmpty(title)) {
            query = query.Where(x => x.Title.Contains(title));
        }

        if (!string.IsNullOrEmpty(tag)) {
            query = query.Where(x => x.TagSlugs.Contains(tag));
        }

        return await query.OrderByDescending(x => x.CreatedAt).ToListAsync();
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
        Live? live = await db.Lives.FindAsync(id);
        if (live is null) return NotFound();

        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null || live.StreamerId != userId) return Forbid();

        var validTags = await db.Tags.Where(x => dto.Tags.Contains(x.Slug)).ToListAsync();
        var missing   = dto.Tags.Except(validTags.Select(x => x.Slug)).ToList();
        if (missing.Count != 0) {
            return BadRequest($"Invalid tags: {string.Join(", ", missing)}");
        }

        live.TagSlugs = dto.Tags;
        live.Title    = dto.Title;

        await db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize]
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteLive(string id) {
        Live? live = await db.Lives.FindAsync(id);
        if (live is null) return NotFound();

        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null || live.StreamerId != userId) return Forbid();

        db.Lives.Remove(live);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
