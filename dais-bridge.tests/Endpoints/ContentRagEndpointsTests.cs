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
}
