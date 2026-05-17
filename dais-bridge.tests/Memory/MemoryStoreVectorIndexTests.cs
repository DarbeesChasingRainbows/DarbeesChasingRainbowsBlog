using System.Net.Http;
using System.Net.Http.Json;
using Darbee.Gateway.Memory;

namespace Darbee.Gateway.Tests.Memory;

[Trait("Category", "Integration")]
public class MemoryStoreVectorIndexTests
{
    private static async Task InsertDocAsync(HttpClient http, string baseUrl, string db, string collection, string user, string pass, int dim)
    {
        var url = $"{baseUrl}/_db/{db}/_api/document/{collection}";
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{user}:{pass}")));
        var json = System.Text.Json.JsonSerializer.Serialize(new { embedding = Enumerable.Repeat(0.1f, dim).ToArray() });
        request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        (await http.SendAsync(request)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task EnsureVectorIndexAsync_NoOps_WhenCollectionHasFewerDocsThanNLists()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var store = new MemoryStore(
                MemoryStoreSchemaTests.ArangoUrl, dbName,
                MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
                "test-embed-model", embeddingDimension: 768, vectorNLists: 5, http);
            await store.EnsureSchemaAsync();

            await store.EnsureVectorIndexAsync("memory_decisions");

            Assert.False(await store.HasUsableVectorIndexAsync("memory_decisions"));
        }
        finally { await MemoryStoreSchemaTests.DropDb(dbName); }
    }

    [Fact]
    public async Task EnsureVectorIndexAsync_CreatesUsableIndex_WhenDocsMeetThreshold()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var store = new MemoryStore(
                MemoryStoreSchemaTests.ArangoUrl, dbName,
                MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
                "test-embed-model", embeddingDimension: 768, vectorNLists: 1, http);
            await store.EnsureSchemaAsync();

            await InsertDocAsync(http, MemoryStoreSchemaTests.ArangoUrl, dbName, "memory_decisions",
                MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass, 768);

            await store.EnsureVectorIndexAsync("memory_decisions");

            Assert.True(await store.HasUsableVectorIndexAsync("memory_decisions"));
        }
        finally { await MemoryStoreSchemaTests.DropDb(dbName); }
    }

    [Fact]
    public async Task EnsureVectorIndexAsync_CleansUpUnusableIndexes_BeforeRetrying()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var store = new MemoryStore(
                MemoryStoreSchemaTests.ArangoUrl, dbName,
                MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
                "test-embed-model", embeddingDimension: 768, vectorNLists: 1, http);
            await store.EnsureSchemaAsync();

            await store.EnsureVectorIndexAsync("memory_decisions");
            Assert.False(await store.HasUsableVectorIndexAsync("memory_decisions"));

            await InsertDocAsync(http, MemoryStoreSchemaTests.ArangoUrl, dbName, "memory_decisions",
                MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass, 768);
            await store.EnsureVectorIndexAsync("memory_decisions");

            Assert.True(await store.HasUsableVectorIndexAsync("memory_decisions"));
            Assert.Equal(1, await store.CountVectorIndexesAsync("memory_decisions"));
        }
        finally { await MemoryStoreSchemaTests.DropDb(dbName); }
    }
}
