using Contracts;
using SearchService.Models;
using Typesense;

namespace SearchService.MessageHandlers;

public class LiveCreatedHandler(
    ITypesenseClient client
) {
    public async Task HandleAsync(LiveCreated message) {
        long created = new DateTimeOffset(message.Created).ToUnixTimeSeconds();

        SearchLive doc = new() {
            Id        = message.LiveId,
            Title     = message.Title,
            CreatedAt = created,
        };
        await client.CreateDocument("lives", doc);
    }
}
