using System.Net.Http;
using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;

namespace Darbee.Gateway.Tests.Memory;

[Trait("Category", "Integration")]
public class MemoryStoreMigrationTests
{
    private static string ArangoUrl => MemoryStoreSchemaTests.ArangoUrl;
    private static string ArangoUser => MemoryStoreSchemaTests.ArangoUser;
    private static string ArangoPass => MemoryStoreSchemaTests.ArangoPass;
    private static bool ArangoEnabled => MemoryStoreSchemaTests.ArangoEnabled;

    [Fact]
    public async Task MigrateEmbeddingsAsync_NoMismatch_IsNoop()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "qwen3-embedding-8b", embeddingDimension: 4096, vectorNLists: 100, http);

            await store.EnsureSchemaAsync();

            var result = await store.MigrateEmbeddingsAsync("preserve-and-reembed");

            Assert.NotNull(result.Previous);
            Assert.Equal(result.Previous, result.Current);
            Assert.Empty(result.IndexesDropped);
            Assert.Equal(0, result.QueueSizeAfter);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task MigrateEmbeddingsAsync_RejectsInvalidConfirm()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "qwen3-embedding-8b", embeddingDimension: 4096, vectorNLists: 100, http);
            await store.EnsureSchemaAsync();

            await Assert.ThrowsAsync<ArgumentException>(() => store.MigrateEmbeddingsAsync("nope"));
            await Assert.ThrowsAsync<ArgumentException>(() => store.MigrateEmbeddingsAsync(""));
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }
}
