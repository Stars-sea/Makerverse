using Typesense;

namespace SearchService.Data;

public static class SearchInitializer {
    public static async Task EnsureIndexExistsAsync(ITypesenseClient client) {
        const string schemaName = "lives";

        try {
            await client.RetrieveCollection(schemaName);
            Console.WriteLine($"Collection '{schemaName}' already exists.");
            return;
        }
        catch (TypesenseApiNotFoundException) {
            Console.WriteLine($"Collection '{schemaName}' does not exist.");
        }

        Schema schema = new(schemaName,
        [
            new Field("id", FieldType.String),
            new Field("title", FieldType.String),
            new Field("createdAt", FieldType.Int64)
        ]) {
            DefaultSortingField = "createdAt"
        };

        await client.CreateCollection(schema);
        Console.WriteLine($"Collection '{schemaName}' created.");
    }
}
