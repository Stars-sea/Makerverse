using Contracts;
using SearchService.Data;
using SearchService.Models;
using Typesense;

namespace SearchService.MessageHandlers;

public class ActivityCreatedHandler(
    ITypesenseClient client
) {
    public async Task HandleAsync(ActivityCreated message) {
        long createdAt = new DateTimeOffset(message.CreatedAt).ToUnixTimeSeconds();

        SearchActivity activity = new() {
            Id        = message.ActivityId,
            Title     = message.Title,
            Content   = message.Content,
            Tags      = message.Tags,
            CreatedAt = createdAt
        };

        await client.CreateDocument(SearchInitializer.ActivityCollectionName, activity);
    }
}
