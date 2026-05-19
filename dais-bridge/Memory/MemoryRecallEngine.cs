using Darbee.Gateway.Memory.Models;

namespace Darbee.Gateway.Memory;

/// <summary>
/// Phase 11 C — hybrid recall engine.
/// Combines explicit graph expansion (via memory_entities + memory_edges) with
/// approximate-vector top-K and a weighted score: alpha * cosine + beta * proximity.
///
/// Phase C ships substring/alias entity matching only; Phase D wires an NER fallback.
/// Tenant isolation is the caller's responsibility — every method takes a tenantId
/// and forwards it into AQL FILTER clauses.
/// </summary>
public sealed class MemoryRecallEngine
{
    private readonly MemoryStore _store;
    private readonly IEmbeddingClient _embeddings;
    private readonly double _alpha;
    private readonly double _beta;
    private readonly Func<string, Task<IReadOnlyList<string>>>? _nerFallback;

    public MemoryRecallEngine(
        MemoryStore store,
        IEmbeddingClient embeddings,
        double alpha,
        double beta,
        Func<string, Task<IReadOnlyList<string>>>? nerFallback = null)
    {
        _store = store;
        _embeddings = embeddings;
        _alpha = alpha;
        _beta = beta;
        _nerFallback = nerFallback;
    }

    /// <summary>
    /// Substring + alias match against memory_entities for the given tenant.
    /// If nothing matches and an NER fallback is configured, returns the fallback's result;
    /// otherwise returns an empty list.
    /// </summary>
    public async Task<IReadOnlyList<string>> ExtractEntitiesAsync(
        string tenantId, string query, CancellationToken ct = default)
    {
        var aql = @"FOR e IN @@col
                      FILTER e.tenant_id == @tenantId
                      LET hit = CONTAINS(LOWER(@query), LOWER(e.canonical_name))
                                OR LENGTH(FOR a IN (e.aliases != null ? e.aliases : [])
                                            FILTER CONTAINS(LOWER(@query), LOWER(a))
                                            RETURN 1) > 0
                      FILTER hit
                      RETURN e._id";
        var bindVars = new Dictionary<string, object>
        {
            ["@col"] = MemoryCollections.Entities,
            ["tenantId"] = tenantId,
            ["query"] = query,
        };
        var ids = await _store.QueryAsync<string>(aql, bindVars, ct);
        if (ids.Count > 0 || _nerFallback is null) return ids;
        return await _nerFallback(query);
    }

    public sealed record GraphCandidate(
        MemoryItem Item,
        int HopsFromQuery,
        IReadOnlyList<string> PathEntityKeys);

    /// <summary>
    /// Walk 1..expandHops ANY edges from each seed entity, return DISTINCT non-entity vertices
    /// scoped to the tenant. Edges are filtered by tenant_id too via the vertex filter.
    /// </summary>
    public async Task<IReadOnlyList<GraphCandidate>> GraphExpandAsync(
        string tenantId, IReadOnlyList<string> entityIds, int expandHops, CancellationToken ct = default)
    {
        if (entityIds.Count == 0) return Array.Empty<GraphCandidate>();

        // PARSE_IDENTIFIER returns { collection, key } — use it to skip entity vertices.
        // Project a flat shape so ArangoDB's Newtonsoft.Json client can deserialize cleanly.
        var aql = @"FOR e IN @entityIds
                      FOR v, edge, p IN 1..@hops ANY e @@edges
                        FILTER v.tenant_id == @tenantId
                        FILTER PARSE_IDENTIFIER(v._id).collection != @entitiesCol
                        RETURN DISTINCT {
                          id:         v._id,
                          key:        v._key,
                          text:       v.text != null ? v.text : '',
                          tenant_id:  v.tenant_id,
                          status:     v.status != null ? v.status : 'ready',
                          created_at: v.created_at,
                          updated_at: v.updated_at,
                          hops:       LENGTH(p.edges),
                          path:       p.vertices[*]._id
                        }";
        var bindVars = new Dictionary<string, object>
        {
            ["entityIds"] = entityIds.ToArray(),
            ["hops"] = expandHops,
            ["tenantId"] = tenantId,
            ["@edges"] = MemoryCollections.Edges,
            ["entitiesCol"] = MemoryCollections.Entities,
        };

        var rows = await _store.QueryAsync<GraphRow>(aql, bindVars, ct);
        return rows.Select(r => new GraphCandidate(
            Item: MaterializeItem(r.id, r.key, r.text, r.tenant_id, r.status, r.created_at, r.updated_at, embedding: null),
            HopsFromQuery: r.hops,
            PathEntityKeys: r.path ?? new List<string>())).ToList();
    }

