using Contracts;
using SearchService.Data;
using Typesense;

namespace SearchService.MessageHandlers;

public class ActivityUpdatedHandler(
    ITypesenseClient client
) {
    public async Task HandleAsync(ActivityUpdated message) {
        await client.UpdateDocument(
            SearchInitializer.ActivityCollectionName,
            message.ActivityId,
            new {
                message.Title,
                message.Content,
                message.Tags
            }
        );
    }
}
