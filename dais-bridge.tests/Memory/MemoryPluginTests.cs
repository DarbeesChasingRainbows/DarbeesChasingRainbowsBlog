using Darbee.Gateway.Infrastructure.Arango;
using Darbee.Gateway.Domain.Models;
using Darbee.Gateway.Models;
using Darbee.Gateway.Domain.ValueObjects;
using Darbee.Gateway.Plugins;
using Xunit;
using System.Net.Http;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Darbee.Gateway.Tests.Memory;

[Trait("Category", "Integration")]
public class MemoryPluginTests
{
    [Fact]
    public async Task RememberDecision_WhenTenantSet_WritesUnderTenant()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStoreWriteTests.ConstantEmbeddingClient(4);
            var store = new ArangoMemoryRepository(MemoryStoreSchemaTests.ArangoUrl, dbName,
                MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
                "test-model", 4, 1, http, new StubDomainEventDispatcher(), emb);
            await store.EnsureSchemaAsync();

            var acc = new TenantContextAccessor { Current = TenantContext.Admin };
            var recall = new ArangoRecallEngine(store, emb, new ArangoEntityExtractor(store), alpha: 0.7, beta: 0.3);
            var plugin = new MemoryPlugin(store, acc, recall);

            var json = await plugin.RememberDecision("subject", "x", "because", new[] { "y" });
            Assert.Contains("\"completed\":true", json);
        }
        finally { await MemoryStoreSchemaTests.DropDb(dbName); }
    }

    [Fact]
    public async Task RememberDecision_WhenTenantUnset_Throws()
    {
        using var http = new HttpClient();
        // Note: store is just a shell here, won't be called if accessor throws first
        var emb = new MemoryStoreWriteTests.ConstantEmbeddingClient(4);
        var store = new ArangoMemoryRepository("http://localhost:8529", "ignored", "root", "password", "test-model", 4, 1, http, new StubDomainEventDispatcher());
        var acc = new TenantContextAccessor();
        var recall = new ArangoRecallEngine(store, emb, new ArangoEntityExtractor(store), alpha: 0.7, beta: 0.3);
        var plugin = new MemoryPlugin(store, acc, recall);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => plugin.RememberDecision("s", "c", "b", Array.Empty<string>()));
    }

    [Fact]
    public async Task Recall_ReturnsItemsScopedToTenantOnly()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStoreWriteTests.ConstantEmbeddingClient(4);
            var store = new ArangoMemoryRepository(MemoryStoreSchemaTests.ArangoUrl, dbName,
                MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
                "test-model", 4, 1, http, new StubDomainEventDispatcher(), emb);
            await store.EnsureSchemaAsync();

            await store.UpsertDecisionAsync(new TenantId("admin"), "policy", "x", "admin-only secret policy", Array.Empty<string>());
            await store.UpsertDecisionAsync(new TenantId("kid:a"), "snack", "x", "kid-only snack note", Array.Empty<string>());

            var recall = new ArangoRecallEngine(store, emb, new ArangoEntityExtractor(store), alpha: 0.7, beta: 0.3);
            var acc = new TenantContextAccessor { Current = TenantContext.ForKid("a") };
            var plugin = new MemoryPlugin(store, acc, recall);

            var json = await plugin.Recall("anything", topK: 8, expandHops: 1);

            Assert.Contains("kid-only", json);
            Assert.DoesNotContain("admin-only", json);
        }
        finally { await MemoryStoreSchemaTests.DropDb(dbName); }
    }
}
