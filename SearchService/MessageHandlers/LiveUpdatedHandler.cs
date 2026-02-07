using Contracts;
using Typesense;

namespace SearchService.MessageHandlers;

public class LiveUpdatedHandler(
    ITypesenseClient client
) {
    public async Task HandleAsync(LiveUpdated message) {
        long created = new DateTimeOffset(message.Created).ToUnixTimeSeconds();

        await client.UpdateDocument(
            "lives",
            message.LiveId,
            new {
                message.Title,
                CreatedAt = created,
            }
        );
    }
}
