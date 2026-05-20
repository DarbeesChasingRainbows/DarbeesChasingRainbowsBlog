using Darbee.Gateway.Domain.Ports;
using Darbee.Gateway.Domain.Models;
using Darbee.Gateway.Domain.ValueObjects;
using Darbee.Gateway.Infrastructure.Arango;
using System.Net.Http;

namespace Darbee.Gateway.Tests.Memory;

[Trait("Category", "Integration")]
public class MemoryRecallEngineTests
{
    private static ArangoMemoryRepository NewStore(string dbName, HttpClient http, IEmbeddingClient emb) =>
        new ArangoMemoryRepository(
            MemoryStoreSchemaTests.ArangoUrl, dbName,
            MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
            "test-model", embeddingDimension: 4, vectorNLists: 1, http,
            new StubDomainEventDispatcher(), emb);

    [Fact]
    public async Task ExtractEntities_MatchesCanonicalNameSubstring()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStoreWriteTests.ConstantEmbeddingClient(4);
            var store = NewStore(dbName, http, emb);
            await store.EnsureSchemaAsync();

            var chickenId = await store.UpsertEntityAsync(new TenantId("admin"), "chickens", new[] { "chooks" }, "concept");
            await store.UpsertEntityAsync(new TenantId("admin"), "PostCard", Array.Empty<string>(), "file");

            var extractor = new ArangoEntityExtractor(store);
            var engine = new ArangoRecallEngine(store, emb, extractor, alpha: 0.7, beta: 0.3);

            var result = await engine.ExtractEntitiesAsync(new TenantId("admin"), "who fed the chickens yesterday");

