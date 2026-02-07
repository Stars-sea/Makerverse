using System.ComponentModel.DataAnnotations;

namespace ActivityService.Models;

public class Activity {
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(36)]
    public required string PublisherId { get; set; }

    [MaxLength(36)]
    public required string? LinkedLiveId { get; set; }

    [MaxLength(300)]
    public required string Title { get; set; }

    [MaxLength(2000)]
    public required string Content { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // TODO: public List<string> ImageUrls { get; set; } = [];

    public ulong Votes { get; set; } = 0;
    public ulong ViewCount { get; set; } = 0;

    public List<string> TagSlugs { get; set; } = [];

    public List<Comment> Comments { get; set; } = [];
}
