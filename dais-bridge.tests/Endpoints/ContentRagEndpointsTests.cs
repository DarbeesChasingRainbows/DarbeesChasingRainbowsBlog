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
            using var deletedSummary = await store.ReadPostDocumentAsync("blog__delete-this__summary");
            using var keptSummary = await store.ReadPostDocumentAsync("blog__keep-this__summary");
            Assert.Null(deletedSummary);
            Assert.NotNull(keptSummary);
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

    [Fact]
    public async Task HandleSearchAsync_ReturnsDedupedTopKByBestVectorKind()
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

            var seed = new ReindexRequest(false, new[] {
                MakeReindexPost("alpha"),
                MakeReindexPost("beta"),
                MakeReindexPost("gamma"),
            });
            await ContentRagEndpoints.HandleReindexAsync(seed, store, emb);

            var search = new SearchRequest(Query: "anything", Kinds: null, K: 2, Tenant: null);
            var result = await ContentRagEndpoints.HandleSearchAsync(search, store, emb);

            Assert.Equal(2, result.Results.Count);
            // Each slug appears at most once (deduped):
            Assert.Equal(result.Results.Select(r => r.Slug).Distinct().Count(), result.Results.Count);
            Assert.All(result.Results, r => Assert.Equal("blog", r.Collection));
            Assert.All(result.Results, r => Assert.StartsWith("/blog/", r.Url));
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task HandleSearchAsync_EmptyCollection_ReturnsEmpty()
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
            await store.EnsureSchemaAsync();

            var search = new SearchRequest(Query: "x", Kinds: null, K: 5, Tenant: null);
            var result = await ContentRagEndpoints.HandleSearchAsync(search, store, emb);

            Assert.Empty(result.Results);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task HandleMigrateAsync_NoMismatch_IsNoop()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var store = new MemoryStore(ArangoUrl, dbName,
                MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
                "qwen3-embedding-8b", embeddingDimension: 4096, vectorNLists: 100, http);
            await store.EnsureSchemaAsync();

            var request = new MigrateRequest("preserve-and-reembed");
            var result = await ContentRagEndpoints.HandleMigrateAsync(request, store);

            Assert.NotNull(result.Previous);
            Assert.Equal(result.Previous, result.Current);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task HandleMigrateAsync_InvalidConfirm_Throws()
    {
        using var http = new HttpClient();
        var store = new MemoryStore("http://unused:8529", "unused", "u", "p",
            "m", 4, 1, http);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            ContentRagEndpoints.HandleMigrateAsync(new MigrateRequest("bad"), store));
    }

    [Fact]
    public async Task HandleReindexAsync_OnePostFails_OthersStillSucceed_ResponseListsFailure()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStorePostsTests.StubEmbeddingClient
            {
                ShouldFailOn = text => text.Contains("broken-post"),
            };
            var store = new MemoryStore(ArangoUrl, dbName,
                MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

            var goodPost = MakeReindexPost("good-post");
            var brokenPost = new ReindexPost(
                Collection: "blog",
                Slug: "broken-post",
                Frontmatter: new ReindexFrontmatter(
                    Title: "broken-post",
                    Description: "intro",
                    PubDate: "2026-04-29",
                    Category: "Faith",
                    Tags: null, AiSummary: null, KeyTakeaways: null,
                    Faq: null, EntityMentions: null),
                Body: "body for broken-post");
            var request = new ReindexRequest(Force: false, Posts: new[] { goodPost, brokenPost });

            var response = await ContentRagEndpoints.HandleReindexAsync(request, store, emb);

            Assert.Equal(2, response.Scanned);
            Assert.Equal(2, response.Posts.Count);

            var goodResult = response.Posts.Single(p => p.Slug == "good-post");
            Assert.Equal("embedded", goodResult.Summary);
            Assert.Equal("embedded", goodResult.Body);
            Assert.Null(goodResult.FailureReason);

            var brokenResult = response.Posts.Single(p => p.Slug == "broken-post");
            Assert.Equal("failed", brokenResult.Summary);
            Assert.Equal("failed", brokenResult.Body);
            Assert.Contains("stub embedding failure", brokenResult.FailureReason);

            // Good post's vectors should be persisted; broken post's should not
            using var goodDoc = await store.ReadPostDocumentAsync("blog__good-post__summary");
            using var brokenDoc = await store.ReadPostDocumentAsync("blog__broken-post__summary");
            Assert.NotNull(goodDoc);
            Assert.Null(brokenDoc);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task HandleReindexAsync_EmptyPosts_ThrowsArgumentException_DoesNotDeleteAnything()
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

            // Seed: index two posts via a normal reindex
            var seed = new ReindexRequest(false, new[] { MakeReindexPost("a"), MakeReindexPost("b") });
            await ContentRagEndpoints.HandleReindexAsync(seed, store, emb);

            // Now an empty-payload reindex must throw and NOT delete anything
            var emptyRequest = new ReindexRequest(false, Array.Empty<ReindexPost>());
            await Assert.ThrowsAsync<ArgumentException>(() =>
                ContentRagEndpoints.HandleReindexAsync(emptyRequest, store, emb));

            // Verify seeded docs are still present
            using var aDoc = await store.ReadPostDocumentAsync("blog__a__summary");
            using var bDoc = await store.ReadPostDocumentAsync("blog__b__summary");
            Assert.NotNull(aDoc);
            Assert.NotNull(bDoc);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task HandleSearchAsync_BackCompat_DefaultsToPostsPublic()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStoreNotesTests.StubEmbeddingClient();
            var store = new MemoryStore(ArangoUrl, dbName, MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);
            await store.UpsertPostAsync(MemoryStorePostsTests.MakePost(slug: "p1"), force: false);
            await store.UpsertNoteAsync(MemoryStoreNotesTests.MakeNote(key: "obsidian://n1.md"));

            var req = new SearchRequest(Query: "hello", Kinds: null, K: 5, Tenant: null, Tenants: null);
            var resp = await ContentRagEndpoints.HandleSearchAsync(req, store, emb);

            Assert.All(resp.Results, r => Assert.Equal("post", r.Kind));
            Assert.All(resp.Results, r => Assert.Equal("public", r.Tenant));
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task HandleSearchAsync_KindsFilter_ReturnsOnlyObservations()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStoreNotesTests.StubEmbeddingClient();
            var store = new MemoryStore(ArangoUrl, dbName, MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);
            await store.UpsertPostAsync(MemoryStorePostsTests.MakePost(slug: "p1"), force: false);
            await store.UpsertNoteAsync(MemoryStoreNotesTests.MakeNote(key: "obsidian://n1.md"));

            var req = new SearchRequest(
                Query: "hello",
                Kinds: new[] { "observation" },
                K: 5,
                Tenant: null,
                Tenants: new[] { "private" });
            var resp = await ContentRagEndpoints.HandleSearchAsync(req, store, emb);

            Assert.NotEmpty(resp.Results);
            Assert.All(resp.Results, r => Assert.Equal("observation", r.Kind));
            Assert.All(resp.Results, r => Assert.Equal("private", r.Tenant));
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task HandleSearchAsync_TenantIsolation_PrivateNeverLeaksWhenQueryingPublic()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStoreNotesTests.StubEmbeddingClient(); // returns identical embedding for any text
            var store = new MemoryStore(ArangoUrl, dbName, MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);
            await store.UpsertPostAsync(MemoryStorePostsTests.MakePost(slug: "publicPost"), force: false);
            await store.UpsertNoteAsync(MemoryStoreNotesTests.MakeNote(key: "obsidian://privateNote.md"));

            var req = new SearchRequest(
                Query: "anything",
                Kinds: new[] { "post", "observation" },
                K: 5,
                Tenant: null,
                Tenants: new[] { "public" });
            var resp = await ContentRagEndpoints.HandleSearchAsync(req, store, emb);

            Assert.NotEmpty(resp.Results);
            Assert.All(resp.Results, r => Assert.Equal("public", r.Tenant));
            Assert.DoesNotContain(resp.Results, r => r.Kind == "observation");
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task HandleSearchAsync_UnionAcrossKinds_RanksByScore()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStoreNotesTests.StubEmbeddingClient();
            var store = new MemoryStore(ArangoUrl, dbName, MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);
            await store.UpsertPostAsync(MemoryStorePostsTests.MakePost(slug: "p1"), force: false);
            await store.UpsertNoteAsync(MemoryStoreNotesTests.MakeNote(key: "obsidian://n1.md"));

            var req = new SearchRequest(
                Query: "x",
                Kinds: new[] { "post", "observation" },
                K: 10,
                Tenant: null,
                Tenants: new[] { "public", "private" });
            var resp = await ContentRagEndpoints.HandleSearchAsync(req, store, emb);

            for (int i = 1; i < resp.Results.Count; i++)
                Assert.True(resp.Results[i - 1].Score >= resp.Results[i].Score);

            Assert.Contains(resp.Results, r => r.Kind == "post");
            Assert.Contains(resp.Results, r => r.Kind == "observation");
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task HandleIngestNotesAsync_RoundTrip_ReturnsCountsAndStaleDeletes()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStoreNotesTests.StubEmbeddingClient();
            var store = new MemoryStore(ArangoUrl, dbName, MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

            // Pre-seed an obsidian note that will become stale.
            await store.UpsertNoteAsync(MemoryStoreNotesTests.MakeNote(key: "obsidian://old.md"));

            var req = new IngestNotesRequest(
                Tenant: "private",
                Notes: new[]
                {
                    new NoteRecord(
                        Key: "obsidian://new.md",
                        Kind: "observation",
                        Text: "fresh note",
                        Title: "new",
                        Metadata: null),
                },
                CurrentKeys: new[] { "obsidian://new.md" });
            var resp = await ContentRagEndpoints.HandleIngestNotesAsync(req, store);

            Assert.Equal(1, resp.EmbeddedCount);
            Assert.Equal(0, resp.CachedCount);
            Assert.Equal(0, resp.FailedCount);
            Assert.Equal(1, resp.StaleDeletedCount);
            Assert.Equal("embedded", resp.PerNote.Single(p => p.Key == "obsidian://new.md").Outcome);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }
}