    private sealed record GraphRow(
        string? id,
        string? key,
        string? text,
        string? tenant_id,
        string? status,
        string? created_at,
        string? updated_at,
        int hops,
        List<string>? path);

    private static MemoryItem MaterializeItem(
        string? id, string? key, string? text, string? tenantId, string? status,
        string? createdAt, string? updatedAt, float[]? embedding)
    {
        var col = !string.IsNullOrEmpty(id) && id.Contains('/') ? id.Split('/')[0] : "";
        var kind = col switch
        {
            MemoryCollections.Decisions => MemoryKind.Decision,
            MemoryCollections.Observations => MemoryKind.Observation,
            MemoryCollections.Facts => MemoryKind.Fact,
            MemoryCollections.Summaries => MemoryKind.Summary,
            MemoryCollections.Entities => MemoryKind.Entity,
            MemoryCollections.Posts => MemoryKind.Post,
            _ => throw new InvalidOperationException($"Unknown collection '{col}' in recall result")
        };

        DateTime ParseDate(string? s) =>
            string.IsNullOrEmpty(s) ? default :
            DateTime.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind);

        return new MemoryItem(
            Key: key ?? "",
            Kind: kind,
            Text: text ?? "",
            Embedding: embedding,
            TenantId: tenantId ?? "",
            Status: status ?? "ready",
            CreatedAt: ParseDate(createdAt),
            UpdatedAt: ParseDate(updatedAt));
    }

    public sealed record VectorCandidate(MemoryItem Item, double Similarity);

    /// <summary>
    /// Top-K over content collections (decisions, observations, facts, summaries)
    /// scoped to the tenant. Per-collection fan-out + in-memory merge.
    ///
    /// We deliberately use COSINE_SIMILARITY (exact) instead of APPROX_NEAR_COSINE.
    /// Rationale: ArangoDB 3.12's APPROX_NEAR_COSINE does NOT support pre-filtering
    /// (see https://github.com/arangodb/arangodb/issues/21690 — only post-filter is
    /// allowed, so LIMIT is applied BEFORE the tenant filter). That makes tenant
    /// isolation impossible to guarantee with the approximate path: a tenant with
    /// many docs starves other tenants' results. Pre-filter pushdown lands in
    /// ArangoDB 4.0; until then exact is the rock-solid choice.
    ///
    /// Performance on our workload (≤ 10k vectors/tenant, 768-dim embeddings) is
    /// sub-millisecond on AVX-512 (Ryzen AI Max+ 395). The sparse Faiss IVF index
    /// is still built on each collection (see MemoryStore.EnsureVectorIndexAsync)
    /// and stands ready for APPROX_NEAR_COSINE the day pre-filter pushdown lands.
    /// </summary>
    public async Task<IReadOnlyList<VectorCandidate>> VectorTopKAsync(
        string tenantId, float[] queryEmbedding, int limit, CancellationToken ct = default)
    {
        var contentCols = new[]
        {
            MemoryCollections.Decisions,
            MemoryCollections.Observations,
            MemoryCollections.Facts,
            MemoryCollections.Summaries,
        };

        var all = new List<VectorCandidate>(capacity: contentCols.Length * limit);
        foreach (var collection in contentCols)
        {
            var aql = @"FOR doc IN @@col
                          FILTER doc.tenant_id == @tenantId
                          FILTER doc.status == 'ready'
                          LET sim = COSINE_SIMILARITY(doc.embedding, @queryEmb)
                          SORT sim DESC
                          LIMIT @limit
                          RETURN {
                            id:         doc._id,
                            key:        doc._key,
                            text:       doc.text != null ? doc.text : '',
                            tenant_id:  doc.tenant_id,
                            status:     doc.status,
                            created_at: doc.created_at,
                            updated_at: doc.updated_at,
                            embedding:  doc.embedding,
                            similarity: sim
                          }";
            var bindVars = new Dictionary<string, object>
            {
                ["@col"] = collection,
                ["tenantId"] = tenantId,
                ["queryEmb"] = queryEmbedding,
                ["limit"] = limit,
            };
            var rows = await _store.QueryAsync<VectorRow>(aql, bindVars, ct);
            all.AddRange(rows.Select(r => new VectorCandidate(
                MaterializeItem(r.id, r.key, r.text, r.tenant_id, r.status, r.created_at, r.updated_at, r.embedding),
                r.similarity)));
        }

        return all
            .OrderByDescending(c => c.Similarity)
            .Take(limit)
            .ToList();
    }

    private sealed record VectorRow(
        string? id,
        string? key,
        string? text,
        string? tenant_id,
        string? status,
        string? created_at,
        string? updated_at,
        float[]? embedding,
        double similarity);

    /// <summary>
    /// Hybrid recall: extract entities → graph expand from those entities →
    /// vector top-K → merge by composite (kind, key) preferring graph candidates →
    /// score = alpha * cosine + beta * proximity, descending.
    /// If embedding fails, returns graph-only with cosine=0 and score=beta*proximity.
    /// </summary>
    public async Task<RecallResult> RecallAsync(
        string tenantId,
        string query,
        int topK = 8,
        int expandHops = 1,
        CancellationToken ct = default)
    {
        var entityIds = await ExtractEntitiesAsync(tenantId, query, ct);
        var graphCandidates = await GraphExpandAsync(tenantId, entityIds, expandHops, ct);

        float[] queryEmb;
        try
        {
            queryEmb = await _embeddings.EmbedAsync(query, ct);
        }
        catch
        {
            // Embedding service down: graph-only fallback (cosine=0, score driven by proximity).
            var graphOnly = graphCandidates
                .Select(c => new ScoredMemoryItem(
                    Item: c.Item,
                    Cosine: 0,
                    Proximity: 1.0 / (1 + c.HopsFromQuery),
                    Score: _beta * (1.0 / (1 + c.HopsFromQuery)),
                    HopsFromQuery: c.HopsFromQuery,
                    PathEntityKeys: c.PathEntityKeys))
                .OrderByDescending(s => s.Score)
                .Take(topK)
                .ToList();
            return new RecallResult(graphOnly, entityIds);
        }

        var vectorCandidates = await VectorTopKAsync(tenantId, queryEmb, 2 * topK, ct);

        var merged = new Dictionary<string, ScoredMemoryItem>();
        foreach (var c in graphCandidates)
        {
            var key = $"{c.Item.Kind}:{c.Item.Key}";
            var cosine = CosineSimilarity(queryEmb, c.Item.Embedding);
            var proximity = 1.0 / (1 + c.HopsFromQuery);
            merged[key] = new ScoredMemoryItem(
                Item: c.Item,
                Cosine: cosine,
                Proximity: proximity,
                Score: _alpha * cosine + _beta * proximity,
                HopsFromQuery: c.HopsFromQuery,
                PathEntityKeys: c.PathEntityKeys);
        }
        foreach (var v in vectorCandidates)
        {
            var key = $"{v.Item.Kind}:{v.Item.Key}";
            if (merged.ContainsKey(key)) continue;
            merged[key] = new ScoredMemoryItem(
                Item: v.Item,
                Cosine: v.Similarity,
                Proximity: 0.0,
                Score: _alpha * v.Similarity,
                HopsFromQuery: null,
                PathEntityKeys: Array.Empty<string>());
        }

        var ordered = merged.Values
            .OrderByDescending(s => s.Score)
            .Take(topK)
            .ToList();
        return new RecallResult(ordered, entityIds);
    }

    private static double CosineSimilarity(float[] a, float[]? b)
    {
        if (b is null || b.Length != a.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        if (na == 0 || nb == 0) return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
