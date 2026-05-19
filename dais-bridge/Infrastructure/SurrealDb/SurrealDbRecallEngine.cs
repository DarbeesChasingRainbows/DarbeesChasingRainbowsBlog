using Darbee.Gateway.Domain.Models;
using Darbee.Gateway.Domain.Ports;

namespace Darbee.Gateway.Infrastructure.SurrealDb;

/// <summary>
/// SurrealDB-backed implementation of the hybrid recall engine.
/// Uses <see cref="IMemoryRepository"/> for data access; never talks to the DB client directly.
/// </summary>
public sealed class SurrealDbRecallEngine : IRecallEngine
{
    private readonly IMemoryRepository _repository;
    private readonly IEmbeddingClient _embeddings;
    private readonly IEntityExtractor _entityExtractor;
    private readonly double _alpha;
    private readonly double _beta;

    public SurrealDbRecallEngine(
        IMemoryRepository repository,
        IEmbeddingClient embeddings,
        IEntityExtractor entityExtractor,
        double alpha = 0.6,
        double beta = 0.4)
    {
        _repository = repository;
        _embeddings = embeddings;
        _entityExtractor = entityExtractor;
        _alpha = alpha;
        _beta = beta;
    }

    public Task<RecallResult> RecallAsync(string tenantId, string query, int topK = 8, int expandHops = 1, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T11: hybrid recall.");

    public Task<IReadOnlyList<string>> ExtractEntitiesAsync(string tenantId, string query, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T11: entity extraction.");
}
