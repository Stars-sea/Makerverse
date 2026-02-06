using System.Text.Json.Serialization;

namespace SearchService.Models;

public class SearchLive {
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("title")]
    public required string Title { get; set; }

    [JsonPropertyName("tags")]
    public string[] Tags { get; set; } = [];

    [JsonPropertyName("createdAt")]
    public long CreatedAt { get; set; }

    [JsonPropertyName("startedAt")]
    public long? StartedAt { get; set; }
}
