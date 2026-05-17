using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;
using Darbee.Gateway.Models;
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
            var store = new MemoryStore(MemoryStoreSchemaTests.ArangoUrl, dbName,
                MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
                "test-model", 4, 1, http, emb);
            await store.EnsureSchemaAsync();

            var acc = new TenantContextAccessor { Current = TenantContext.Admin };
            var plugin = new MemoryPlugin(store, acc);

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
        var store = new MemoryStore("http://localhost:8529", "ignored", "root", "password", "test-model", 4, 1, http);
        var acc = new TenantContextAccessor();
        var plugin = new MemoryPlugin(store, acc);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => plugin.RememberDecision("s", "c", "b", Array.Empty<string>()));
    }
}
