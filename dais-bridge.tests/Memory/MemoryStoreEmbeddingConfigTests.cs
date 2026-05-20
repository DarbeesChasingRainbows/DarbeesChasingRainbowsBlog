using System.Net.Http;
using Darbee.Gateway.Infrastructure.Arango;
using Darbee.Gateway.Infrastructure.Embedding;
using Darbee.Gateway.Domain.Exceptions;
using Darbee.Gateway.Domain.Models;
using Darbee.Gateway.Domain.ValueObjects;

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
            var store = new ArangoMemoryRepository(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "qwen3-embedding-8b", embeddingDimension: 4096, vectorNLists: 100, http, new StubDomainEventDispatcher());

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
            var first = new ArangoMemoryRepository(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "nomic-embed-text-v1.5", embeddingDimension: 768, vectorNLists: 1, http, new StubDomainEventDispatcher());
            await first.EnsureSchemaAsync();

            var second = new ArangoMemoryRepository(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "qwen3-embedding-8b", embeddingDimension: 4096, vectorNLists: 1, http, new StubDomainEventDispatcher());

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
            var store = new ArangoMemoryRepository(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "qwen3-embedding-8b", embeddingDimension: 4096, vectorNLists: 100, http, new StubDomainEventDispatcher());

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
            var store = new ArangoMemoryRepository(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "qwen3-embedding-8b", embeddingDimension: 4096, vectorNLists: 100, http, new StubDomainEventDispatcher());

            // Do NOT call EnsureSchemaAsync explicitly — UpsertFactAsync should trigger it.
            await store.UpsertFactAsync(new TenantId("tenant-a"), "the sky is blue", sourceThread: null);

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
            var oldStore = new ArangoMemoryRepository(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "nomic-embed-text-v1.5", embeddingDimension: 768, vectorNLists: 1, http, new StubDomainEventDispatcher());
            await oldStore.EnsureSchemaAsync();

            // Open new store at mismatched config
            var newStore = new ArangoMemoryRepository(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "qwen3-embedding-8b", embeddingDimension: 4096, vectorNLists: 1, http, new StubDomainEventDispatcher());

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
