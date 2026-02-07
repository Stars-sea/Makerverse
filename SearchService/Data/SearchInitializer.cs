using Typesense;

namespace SearchService.Data;

public static class SearchInitializer {
    
    public const string ActivityCollectionName = "activities";
    public const string LiveCollectionName     = "lives";
    
    public static async Task EnsureIndexesExistsAsync(ITypesenseClient client) {
        await Task.WhenAll(
            EnsureLiveIndexExistsAsync(client),
            EnsureActivityIndexExistsAsync(client)
        );
    }

    private static async Task EnsureActivityIndexExistsAsync(ITypesenseClient client) {
        try {
            await client.RetrieveCollection(ActivityCollectionName);
            Console.WriteLine($"Collection '{ActivityCollectionName}' already exists.");
            return;
        }
        catch (TypesenseApiNotFoundException) {
            Console.WriteLine($"Collection '{ActivityCollectionName}' does not exist.");
        }

        Schema schema = new(ActivityCollectionName,
        [
            new Field("id", FieldType.String),
            new Field("title", FieldType.String),
            new Field("content", FieldType.String),
            new Field("tags", FieldType.StringArray),
            new Field("createdAt", FieldType.Int64)
        ]) {
            DefaultSortingField = "createdAt"
        };

        await client.CreateCollection(schema);
        Console.WriteLine($"Collection '{ActivityCollectionName}' created.");
    }

    private static async Task EnsureLiveIndexExistsAsync(ITypesenseClient client) {
        try {
            await client.RetrieveCollection(LiveCollectionName);
            Console.WriteLine($"Collection '{LiveCollectionName}' already exists.");
            return;
        }
        catch (TypesenseApiNotFoundException) {
            Console.WriteLine($"Collection '{LiveCollectionName}' does not exist.");
        }

        Schema schema = new(LiveCollectionName,
        [
            new Field("id", FieldType.String),
            new Field("title", FieldType.String),
            new Field("createdAt", FieldType.Int64)
        ]) {
            DefaultSortingField = "createdAt"
        };

        await client.CreateCollection(schema);
        Console.WriteLine($"Collection '{LiveCollectionName}' created.");
    }
}
