using Contracts;
using SearchService.Data;
using SearchService.Models;
using Typesense;

namespace SearchService.MessageHandlers;

public class LiveCreatedHandler(
    ITypesenseClient client
) {
    public async Task HandleAsync(LiveCreated message) {
        long createdAt = new DateTimeOffset(message.CreatedAt).ToUnixTimeSeconds();

        SearchLive doc = new() {
            Id        = message.LiveId,
            Title     = message.Title,
            CreatedAt = createdAt,
        };
        await client.CreateDocument(SearchInitializer.LiveCollectionName, doc);
    }
}
