using Contracts;
using SearchService.Data;
using SearchService.Models;
using Typesense;

namespace SearchService.MessageHandlers;

public class LiveDeletedHandler(
    ITypesenseClient client
) {
    public async Task HandleAsync(LiveDeleted message) {
        await client.DeleteDocument<SearchLive>(SearchInitializer.LiveCollectionName, message.LiveId);
    }
}
