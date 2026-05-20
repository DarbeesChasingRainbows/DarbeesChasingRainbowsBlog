using System.Net.Http;
using System.Text.Json;
using Darbee.Gateway.Infrastructure.Arango;
using Darbee.Gateway.Domain.Ports;
using Darbee.Gateway.Domain.Models;
using Darbee.Gateway.Domain.ValueObjects;

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
        public Func<string, bool>? ShouldFailOn { get; set; }

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            EmbedCalls++;
            if (ShouldFailOn?.Invoke(text) == true)
                throw new InvalidOperationException("stub embedding failure");
            return Task.FromResult(new[] { 0.1f, 0.2f, 0.3f, 0.4f });
        }

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            EmbedCalls += texts.Count;
            foreach (var t in texts)
                if (ShouldFailOn?.Invoke(t) == true)
                    throw new InvalidOperationException("stub embedding failure");
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
            var store = new ArangoMemoryRepository(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, new StubDomainEventDispatcher(), emb);

            var result = await store.UpsertPostAsync(MakePost(), force: false);

            Assert.Equal("welcome", result.Slug);
            Assert.Equal("blog", result.Collection);
            Assert.Equal(VectorWriteOutcome.Embedded, result.Summary);
            Assert.Equal(VectorWriteOutcome.Embedded, result.Body);
            Assert.Equal(2, emb.EmbedCalls);

            using var summaryDoc = await store.ReadPostDocumentAsync("blog__welcome__summary");
            using var bodyDoc = await store.ReadPostDocumentAsync("blog__welcome__body");
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
            var store = new ArangoMemoryRepository(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, new StubDomainEventDispatcher(), emb);

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
    public async Task UpsertPostAsync_HashMatch_ButStatusPending_TreatsAsCacheMiss_AndReembeds()
    {
        // Post-migration recovery scenario: MigrateEmbeddingsAsync clears
        // doc.embedding and sets status=pending_embedding but leaves hash intact.
        // A subsequent reindex (without --force) must re-embed those docs,
        // not skip them via hash-only cache.
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new StubEmbeddingClient();
            var store = new ArangoMemoryRepository(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, new StubDomainEventDispatcher(), emb);

            // First write: doc lands with status=ready
            var first = await store.UpsertPostAsync(MakePost(), force: false);
            Assert.Equal(VectorWriteOutcome.Embedded, first.Summary);
            Assert.Equal(VectorWriteOutcome.Embedded, first.Body);
            var callsAfterFirst = emb.EmbedCalls;

            // Simulate migration: flip the existing summary doc to status=pending_embedding
            // (preserving hash) — mirrors what MigrateEmbeddingsAsync.preserve-and-reembed does.
            // The embedding key is OMITTED (not set to null) because the sparse vector index
            // in ArangoDB 3.12 rejects explicit-null writes on the indexed field.
            await store.InsertRawPostAsync(new Dictionary<string, object?>
            {
                ["_key"] = "blog__welcome__summary",
                ["slug"] = "welcome",
                ["collection"] = "blog",
                ["vector_kind"] = "summary",
                ["tenant_id"] = "public",
                ["text"] = "(stale)",
                ["hash"] = (await store.ReadPostHashAsync("blog__welcome__summary"))!,
                ["title"] = "Welcome",
                ["description"] = "An intro post.",
                ["status"] = "pending_embedding",
            });

            // Reindex without --force. Despite the hash matching, status=pending_embedding
            // must drive a re-embed (cache miss).
            var second = await store.UpsertPostAsync(MakePost(), force: false);
            Assert.Equal(VectorWriteOutcome.Embedded, second.Summary);
            Assert.Equal(callsAfterFirst + 1, emb.EmbedCalls);
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
            var store = new ArangoMemoryRepository(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, new StubDomainEventDispatcher(), emb);

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
            var store = new ArangoMemoryRepository(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, new StubDomainEventDispatcher(), emb);

            await store.UpsertPostAsync(MakePost(slug: "one"), force: false);
            await store.UpsertPostAsync(MakePost(slug: "two"), force: false);
            await store.UpsertPostAsync(MakePost(slug: "three"), force: false);

            // current set keeps "one" and "three"; "two" should be deleted (both vectors)
            var current = new List<(string Collection, string Slug)>
            {
                ("blog", "one"),
                ("blog", "three"),
            };

            var deleted = await store.DeleteStalePostsAsync(current, scopedCollections: null);

            Assert.Equal(2, deleted);  // summary + body for "two"
            using var twoSummary = await store.ReadPostDocumentAsync("blog__two__summary");
            using var twoBody = await store.ReadPostDocumentAsync("blog__two__body");
            using var oneDoc = await store.ReadPostDocumentAsync("blog__one__summary");
            using var threeDoc = await store.ReadPostDocumentAsync("blog__three__summary");
            Assert.Null(twoSummary);
            Assert.Null(twoBody);
            Assert.NotNull(oneDoc);
            Assert.NotNull(threeDoc);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task DeleteStalePostsAsync_ScopedToCollections_LeavesOtherCollectionsAlone()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new StubEmbeddingClient();
            var store = new ArangoMemoryRepository(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, new StubDomainEventDispatcher(), emb);

            // Seed: one blog, one project
            await store.UpsertPostAsync(MakePost(slug: "blog-one", collection: "blog"), force: false);
            await store.UpsertPostAsync(MakePost(slug: "proj-one", collection: "projects"), force: false);

            // Stale-delete scoped to blog only, with NO blog posts in current set
            var deleted = await store.DeleteStalePostsAsync(
                currentPosts: Array.Empty<(string, string)>(),
                scopedCollections: new[] { "blog" });

            Assert.Equal(2, deleted);  // blog-one summary + body
            using var blogDoc = await store.ReadPostDocumentAsync("blog__blog-one__summary");
            using var projDoc = await store.ReadPostDocumentAsync("projects__proj-one__summary");
            Assert.Null(blogDoc);
            Assert.NotNull(projDoc);  // Projects unaffected
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
            var store = new ArangoMemoryRepository(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, new StubDomainEventDispatcher(), emb);

            await store.EnsureSchemaAsync();

            var results = await store.SearchAsync(
                queryVec: new[] { 0.1f, 0.2f, 0.3f, 0.4f },
                kinds: new[] { MemoryKind.Post },
                tenants: new[] { new TenantId("public") },
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
            var store = new ArangoMemoryRepository(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, new StubDomainEventDispatcher(), emb);

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
                tenants: new[] { new TenantId("public") },
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
