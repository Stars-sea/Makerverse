using Contracts;
using SearchService.Data;
using Typesense;

namespace SearchService.MessageHandlers;

public class LiveUpdatedHandler(
    ITypesenseClient client
) {
    public async Task HandleAsync(LiveUpdated message) {
        await client.UpdateDocument(
            SearchInitializer.LiveCollectionName,
            message.LiveId,
            new {
                message.Title
            }
        );
    }
}
