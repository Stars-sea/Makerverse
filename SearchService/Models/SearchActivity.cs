using System.Text.Json.Serialization;

namespace SearchService.Models;

public class SearchActivity {
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("title")]
    public required string Title { get; set; }

    [JsonPropertyName("content")]
    public required string Content { get; set; }

    [JsonPropertyName("tags")]
    public required string[] Tags { get; set; }

    [JsonPropertyName("createdAt")]
    public long CreatedAt { get; set; }
}
