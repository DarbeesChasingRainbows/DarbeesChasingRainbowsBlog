using System.Net.Http;
using Darbee.Gateway.Endpoints;
using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;
using Darbee.Gateway.Tests.Memory;

namespace Darbee.Gateway.Tests.Endpoints;

[Trait("Category", "Integration")]
public class ContentRagEndpointsTests
{
    private static string ArangoUrl => MemoryStoreSchemaTests.ArangoUrl;
    private static bool ArangoEnabled => MemoryStoreSchemaTests.ArangoEnabled;

    internal static ReindexPost MakeReindexPost(string slug = "welcome") =>
        new ReindexPost(
            Collection: "blog",
            Slug: slug,
            Frontmatter: new ReindexFrontmatter(
                Title: "Welcome",
                Description: "intro",
                PubDate: "2026-04-29",
                Category: "Faith",
                Tags: new[] { "family" },
                AiSummary: "summary",
                KeyTakeaways: new[] { "one" },
                Faq: null,
                EntityMentions: null),
            Body: "Hello from the road.");

    [Fact]
    public async Task HandleReindexAsync_ColdStart_WritesTwoVectorsPerPost()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStorePostsTests.StubEmbeddingClient();
            var store = new MemoryStore(ArangoUrl, dbName,
                MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

            var request = new ReindexRequest(
                Force: false,
                Posts: new[] { MakeReindexPost("one"), MakeReindexPost("two") });

            var response = await ContentRagEndpoints.HandleReindexAsync(request, store, emb);

            Assert.Equal(2, response.Scanned);
            Assert.Equal(4, response.Embedded);  // 2 posts × 2 vectors
            Assert.Equal(0, response.FromCache);
            Assert.Equal(0, response.DeletedStale);
            Assert.Equal(2, response.Posts.Count);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task HandleReindexAsync_SameRequestTwice_SecondIsAllCacheHits()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStorePostsTests.StubEmbeddingClient();
            var store = new MemoryStore(ArangoUrl, dbName,
                MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

            var request = new ReindexRequest(false, new[] { MakeReindexPost("one") });
            await ContentRagEndpoints.HandleReindexAsync(request, store, emb);
            var second = await ContentRagEndpoints.HandleReindexAsync(request, store, emb);

            Assert.Equal(0, second.Embedded);
            Assert.Equal(2, second.FromCache);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task HandleReindexAsync_RemovedPost_DeletesStaleDocs()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStorePostsTests.StubEmbeddingClient();
            var store = new MemoryStore(ArangoUrl, dbName,
                MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

            var first = new ReindexRequest(false, new[] {
                MakeReindexPost("keep-this"),
                MakeReindexPost("delete-this"),
            });
            await ContentRagEndpoints.HandleReindexAsync(first, store, emb);

            // Second call drops "delete-this"
            var second = new ReindexRequest(false, new[] { MakeReindexPost("keep-this") });
            var result = await ContentRagEndpoints.HandleReindexAsync(second, store, emb);

            Assert.Equal(2, result.DeletedStale);  // summary + body of delete-this
            Assert.Null(await store.ReadPostDocumentAsync("blog__delete-this__summary"));
            Assert.NotNull(await store.ReadPostDocumentAsync("blog__keep-this__summary"));
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task HandleReindexAsync_DuplicateSlugInPayload_Throws()
    {
        var request = new ReindexRequest(false, new[] {
            MakeReindexPost("dup"),
            MakeReindexPost("dup"),
        });

        // No real Arango call — validation runs before any store call.
        using var http = new HttpClient();
        var emb = new MemoryStorePostsTests.StubEmbeddingClient();
        var store = new MemoryStore("http://unused:8529", "unused", "u", "p",
            "m", 4, 1, http, emb);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            ContentRagEndpoints.HandleReindexAsync(request, store, emb));
    }
}
