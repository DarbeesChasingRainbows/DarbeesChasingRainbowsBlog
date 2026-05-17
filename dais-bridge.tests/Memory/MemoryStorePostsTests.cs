using System.Net.Http;
using System.Text.Json;
using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;

namespace Darbee.Gateway.Tests.Memory;

[Trait("Category", "Integration")]
public class MemoryStorePostsTests
{
    private static string ArangoUrl => MemoryStoreSchemaTests.ArangoUrl;
    private static string ArangoUser => MemoryStoreSchemaTests.ArangoUser;
    private static string ArangoPass => MemoryStoreSchemaTests.ArangoPass;
    private static bool ArangoEnabled => MemoryStoreSchemaTests.ArangoEnabled;

    internal sealed class StubEmbeddingClient : IEmbeddingClient
    {
        public int Dimension { get; set; } = 4;
        public int EmbedCalls { get; private set; }
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            EmbedCalls++;
            return Task.FromResult(new[] { 0.1f, 0.2f, 0.3f, 0.4f });
        }
        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            EmbedCalls += texts.Count;
            return Task.FromResult<IReadOnlyList<float[]>>(
                texts.Select(_ => new[] { 0.1f, 0.2f, 0.3f, 0.4f }).ToArray());
        }
    }

    internal static PostDocument MakePost(string slug = "welcome", string collection = "blog") =>
        new PostDocument(
            Collection: collection,
            Slug: slug,
            Title: "Welcome",
            Description: "An intro post.",
            Body: "Hello from the road.",
            AiSummary: "Intro summary",
            KeyTakeaways: new[] { "One" },
            Faq: Array.Empty<FaqEntry>(),
            EntityMentions: Array.Empty<string>(),
            Tags: new[] { "family" },
            Category: "Faith",
            PubDate: "2026-04-29");

    [Fact]
    public async Task UpsertPostAsync_FreshPost_WritesTwoDocsSummaryAndBody()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new StubEmbeddingClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

            var result = await store.UpsertPostAsync(MakePost(), force: false);

            Assert.Equal("welcome", result.Slug);
            Assert.Equal("blog", result.Collection);
            Assert.Equal(VectorWriteOutcome.Embedded, result.Summary);
            Assert.Equal(VectorWriteOutcome.Embedded, result.Body);
            Assert.Equal(2, emb.EmbedCalls);

            var summaryDoc = await store.ReadPostDocumentAsync("blog__welcome__summary");
            var bodyDoc = await store.ReadPostDocumentAsync("blog__welcome__body");
            Assert.NotNull(summaryDoc);
            Assert.NotNull(bodyDoc);
            Assert.Equal("summary", summaryDoc!.RootElement.GetProperty("vector_kind").GetString());
            Assert.Equal("body", bodyDoc!.RootElement.GetProperty("vector_kind").GetString());
            Assert.Equal("public", summaryDoc.RootElement.GetProperty("tenant_id").GetString());
            Assert.Equal("ready", summaryDoc.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task UpsertPostAsync_SamePostTwice_SecondCallIsAllCacheHits()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new StubEmbeddingClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

            await store.UpsertPostAsync(MakePost(), force: false);
            var callsAfterFirst = emb.EmbedCalls;
            var result2 = await store.UpsertPostAsync(MakePost(), force: false);

            Assert.Equal(VectorWriteOutcome.Cached, result2.Summary);
            Assert.Equal(VectorWriteOutcome.Cached, result2.Body);
            Assert.Equal(callsAfterFirst, emb.EmbedCalls);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task UpsertPostAsync_ForceTrue_ReembedsEvenOnHashMatch()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new StubEmbeddingClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

            await store.UpsertPostAsync(MakePost(), force: false);
            var callsAfterFirst = emb.EmbedCalls;
            var result2 = await store.UpsertPostAsync(MakePost(), force: true);

            Assert.Equal(VectorWriteOutcome.Embedded, result2.Summary);
            Assert.Equal(VectorWriteOutcome.Embedded, result2.Body);
            Assert.Equal(callsAfterFirst + 2, emb.EmbedCalls);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task DeleteStalePostsAsync_RemovesPostsNotInCurrentSet()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new StubEmbeddingClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

            await store.UpsertPostAsync(MakePost(slug: "one"), force: false);
            await store.UpsertPostAsync(MakePost(slug: "two"), force: false);
            await store.UpsertPostAsync(MakePost(slug: "three"), force: false);

            // current set keeps "one" and "three"; "two" should be deleted (both vectors)
            var current = new List<(string Collection, string Slug)>
            {
                ("blog", "one"),
                ("blog", "three"),
            };

            var deleted = await store.DeleteStalePostsAsync(current);

            Assert.Equal(2, deleted);  // summary + body for "two"
            Assert.Null(await store.ReadPostDocumentAsync("blog__two__summary"));
            Assert.Null(await store.ReadPostDocumentAsync("blog__two__body"));
            Assert.NotNull(await store.ReadPostDocumentAsync("blog__one__summary"));
            Assert.NotNull(await store.ReadPostDocumentAsync("blog__three__summary"));
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task SearchAsync_EmptyCollection_ReturnsEmpty()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new StubEmbeddingClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

            await store.EnsureSchemaAsync();

            var results = await store.SearchAsync(
                queryVec: new[] { 0.1f, 0.2f, 0.3f, 0.4f },
                kinds: new[] { MemoryKind.Post },
                tenants: new[] { "public" },
                rawK: 10);

            Assert.Empty(results);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task SearchAsync_FiltersOutPendingEmbeddingStatus()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new StubEmbeddingClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

            await store.UpsertPostAsync(MakePost(slug: "ready"), force: false);

            // Manually insert a pending doc directly
            await store.InsertRawPostAsync(new Dictionary<string, object?>
            {
                ["_key"] = "blog__pending__summary",
                ["slug"] = "pending",
                ["collection"] = "blog",
                ["vector_kind"] = "summary",
                ["tenant_id"] = "public",
                ["status"] = "pending_embedding",
                ["embedding"] = new float[] { 0.1f, 0.2f, 0.3f, 0.4f },
                ["text"] = "x",
                ["title"] = "Pending Post",
                ["description"] = "",
            });

            var results = await store.SearchAsync(
                queryVec: new[] { 0.1f, 0.2f, 0.3f, 0.4f },
                kinds: new[] { MemoryKind.Post },
                tenants: new[] { "public" },
                rawK: 10);

            Assert.All(results, r => Assert.NotEqual("pending", r.Slug));
            Assert.Contains(results, r => r.Slug == "ready");
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }
}
