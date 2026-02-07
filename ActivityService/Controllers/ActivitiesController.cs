using System.Security.Claims;
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
public class ActivitiesController(
    TagService tagService,
    ActivityDbContext db
) : ControllerBase {

    #region Activity Region

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<Activity>> CreateActivity(CreateActivityDto dto) {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return BadRequest("Cannot get user details.");

        if (!await tagService.AreTagsValidAsync(dto.Tags))
            return BadRequest("One or more tags are invalid.");

        Activity activity = new() {
            PublisherId  = userId,
            Title        = dto.Title,
            Content      = dto.Content,
            TagSlugs     = dto.Tags,
            LinkedLiveId = dto.LinkedLiveId,
        };
        db.Activities.Add(activity);
        await db.SaveChangesAsync();

        return CreatedAtAction(
            nameof(GetActivity),
            new {
                id = activity.Id
            },
            activity
        );
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Activity>> GetActivity(string id) {
        Activity? activity = await db.Activities.FindAsync(id);
        if (activity is null) return NotFound();

        await db.Activities.Where(x => x.Id == activity.Id)
            .ExecuteUpdateAsync(x =>
                x.SetProperty(a => a.ViewCount, a => a.ViewCount + 1)
            );

        return activity;
    }

    [Authorize]
    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateActivity(string id, CreateActivityDto dto) {
        Activity? activity = await db.Activities.FindAsync(id);
        if (activity is null) return NotFound();

        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null || activity.PublisherId != userId) return Forbid();

        if (!await tagService.AreTagsValidAsync(dto.Tags))
            return BadRequest("One or more tags are invalid.");

        activity.Title        = dto.Title;
        activity.Content      = dto.Content;
        activity.TagSlugs     = dto.Tags;
        activity.LinkedLiveId = dto.LinkedLiveId;
        activity.UpdatedAt    = DateTime.UtcNow;

        await db.SaveChangesAsync();

        return NoContent();
    }

    [Authorize]
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteActivity(string id) {
        Activity? activity = await db.Activities.FindAsync(id);
        if (activity is null) return NotFound();

        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null || activity.PublisherId != userId) return Forbid();

        db.Activities.Remove(activity);
        await db.SaveChangesAsync();

        return NoContent();
    }

    #endregion

    #region Comment Region

    [Authorize]
    [HttpPost("{activityId}/comments")]
    public async Task<ActionResult<Comment>> CreateComment(string activityId, CreateCommentDto dto) {
        Activity? activity = await db.Activities.FindAsync(activityId);
        if (activity is null) return NotFound();

        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return BadRequest("Cannot get user details.");

        Comment comment = new() {
            ActivityId  = activity.Id,
            PublisherId = userId,
            Content     = dto.Content
        };
        db.Comments.Add(comment);
        await db.SaveChangesAsync();

        return CreatedAtAction(
            nameof(GetComment),
            new {
                activityId = activity.Id,
                commentId  = comment.Id
            },
            comment
        );
    }

    [HttpGet("{activityId}/comments/{commentId}")]
    public async Task<ActionResult<Comment>> GetComment(string activityId, string commentId) {
        Comment? comment = await db.Comments.FindAsync(commentId);
        if (comment is null || comment.ActivityId != activityId) return NotFound();
        return comment;
    }

    [Authorize]
    [HttpPut("{activityId}/comments/{commentId}")]
    public async Task<ActionResult> UpdateComment(string activityId, string commentId, CreateCommentDto dto) {
        Comment? comment = await db.Comments.FindAsync(commentId);
        if (comment is null || comment.ActivityId != activityId) return NotFound();

        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null || comment.PublisherId != userId) return Forbid();

        comment.Content   = dto.Content;
        comment.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        return NoContent();
    }

    [Authorize]
    [HttpDelete("{activityId}/comments/{commentId}")]
    public async Task<ActionResult> DeleteComment(string activityId, string commentId) {
        Comment? comment = await db.Comments.FindAsync(commentId);
        if (comment is null || comment.ActivityId != activityId) return NotFound();

        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null || comment.PublisherId != userId) return Forbid();

        db.Comments.Remove(comment);
        await db.SaveChangesAsync();

        return NoContent();
    }

    #endregion

}
