using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ActivityService.Models;

public class Comment {
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(36)]
    public required string ActivityId { get; set; }

    [MaxLength(36)]
    public required string PublisherId { get; set; }

    [MaxLength(500)]
    public required string Content { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ulong Votes { get; set; } = 0;

    [JsonIgnore]
    public Activity Activity { get; set; } = null!;
}
