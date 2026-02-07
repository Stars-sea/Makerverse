using Contracts;
using SearchService.Data;
using SearchService.Models;
using Typesense;

namespace SearchService.MessageHandlers;

public class ActivityDeletedHandler(
    ITypesenseClient client
) {
    public async Task HandleAsync(ActivityDeleted message) {
        await client.DeleteDocument<SearchActivity>(SearchInitializer.ActivityCollectionName, message.ActivityId);
    }
}
