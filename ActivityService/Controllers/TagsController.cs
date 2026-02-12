using ActivityService.Data;
using ActivityService.DTOs;
using ActivityService.Models;
using ActivityService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Controllers;

[ApiController]
[Route("[controller]")]
public class TagsController(
    ActivityDbContext db,
    TagService tagService
) : ControllerBase {

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Tag>>> GetTags() {
        return await db.Tags.OrderBy(x => x.Name).ToListAsync();
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<Tag>> GetTag(string slug) {
        Tag? tag = await db.Tags.FirstOrDefaultAsync(t => t.Slug == slug);
        if (tag is null) {
            return NotFound($"Tag with slug '{slug}' not found.");
        }
        return tag;
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<Tag>> CreateTag([FromBody] CreateTagDto dto) {
        if (await db.Tags.AnyAsync(t => t.Slug == dto.Slug)) {
            return Conflict($"Tag with slug '{dto.Slug}' already exists.");
        }

        Tag tag = new() {
            Name        = dto.Name,
            Slug        = dto.Slug,
            Description = dto.Description
        };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();

        await tagService.InvalidateCacheAsync();
        return CreatedAtAction(
            nameof(GetTag),
            new {
                slug = tag.Slug
            },
            tag
        );
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{slug}")]
    public async Task<ActionResult> DeleteTag(string slug) {
        Tag? tag = await db.Tags.FirstOrDefaultAsync(t => t.Slug == slug);
        if (tag is null) {
            return NotFound($"Tag with slug '{slug}' not found.");
        }

        db.Tags.Remove(tag);
        await db.SaveChangesAsync();

        await tagService.InvalidateCacheAsync();

        return NoContent();
    }

}
