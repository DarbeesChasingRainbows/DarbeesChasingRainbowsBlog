using System.Net.Http;
using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;

namespace Darbee.Gateway.Tests.Memory;

[Trait("Category", "Integration")]
public class MemoryStoreEmbeddingConfigTests
{
    private static string ArangoUrl => MemoryStoreSchemaTests.ArangoUrl;
    private static string ArangoUser => MemoryStoreSchemaTests.ArangoUser;
    private static string ArangoPass => MemoryStoreSchemaTests.ArangoPass;
    private static bool ArangoEnabled => MemoryStoreSchemaTests.ArangoEnabled;

    [Fact]
    public async Task EnsureSchemaAsync_FirstRun_WritesEmbeddingConfigSentinel()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "qwen3-embedding-8b", embeddingDimension: 4096, vectorNLists: 100, http);

            await store.EnsureSchemaAsync();

            var config = await store.ReadEmbeddingConfigAsync();
            Assert.NotNull(config);
            Assert.Equal(4096, config!.Dimension);
            Assert.Equal("qwen3-embedding-8b", config.Model);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task EnsureSchemaAsync_MismatchedConfig_ThrowsEmbeddingConfigMismatchException()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var first = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "nomic-embed-text-v1.5", embeddingDimension: 768, vectorNLists: 1, http);
            await first.EnsureSchemaAsync();

            var second = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "qwen3-embedding-8b", embeddingDimension: 4096, vectorNLists: 1, http);

            var ex = await Assert.ThrowsAsync<EmbeddingConfigMismatchException>(() => second.EnsureSchemaAsync());
            Assert.Equal("nomic-embed-text-v1.5", ex.Previous.Model);
            Assert.Equal(768, ex.Previous.Dimension);
            Assert.Equal("qwen3-embedding-8b", ex.Current.Model);
            Assert.Equal(4096, ex.Current.Dimension);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task EnsureSchemaAsync_MatchingConfig_DoesNotThrow_AndIsIdempotent()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "qwen3-embedding-8b", embeddingDimension: 4096, vectorNLists: 100, http);

            await store.EnsureSchemaAsync();
            await store.EnsureSchemaAsync();  // second call — must not throw
            await store.EnsureSchemaAsync();  // third call — still no throw

            var config = await store.ReadEmbeddingConfigAsync();
            Assert.NotNull(config);
            Assert.Equal("qwen3-embedding-8b", config!.Model);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task UpsertFactAsync_OnFreshStore_TriggersSchemaBootstrap()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "qwen3-embedding-8b", embeddingDimension: 4096, vectorNLists: 100, http);

            // Do NOT call EnsureSchemaAsync explicitly — UpsertFactAsync should trigger it.
            await store.UpsertFactAsync("tenant-a", "the sky is blue", sourceThread: null);

            // Verify the schema was actually bootstrapped: read the config sentinel.
            var config = await store.ReadEmbeddingConfigAsync();
            Assert.NotNull(config);
            Assert.Equal("qwen3-embedding-8b", config!.Model);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task EnsureSchemaIfNeededAsync_AfterMismatch_CachesException_DoesNotRecheckArango()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();

            // Seed at old config
            var oldStore = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "nomic-embed-text-v1.5", embeddingDimension: 768, vectorNLists: 1, http);
            await oldStore.EnsureSchemaAsync();

            // Open new store at mismatched config
            var newStore = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "qwen3-embedding-8b", embeddingDimension: 4096, vectorNLists: 1, http);

            // First call: throws mismatch
            var ex1 = await Assert.ThrowsAsync<EmbeddingConfigMismatchException>(
                () => newStore.EnsureSchemaIfNeededAsync());
            // Second call: throws the SAME exception instance (cached)
            var ex2 = await Assert.ThrowsAsync<EmbeddingConfigMismatchException>(
                () => newStore.EnsureSchemaIfNeededAsync());
            // Third call: still cached
            var ex3 = await Assert.ThrowsAsync<EmbeddingConfigMismatchException>(
                () => newStore.EnsureSchemaIfNeededAsync());

            Assert.Same(ex1, ex2);
            Assert.Same(ex2, ex3);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }
}
