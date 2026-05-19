using System.Net.Http;
using Darbee.Gateway.Domain.Ports;
using Darbee.Gateway.Memory;
using Darbee.Gateway.Tests.Memory;

namespace Darbee.Gateway.Tests.Contracts;

/// <summary>
/// LSP enforcement: prove that <see cref="MemoryStore"/> (the ArangoDB adapter)
/// satisfies the IMemoryRepository contract. Same suite runs against
/// SurrealDbMemoryRepository in Phase 12.
/// </summary>
public sealed class ArangoMemoryRepositoryContractTests : MemoryRepositoryContractTests
{
    private string? _dbName;
    private HttpClient? _http;

    protected override async Task<(IMemoryRepository repo, IEmbeddingClient emb)?> CreateRepositoryAsync()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return null;

        _dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        _http = new HttpClient();
        var emb = new MemoryStoreNotesTests.StubEmbeddingClient();
        var store = new MemoryStore(
            MemoryStoreSchemaTests.ArangoUrl,
            _dbName,
            MemoryStoreSchemaTests.ArangoUser,
            MemoryStoreSchemaTests.ArangoPass,
            embeddingModelId: "test-model",
            embeddingDimension: 4,
            vectorNLists: 1,
            _http,
            emb);
        return (store, emb);
    }

    protected override async Task TeardownAsync()
    {
        if (_dbName != null)
        {
            await MemoryStoreSchemaTests.DropDb(_dbName);
            _dbName = null;
        }
        _http?.Dispose();
        _http = null;
    }
}
