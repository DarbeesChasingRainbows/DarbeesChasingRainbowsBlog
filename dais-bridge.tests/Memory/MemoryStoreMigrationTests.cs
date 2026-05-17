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

    [Fact]
    public async Task MigrateEmbeddingsAsync_PreserveAndReembed_ClearsEmbedding_EnqueuesPending()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStorePostsTests.StubEmbeddingClient();

            // Seed at old config
            var oldStore = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "nomic-embed-text-v1.5", embeddingDimension: 4, vectorNLists: 1, http, emb);
            await oldStore.UpsertPostAsync(MemoryStorePostsTests.MakePost("one"), force: false);

            // Pre-migration: doc is ready with embedding
            var pre = await oldStore.ReadPostDocumentAsync("blog__one__summary");
            Assert.NotNull(pre);
            Assert.Equal("ready", pre!.RootElement.GetProperty("status").GetString());

            // Now open a NEW store at NEW config and run migration
            var newStore = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "qwen3-embedding-8b", embeddingDimension: 4096, vectorNLists: 100, http, emb);

            var result = await newStore.MigrateEmbeddingsAsync("preserve-and-reembed");

            Assert.NotNull(result.Previous);
            Assert.Equal("nomic-embed-text-v1.5", result.Previous!.Model);
            Assert.Equal("qwen3-embedding-8b", result.Current.Model);
            Assert.True(result.DocsMarkedForReembed[MemoryCollections.Posts] >= 2,
                $"expected ≥2 posts marked, got {result.DocsMarkedForReembed[MemoryCollections.Posts]}");
            Assert.True(result.QueueSizeAfter >= 2);

            // Post-migration: doc has null embedding, pending status, text intact
            var post = await newStore.ReadPostDocumentAsync("blog__one__summary");
            Assert.NotNull(post);
            Assert.Equal("pending_embedding", post!.RootElement.GetProperty("status").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, post.RootElement.GetProperty("embedding").ValueKind);
            Assert.Equal("Welcome", post.RootElement.GetProperty("title").GetString());
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }
}
