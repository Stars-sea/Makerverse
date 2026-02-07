using System.Text.Json.Serialization;

namespace SearchService.Models;

public class SearchLive {
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("title")]
    public required string Title { get; set; }

    [JsonPropertyName("createdAt")]
    public long CreatedAt { get; set; }
}