            Assert.Single(result);
            Assert.Equal($"{MemoryCollections.Entities}/{chickenId}", result[0]);
        }
        finally { await MemoryStoreSchemaTests.DropDb(dbName); }
    }

    [Fact]
    public async Task ExtractEntities_MatchesAliasSubstring()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStoreWriteTests.ConstantEmbeddingClient(4);
            var store = NewStore(dbName, http, emb);
            await store.EnsureSchemaAsync();
            var id = await store.UpsertEntityAsync(new TenantId("admin"), "chickens", new[] { "chooks", "hens" }, "concept");

            var extractor = new ArangoEntityExtractor(store);
            var engine = new ArangoRecallEngine(store, emb, extractor, alpha: 0.7, beta: 0.3);
            var result = await engine.ExtractEntitiesAsync(new TenantId("admin"), "checked the chooks this morning");

            Assert.Contains($"{MemoryCollections.Entities}/{id}", result);
        }
        finally { await MemoryStoreSchemaTests.DropDb(dbName); }
    }

    [Fact]
    public async Task ExtractEntities_TenantIsolated_DoesNotMatchOtherTenantsEntities()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStoreWriteTests.ConstantEmbeddingClient(4);
            var store = NewStore(dbName, http, emb);
            await store.EnsureSchemaAsync();
            // Same canonical name, different tenants
            await store.UpsertEntityAsync(new TenantId("admin"), "chickens", Array.Empty<string>(), "concept");
            await store.UpsertEntityAsync(new TenantId("kid:a"), "chickens", Array.Empty<string>(), "concept");

            var extractor = new ArangoEntityExtractor(store);
            var engine = new ArangoRecallEngine(store, emb, extractor, alpha: 0.7, beta: 0.3);
            var adminHits = await engine.ExtractEntitiesAsync(new TenantId("admin"), "feed the chickens");
            var kidHits = await engine.ExtractEntitiesAsync(new TenantId("kid:a"), "feed the chickens");

            Assert.Single(adminHits);
            Assert.Single(kidHits);
            Assert.NotEqual(adminHits[0], kidHits[0]);
        }
        finally { await MemoryStoreSchemaTests.DropDb(dbName); }
    }

    [Fact]
    public async Task GraphExpand_FindsItemsConnectedToEntityWithinHopBudget()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStoreWriteTests.ConstantEmbeddingClient(4);
            var store = NewStore(dbName, http, emb);
            await store.EnsureSchemaAsync();

            var entityId = await store.UpsertEntityAsync(new TenantId("admin"), "PostCard", Array.Empty<string>(), "file");
            var dec = await store.UpsertDecisionAsync(
                new TenantId("admin"), "PostCard kind union", "discriminated union",
                "TS6 narrowing", Array.Empty<string>());
            await store.UpsertEdgeAsync(
                new TenantId("admin"),
                $"{MemoryCollections.Decisions}/{dec.Id}",
                $"{MemoryCollections.Entities}/{entityId}",
                "mentions", 1.0);

            var extractor = new ArangoEntityExtractor(store);
            var engine = new ArangoRecallEngine(store, emb, extractor, alpha: 0.7, beta: 0.3);
            var expanded = await engine.GraphExpandAsync(
                new TenantId("admin"),
                new[] { $"{MemoryCollections.Entities}/{entityId}" },
                expandHops: 1);

            Assert.Single(expanded);
            Assert.Equal(MemoryKind.Decision, expanded[0].Item.Kind);
            Assert.Equal(1, expanded[0].HopsFromQuery);
        }
        finally { await MemoryStoreSchemaTests.DropDb(dbName); }
    }

    /// <summary>
    /// Embeds the query as one vector and other texts so we can predict ordering.
    /// Each registered (substring, vector) pair maps any text containing the substring
    /// to that vector; everything else falls back to the query embedding.
    /// </summary>
    internal sealed class TextMappedEmbeddings : IEmbeddingClient
    {
        private readonly float[] _queryEmb;
        private readonly List<(string substring, float[] vec)> _map;
        public int Dimension { get; }

        public TextMappedEmbeddings(float[] queryEmb, params (string substring, float[] vec)[] map)
        {
            _queryEmb = queryEmb;
            _map = map.ToList();
            Dimension = queryEmb.Length;
        }

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            foreach (var (sub, vec) in _map)
                if (text.Contains(sub, StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(vec);
            return Task.FromResult(_queryEmb);
        }

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(txt => EmbedAsync(txt).Result).ToArray());
    }

    [Fact]
    public async Task RecallAsync_CombinesGraphAndVectorTopK_AndScoresGraphConnectedHigher()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            // Query embeds to [1,0,0,0]; "chickens" decision aligns; "weather" decision is orthogonal.
            var queryEmb = new float[] { 1f, 0f, 0f, 0f };
            var emb = new TextMappedEmbeddings(
                queryEmb,
                ("chickens", new float[] { 0.9f, 0.1f, 0f, 0f }),
                ("weather", new float[] { 0f, 1f, 0f, 0f }));
            var store = NewStore(dbName, http, emb);
            await store.EnsureSchemaAsync();

            var ent = await store.UpsertEntityAsync(new TenantId("admin"), "chickens", Array.Empty<string>(), "concept");
            var dec1 = await store.UpsertDecisionAsync(
                new TenantId("admin"), "chickens housing", "free range",
                "chickens are happier outdoors", Array.Empty<string>());
            var dec2 = await store.UpsertDecisionAsync(
                new TenantId("admin"), "weather plan", "summer schedule",
                "weather is warm enough", Array.Empty<string>());
            await store.UpsertEdgeAsync(
                new TenantId("admin"),
                $"{MemoryCollections.Decisions}/{dec1.Id}",
                $"{MemoryCollections.Entities}/{ent}",
                "mentions", 1.0);

            var extractor = new ArangoEntityExtractor(store);
            var engine = new ArangoRecallEngine(store, emb, extractor, alpha: 0.7, beta: 0.3);
            var result = await engine.RecallAsync(new TenantId("admin"), "what about the chickens", topK: 5);

            Assert.NotEmpty(result.Items);
            Assert.NotEmpty(result.ExtractedEntityIds);
            // dec1 should rank first (graph-connected + high cosine)
            Assert.Contains("chickens", result.Items[0].Item.Text);
            // dec1 should have a hop count (graph-connected); dec2 either absent or hops=null.
            Assert.NotNull(result.Items[0].HopsFromQuery);
        }
        finally { await MemoryStoreSchemaTests.DropDb(dbName); }
    }

    /// <summary>
    /// Regression test for two latent bugs caught while researching the
    /// COSINE_SIMILARITY vs APPROX_NEAR_COSINE choice:
    ///   1. Multiple writes to the same collection (after lazy vector index
    ///      creation kicks in) used to fail with "vector field not present in
    ///      document" because the pre-existing index was not sparse.
    ///   2. VectorTopKAsync needs to return correct results across multiple
    ///      docs, including after the index has been built.
    /// </summary>
    [Fact]
    public async Task VectorTopK_MultipleWrites_DoNotBreakAfterIndexCreation()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStoreWriteTests.ConstantEmbeddingClient(4);
            var store = NewStore(dbName, http, emb);
            await store.EnsureSchemaAsync();

            // vectorNLists=1 → first write triggers index creation, subsequent writes
            // must succeed with the index in place (this is what the sparse=true fix
            // enables).
            for (int i = 0; i < 5; i++)
            {
                var result = await store.UpsertDecisionAsync(
                    new TenantId("admin"), $"subject {i}", "x", $"because {i}", Array.Empty<string>());
                Assert.True(result.Completed, $"write {i} should complete (sparse index allows pending docs)");
            }

            // Index should be trained and discoverable.
            Assert.True(
                await store.IsVectorIndexReadyAsync(MemoryCollections.Decisions),
                "vector index for memory_decisions should be trained after writes");

            var extractor = new ArangoEntityExtractor(store);
            var engine = new ArangoRecallEngine(store, emb, extractor, alpha: 0.7, beta: 0.3);
            var topK = await engine.VectorTopKAsync(new TenantId("admin"), new float[] { 1, 1, 1, 1 }, limit: 5);

            Assert.Equal(5, topK.Count);
            Assert.All(topK, c => Assert.Equal("admin", c.Item.TenantId.Value));
        }
        finally { await MemoryStoreSchemaTests.DropDb(dbName); }
    }

    [Fact]
    public async Task RecallAsync_TenantIsolated_DoesNotReturnOtherTenantsItems()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStoreWriteTests.ConstantEmbeddingClient(4);
            var store = NewStore(dbName, http, emb);
            await store.EnsureSchemaAsync();

            await store.UpsertDecisionAsync(new TenantId("admin"), "policy", "x", "admin-only fact", Array.Empty<string>());
            await store.UpsertDecisionAsync(new TenantId("kid:a"), "snack", "x", "kid-only fact", Array.Empty<string>());

            var extractor = new ArangoEntityExtractor(store);
            var engine = new ArangoRecallEngine(store, emb, extractor, alpha: 0.7, beta: 0.3);
            var kidResult = await engine.RecallAsync(new TenantId("kid:a"), "anything goes", topK: 8);

            Assert.All(kidResult.Items, i => Assert.Equal("kid:a", i.Item.TenantId.Value));
            Assert.DoesNotContain(kidResult.Items, i => i.Item.Text.Contains("admin-only"));
        }
        finally { await MemoryStoreSchemaTests.DropDb(dbName); }
    }

    [Fact]
    public async Task GraphExpand_ReturnsEmptyWhenNoSeedEntities()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStoreWriteTests.ConstantEmbeddingClient(4);
            var store = NewStore(dbName, http, emb);
            await store.EnsureSchemaAsync();

            var extractor = new ArangoEntityExtractor(store);
            var engine = new ArangoRecallEngine(store, emb, extractor, alpha: 0.7, beta: 0.3);
            var expanded = await engine.GraphExpandAsync(new TenantId("admin"), Array.Empty<string>(), expandHops: 2);

            Assert.Empty(expanded);
        }
        finally { await MemoryStoreSchemaTests.DropDb(dbName); }
    }
}