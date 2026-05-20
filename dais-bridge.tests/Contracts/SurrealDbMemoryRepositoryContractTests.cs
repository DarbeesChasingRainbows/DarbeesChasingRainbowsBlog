using Darbee.Gateway.Domain.Ports;
using Darbee.Gateway.Infrastructure.SurrealDb;
using Darbee.Gateway.Tests.Memory;
using Microsoft.Extensions.DependencyInjection;
using SurrealDb.Net;
using SurrealDb.Net.Models.Auth;

namespace Darbee.Gateway.Tests.Contracts;

/// <summary>
/// LSP enforcement: prove that <see cref="SurrealDbMemoryRepository"/> satisfies
/// the same contract as the ArangoDB adapter. Same 9 facts as the base class run
/// against SurrealDB — substitutability guaranteed if green.
/// </summary>
public sealed class SurrealDbMemoryRepositoryContractTests : MemoryRepositoryContractTests
{
    private static string SurrealUrl =>
        Environment.GetEnvironmentVariable("SURREAL_URL") ?? "http://localhost:8000";
    private static string SurrealUser =>
        Environment.GetEnvironmentVariable("SURREAL_USER") ?? "root";
    private static string SurrealPass =>
        Environment.GetEnvironmentVariable("SURREAL_PASS") ?? "password";
    private static bool SurrealEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("SURREAL_TEST_RUN"), "1", StringComparison.Ordinal);

    private ISurrealDbClient? _client;
    private string? _dbName;

    protected override async Task<(IMemoryRepository repo, IEmbeddingClient emb)?> CreateRepositoryAsync()
    {
        if (!SurrealEnabled) return null;

        // Per-test database (unique). Namespace stays constant ("darbees-test").
        _dbName = $"test_{Guid.NewGuid():N}";

        var options = SurrealDbOptions
            .Create()
            .WithEndpoint(SurrealUrl)
            .WithNamespace("darbees-test")
            .WithDatabase(_dbName)
            .WithUsername(SurrealUser)
            .WithPassword(SurrealPass)
            .Build();

        _client = new SurrealDbClient(options);
        await _client.Connect();
        await _client.SignIn(new RootAuth { Username = SurrealUser, Password = SurrealPass });
        await _client.Use("darbees-test", _dbName);

        var emb = new MemoryStoreNotesTests.StubEmbeddingClient();
        var dispatcher = new StubDomainEventDispatcher();
        var repo = new SurrealDbMemoryRepository(
            _client,
            embeddingModelId: "test-model",
            embeddingDimension: 4,
            embeddings: emb,
            dispatcher: dispatcher);

        // Force schema bootstrap so the contract tests' first operation doesn't race the lazy init.
        await repo.EnsureSchemaAsync();

        return (repo, emb);
    }

    protected override async Task TeardownAsync()
    {
        if (_client is null) return;

        // Drop the test database. SurrealDB: `REMOVE DATABASE IF EXISTS test_xxx;`
        try
        {
            await _client.RawQuery($"REMOVE DATABASE IF EXISTS `{_dbName}`;");
        }
        catch
        {
            // Best-effort cleanup; if drop fails the next test will use a different name anyway.
        }

        // ISurrealDbClient is IDisposable / IAsyncDisposable.
        if (_client is IAsyncDisposable ad) await ad.DisposeAsync();
        else (_client as IDisposable)?.Dispose();
        _client = null;
        _dbName = null;
    }
}
