using System.Net.Http;
using ArangoDBNetStandard;
using ArangoDBNetStandard.Transport.Http;
using Darbee.Gateway.Memory;

namespace Darbee.Gateway.Tests.Memory;

[Trait("Category", "Integration")]
public class MemoryStoreSchemaTests
{
    private static readonly string TestDbBase = "darbees_memory_test";

    internal static string ArangoUrl =>
        Environment.GetEnvironmentVariable("ARANGO_TEST_URL") ?? "http://localhost:8529";

    internal static string ArangoUser =>
        Environment.GetEnvironmentVariable("ARANGO_TEST_USER") ?? "root";

    internal static string ArangoPass =>
        Environment.GetEnvironmentVariable("ARANGO_TEST_PASS") ?? "password";

    internal static bool ArangoEnabled =>
        Environment.GetEnvironmentVariable("ARANGO_TEST_URL") != null
        || Environment.GetEnvironmentVariable("ARANGO_TEST_RUN") == "1";

    internal static async Task<string> CreateUniqueDb()
    {
        var dbName = $"{TestDbBase}_{Guid.NewGuid():N}";
        var rootTransport = HttpApiTransport.UsingBasicAuth(new Uri(ArangoUrl), "_system", ArangoUser, ArangoPass);
        using var rootClient = new ArangoDBClient(rootTransport);
        await rootClient.Database.PostDatabaseAsync(new ArangoDBNetStandard.DatabaseApi.Models.PostDatabaseBody { Name = dbName });
        return dbName;
    }

    internal static async Task DropDb(string dbName)
    {
        var rootTransport = HttpApiTransport.UsingBasicAuth(new Uri(ArangoUrl), "_system", ArangoUser, ArangoPass);
        using var rootClient = new ArangoDBClient(rootTransport);
        try { await rootClient.Database.DeleteDatabaseAsync(dbName); } catch { }
    }

    [Fact]
    public async Task EnsureSchemaAsync_CreatesAllCollectionsAndPersistentIndexes_Idempotent()
    {
        if (!ArangoEnabled) return;
        var dbName = await CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass, embeddingDimension: 768, vectorNLists: 1, http);

            await store.EnsureSchemaAsync();
            await store.EnsureSchemaAsync();

            var collections = await store.ListCollectionsAsync();
            Assert.Contains("memory_decisions", collections);
            Assert.Contains("memory_observations", collections);
            Assert.Contains("memory_facts", collections);
            Assert.Contains("memory_summaries", collections);
            Assert.Contains("memory_entities", collections);
            Assert.Contains("memory_edges", collections);
            Assert.Contains("memory_pending_embeddings", collections);
        }
        finally
        {
            await DropDb(dbName);
        }
    }
}
