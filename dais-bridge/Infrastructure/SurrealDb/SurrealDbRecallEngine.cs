using Darbee.Gateway.Domain.Models;
using Darbee.Gateway.Domain.Ports;
using Darbee.Gateway.Domain.ValueObjects;

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

    public async Task<IReadOnlyList<string>> ExtractEntitiesAsync(
        TenantId tenantId, string query, CancellationToken ct = default)
    {
        return await _entityExtractor.ExtractAsync(tenantId, query, ct);
    }

    public async Task<RecallResult> RecallAsync(
        TenantId tenantId, string query, int topK = 8, int expandHops = 1, CancellationToken ct = default)
    {
        // Step 1: extract matching entities from the query text.
        var entityIds = await ExtractEntitiesAsync(tenantId, query, ct);

        // Step 2: embed the query for vector top-K below.
        var queryVec = await _embeddings.EmbedAsync(query, ct);

        // Step 3: 1-hop graph expansion — from matched entities, collect connected content ids.
        // SurrealDB arrow-traversal: record->edge_table->target_table.
        // Multi-hop expansion is future work; Phase 2 ships 1-hop only.
        var connectedIds = new HashSet<string>();
        if (entityIds.Count > 0)
        {
            // Traverse 1 hop through memory_edges to all four content collections.
            var hopSql = @"
SELECT VALUE ->memory_edges->(memory_decisions, memory_observations, memory_facts, memory_summaries).id
FROM $startIds;";

            var rows = await _repository.QueryAsync<List<string>>(
                hopSql,
                new Dictionary<string, object> { ["startIds"] = entityIds },
                ct);

            foreach (var row in rows)
                if (row is not null)
                    foreach (var rid in row)
                        connectedIds.Add(rid);
        }

        // Step 4: vector top-K across all content collections, filtered by tenant.
        // Fetch 2× topK candidates so the re-ranking step has room to work.
        var hits = await _repository.SearchAsync(
            queryVec,
            new[]
            {
                MemoryKind.Decision,
                MemoryKind.Observation,
                MemoryKind.Fact,
                MemoryKind.Summary,
                MemoryKind.Post,
            },
            new[] { tenantId, new TenantId("public") },
            k: Math.Max(topK * 2, 16),
            ct);

        // Step 5: combine scores — alpha * cosine + beta * proximity (1 if graph-connected, 0 otherwise).
        var scored = hits.Select(h =>
        {
            // SurrealDB record ids look like "memory_decisions:abc123".
            // Check both the full record id and the bare key part against connectedIds.
            var fullId = $"{h.Collection}:{h.Key}";
            var proximity = connectedIds.Contains(fullId) || connectedIds.Contains(h.Key) ? 1.0 : 0.0;
            var cosine = h.Sim;
            var combined = _alpha * cosine + _beta * proximity;

            // Resolve kind from the hit's collection string; fall back to Post.
            var kind = h.Collection switch
            {
                MemoryCollections.Decisions    => MemoryKind.Decision,
                MemoryCollections.Observations => MemoryKind.Observation,
                MemoryCollections.Facts        => MemoryKind.Fact,
                MemoryCollections.Summaries    => MemoryKind.Summary,
                MemoryCollections.Posts        => MemoryKind.Post,
                _                              => MemoryKind.Post,
            };

            var memItem = new MemoryItem(
                Key:       h.Key,
                Kind:      kind,
                Text:      h.Text,
                Embedding: null,
                TenantId:  h.TenantId ?? tenantId,
                Status:    "active",
                CreatedAt: DateTime.UtcNow,
                UpdatedAt: DateTime.UtcNow,
                Metadata:  null);

            return new ScoredMemoryItem(
                Item:            memItem,
                Cosine:          cosine,
                Proximity:       proximity,
                Score:           combined,
                HopsFromQuery:   proximity > 0 ? 1 : null,
                PathEntityKeys:  Array.Empty<string>());
        })
        .OrderByDescending(s => s.Score)
        .Take(topK)
        .ToList();

        return new RecallResult(Items: scored, ExtractedEntityIds: entityIds);
    }
}
