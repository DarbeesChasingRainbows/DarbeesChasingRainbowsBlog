using Darbee.Gateway.Infrastructure.Arango;
using Darbee.Gateway.Domain.Ports;
using Darbee.Gateway.Domain.Models;
using Darbee.Gateway.Domain.ValueObjects;
using Darbee.Gateway.Domain.ValueObjects;

namespace Darbee.Gateway.Tests.Memory;

[Trait("Category", "Integration")]
public class MemoryStoreWriteTests
{
    internal sealed class ConstantEmbeddingClient(int dim, float[]? value = null) : IEmbeddingClient
    {
        public int Dimension => dim;
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
            => Task.FromResult(value ?? Enumerable.Repeat(0.1f, dim).ToArray());
        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => value ?? Enumerable.Repeat(0.1f, dim).ToArray()).ToArray());
    }

    internal sealed class FailingEmbeddingClient(int dim) : IEmbeddingClient
    {
        public int Dimension => dim;
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
            => throw new HttpRequestException("LM Studio unavailable");
        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            => throw new HttpRequestException("LM Studio unavailable");
    }

    [Fact]
    public async Task UpsertDecisionAsync_WhenEmbeddingSucceeds_ReturnsCompleted()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var store = new ArangoMemoryRepository(
                MemoryStoreSchemaTests.ArangoUrl, dbName,
                MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
                "test-embed-model", embeddingDimension: 4, vectorNLists: 1, http, new StubDomainEventDispatcher(), new ConstantEmbeddingClient(4));
            await store.EnsureSchemaAsync();

            var result = await store.UpsertDecisionAsync(
                tenantId: new TenantId("admin"),
                subject: "PostCard kind union",
                chose: "discriminated union",
                because: "type narrowing in TS6",
                alternatives: new[] { "polymorphic", "any" });

            Assert.True(result.Completed);
            Assert.False(result.Queued);
            Assert.False(string.IsNullOrEmpty(result.Id));
        }
        finally { await MemoryStoreSchemaTests.DropDb(dbName); }
    }

    [Fact]
    public async Task UpsertDecisionAsync_WhenEmbeddingFails_QueuesPending()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var store = new ArangoMemoryRepository(
                MemoryStoreSchemaTests.ArangoUrl, dbName,
                MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
                "test-embed-model", embeddingDimension: 4, vectorNLists: 1, http, new StubDomainEventDispatcher(), new FailingEmbeddingClient(4));
            await store.EnsureSchemaAsync();

            var result = await store.UpsertDecisionAsync(
                tenantId: new TenantId("admin"), subject: "x", chose: "a", because: "b", alternatives: Array.Empty<string>());

            Assert.False(result.Completed);
            Assert.True(result.Queued);

            var pending = await store.ListPendingEmbeddingsAsync();
            Assert.Single(pending);
            Assert.Equal(MemoryCollections.Decisions, pending[0].targetCollection);
        }
        finally { await MemoryStoreSchemaTests.DropDb(dbName); }
    }
}
