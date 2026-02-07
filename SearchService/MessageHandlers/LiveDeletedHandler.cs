using Contracts;
using SearchService.Models;
using Typesense;

namespace SearchService.MessageHandlers;

public class LiveDeletedHandler(
    ITypesenseClient client
) {
    public async Task HandleAsync(LiveDeleted message) {
        await client.DeleteDocument<SearchLive>("lives", message.LiveId);
    }
}
