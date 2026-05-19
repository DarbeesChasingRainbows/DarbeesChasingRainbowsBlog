using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;

namespace Darbee.Gateway.Tests.Memory;

/// <summary>
/// B5 — The single most important test in Phase 11.
/// Identical text with identical cosine scores written under two tenants.
/// Confirms that tenant_id filter — not content uniqueness — enforces isolation.
/// </summary>
[Trait("Category", "Integration")]
public class CrossTenantIsolationTests
{
    private const string TenantAlpha = "tenant-alpha";
    private const string TenantBeta  = "tenant-beta";

    // Two embedding vectors with high cosine similarity to a [1,1,1,1] query vec.
    // They are identical so the test cannot pass by content uniqueness.
    private static readonly float[] AlphaVec = [0.9f, 0.9f, 0.9f, 0.9f];
    private static readonly float[] BetaVec  = [0.9f, 0.9f, 0.9f, 0.9f];
    private static readonly float[] QueryVec = [1f, 1f, 1f, 1f];

    // Emit a deterministic vector per tenant so we can distinguish which tenant
    // produced a result if the isolation breaks and both appear in one response.
    private sealed class TenantAwareEmbeddingClient(string tenantId) : IEmbeddingClient
    {
        public int Dimension => 4;
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
            => Task.FromResult(tenantId == TenantAlpha ? AlphaVec : BetaVec);
        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => tenantId == TenantAlpha ? AlphaVec : BetaVec).ToArray());
    }

    [Fact]
    public async Task SearchAsync_WhenTwoTenantsWriteIdenticalText_EachTenantSeesOnlyOwnResults()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();

            // Store for tenant-alpha
            var storeAlpha = new MemoryStore(
                MemoryStoreSchemaTests.ArangoUrl, dbName,
                MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http,
                new TenantAwareEmbeddingClient(TenantAlpha));
            await storeAlpha.EnsureSchemaAsync();

            // Store for tenant-beta (shares the same DB — same as production sharing one DB)
            var storeBeta = new MemoryStore(
                MemoryStoreSchemaTests.ArangoUrl, dbName,
                MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http,
                new TenantAwareEmbeddingClient(TenantBeta));

            const string SameText = "kingdom farm decision: use raised beds for the first growing season";

            // Write identical text under both tenants
            var resultAlpha = await storeAlpha.UpsertDecisionAsync(
                TenantAlpha, "kingdom farm beds", "raised beds", SameText, Array.Empty<string>());
            var resultBeta = await storeBeta.UpsertDecisionAsync(
                TenantBeta, "kingdom farm beds", "raised beds", SameText, Array.Empty<string>());

            Assert.True(resultAlpha.Completed, "alpha write should complete");
            Assert.True(resultBeta.Completed, "beta write should complete");

            // Search scoped strictly to tenant-alpha only
            var alphaHits = await storeAlpha.SearchAsync(
                QueryVec,
                kinds: [MemoryKind.Decision],
                tenants: [TenantAlpha],
                rawK: 10);

            // Search scoped strictly to tenant-beta only
            var betaHits = await storeBeta.SearchAsync(
                QueryVec,
                kinds: [MemoryKind.Decision],
                tenants: [TenantBeta],
                rawK: 10);

            // Each tenant should find exactly one result — their own
            Assert.True(alphaHits.Count >= 1, "alpha search should return at least one hit");
            Assert.True(betaHits.Count >= 1, "beta search should return at least one hit");

            // Isolation: alpha results must not contain any beta tenant doc
            Assert.All(alphaHits, h => Assert.Equal(TenantAlpha, h.TenantId));

            // Isolation: beta results must not contain any alpha tenant doc
            Assert.All(betaHits, h => Assert.Equal(TenantBeta, h.TenantId));
        }
        finally { await MemoryStoreSchemaTests.DropDb(dbName); }
    }

    [Fact]
    public async Task SearchAsync_CrossTenantQuery_DoesNotLeakAcrossTenants()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var store = new MemoryStore(
                MemoryStoreSchemaTests.ArangoUrl, dbName,
                MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http,
                new MemoryStoreWriteTests.ConstantEmbeddingClient(4));
            await store.EnsureSchemaAsync();

            // Write one fact under each tenant
            await store.UpsertFactAsync(TenantAlpha, "alpha-only fact", null);
            await store.UpsertFactAsync(TenantBeta,  "beta-only fact",  null);

            // Query with alpha tenant list — must not return beta
            var alphaOnly = await store.SearchAsync(
                QueryVec,
                kinds: [MemoryKind.Fact],
                tenants: [TenantAlpha],
                rawK: 100);

            Assert.NotEmpty(alphaOnly);
            Assert.DoesNotContain(alphaOnly, h => h.TenantId == TenantBeta);

            // Query with beta tenant list — must not return alpha
            var betaOnly = await store.SearchAsync(
                QueryVec,
                kinds: [MemoryKind.Fact],
                tenants: [TenantBeta],
                rawK: 100);

            Assert.NotEmpty(betaOnly);
            Assert.DoesNotContain(betaOnly, h => h.TenantId == TenantAlpha);
        }
        finally { await MemoryStoreSchemaTests.DropDb(dbName); }
    }
}
