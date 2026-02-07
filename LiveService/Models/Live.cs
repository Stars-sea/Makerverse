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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? StoppedAt { get; set; }
}
