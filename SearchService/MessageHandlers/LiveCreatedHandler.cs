using Contracts;
using SearchService.Models;
using Typesense;

namespace SearchService.MessageHandlers;

public class LiveCreatedHandler(
    ITypesenseClient client
) {
    public async Task HandleAsync(LiveCreated message) {
        long created = new DateTimeOffset(message.Created).ToUnixTimeSeconds();
        long? stared = message.Started switch {
            {} time => new DateTimeOffset(time).ToUnixTimeSeconds(),
            _       => null
        };

        SearchLive doc = new() {
            Id        = message.LiveId,
            Title     = message.Title,
            Tags      = message.Tags.ToArray(),
            CreatedAt = created,
            StartedAt = stared,
        };
        await client.CreateDocument("lives", doc);
    }
}
