using System.ComponentModel.DataAnnotations;

namespace LiveService.Models;

public class Live {
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(300)]
    public required string Title { get; set; }

    [MaxLength(36)]
    public required string StreamerId { get; set; }

    public LiveStatus Status { get; set; } = LiveStatus.Created;

    public List<string> TagSlugs { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
}
