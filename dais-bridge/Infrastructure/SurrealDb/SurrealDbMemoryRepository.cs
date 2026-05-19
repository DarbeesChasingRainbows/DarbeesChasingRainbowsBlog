using System.Text.Json;
using Darbee.Gateway.Domain.Models;
using Darbee.Gateway.Domain.Ports;
using SurrealDb.Net;

namespace Darbee.Gateway.Infrastructure.SurrealDb;

/// <summary>
/// SurrealDB adapter for the memory persistence ports.
/// Implements <see cref="IMemoryRepository"/>, <see cref="ISchemaManager"/>, <see cref="IEmbeddingMigrator"/>.
/// One concrete class behind multiple ports is fine per ISP — consumers depend only on the interface they need.
/// </summary>
public sealed class SurrealDbMemoryRepository : IMemoryRepository, ISchemaManager, IEmbeddingMigrator, IDisposable
{
    private readonly ISurrealDbClient _surreal;
    private readonly string _embeddingModelId;
    private readonly int _embeddingDimension;
    private readonly IEmbeddingClient _embeddings;

    // ----- lazy schema state (double-checked locking, mirrors MemoryStore) -----
    private volatile bool _schemaReady;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private Exception? _schemaError;

    public SurrealDbMemoryRepository(
        ISurrealDbClient surreal,
        string embeddingModelId,
        int embeddingDimension,
        IEmbeddingClient embeddings)
    {
        _surreal = surreal;
        _embeddingModelId = embeddingModelId;
        _embeddingDimension = embeddingDimension;
        _embeddings = embeddings;
    }

    // ----- ISchemaManager -----

    /// <summary>
    /// Issues a single batched SurrealQL DDL statement that creates all tables and indexes
    /// idempotently. Safe to call multiple times — every statement uses IF NOT EXISTS.
    /// </summary>
    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        var ddl = $@"
DEFINE TABLE IF NOT EXISTS memory_posts SCHEMALESS;
DEFINE TABLE IF NOT EXISTS memory_observations SCHEMALESS;
DEFINE TABLE IF NOT EXISTS memory_facts SCHEMALESS;
DEFINE TABLE IF NOT EXISTS memory_decisions SCHEMALESS;
DEFINE TABLE IF NOT EXISTS memory_summaries SCHEMALESS;
DEFINE TABLE IF NOT EXISTS memory_entities SCHEMALESS;
DEFINE TABLE IF NOT EXISTS memory_edges TYPE RELATION SCHEMALESS;
DEFINE TABLE IF NOT EXISTS memory_pending_embeddings SCHEMALESS;
DEFINE TABLE IF NOT EXISTS memory_meta SCHEMALESS;

DEFINE INDEX IF NOT EXISTS idx_posts_tenant ON TABLE memory_posts FIELDS tenant_id, status, vector_kind;
DEFINE INDEX IF NOT EXISTS idx_observations_tenant ON TABLE memory_observations FIELDS tenant_id, status, created_at;
DEFINE INDEX IF NOT EXISTS idx_facts_tenant ON TABLE memory_facts FIELDS tenant_id, status, created_at;
DEFINE INDEX IF NOT EXISTS idx_decisions_tenant ON TABLE memory_decisions FIELDS tenant_id, status, created_at;
DEFINE INDEX IF NOT EXISTS idx_summaries_tenant ON TABLE memory_summaries FIELDS tenant_id, status, created_at;
DEFINE INDEX IF NOT EXISTS idx_entities_tenant ON TABLE memory_entities FIELDS tenant_id, canonical_name;
DEFINE INDEX IF NOT EXISTS idx_entities_aliases ON TABLE memory_entities FIELDS tenant_id, aliases;
DEFINE INDEX IF NOT EXISTS idx_edges_tenant ON TABLE memory_edges FIELDS tenant_id, kind;

DEFINE INDEX IF NOT EXISTS idx_posts_embed ON TABLE memory_posts FIELDS embedding HNSW DIMENSION {_embeddingDimension} DIST COSINE;
DEFINE INDEX IF NOT EXISTS idx_observations_embed ON TABLE memory_observations FIELDS embedding HNSW DIMENSION {_embeddingDimension} DIST COSINE;
DEFINE INDEX IF NOT EXISTS idx_facts_embed ON TABLE memory_facts FIELDS embedding HNSW DIMENSION {_embeddingDimension} DIST COSINE;
DEFINE INDEX IF NOT EXISTS idx_decisions_embed ON TABLE memory_decisions FIELDS embedding HNSW DIMENSION {_embeddingDimension} DIST COSINE;
DEFINE INDEX IF NOT EXISTS idx_summaries_embed ON TABLE memory_summaries FIELDS embedding HNSW DIMENSION {_embeddingDimension} DIST COSINE;
";
        await _surreal.RawQuery(ddl, null, ct);
    }

    /// <summary>
    /// Lazily runs <see cref="EnsureSchemaAsync"/> once per process lifetime.
    /// Uses double-checked locking and caches any failure so repeated calls surface it immediately.
    /// </summary>
    public async Task EnsureSchemaIfNeededAsync(CancellationToken ct = default)
    {
        if (_schemaReady) return;

        await _schemaLock.WaitAsync(ct);
        try
        {
            if (_schemaReady) return;
            if (_schemaError is not null) throw _schemaError;

            try
            {
                await EnsureSchemaAsync(ct);
                _schemaReady = true;
            }
            catch (Exception ex)
            {
                _schemaError = ex;
                throw;
            }
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    /// <summary>
    /// HNSW indexes are created by <see cref="EnsureSchemaAsync"/> as part of the DDL batch.
    /// The per-collection method exists for parity with the ArangoDB adapter (which has
    /// lazy/threshold-gated vector index creation). SurrealDB creates the index upfront,
    /// so this delegates to <see cref="EnsureSchemaIfNeededAsync"/> and is otherwise a no-op.
    /// </summary>
    public Task EnsureVectorIndexAsync(string collection, CancellationToken ct = default)
        => EnsureSchemaIfNeededAsync(ct);

    /// <summary>
    /// In SurrealDB, HNSW is always usable immediately after the DEFINE INDEX statement completes;
    /// there is no async build phase visible to the caller. Schema-ready implies vector-ready.
    /// </summary>
    public Task<bool> IsVectorIndexReadyAsync(string collection, CancellationToken ct = default)
        => Task.FromResult(_schemaReady);

    /// <summary>
    /// Returns the names of all tables in the current database via INFO FOR DB.
    /// </summary>
    public async Task<List<string>> ListCollectionsAsync(CancellationToken ct = default)
    {
        var resp = await _surreal.RawQuery("INFO FOR DB;", null, ct);
        var first = resp.GetValue<JsonElement>(0);
        var tables = new List<string>();
        if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("tables", out var tablesEl))
        {
            foreach (var p in tablesEl.EnumerateObject())
                tables.Add(p.Name);
        }
        return tables;
    }

    // ----- IMemoryRepository: writes -----
    public Task<UpsertPostResult> UpsertPostAsync(PostDocument post, bool force, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T6: UpsertPost.");
    public Task<UpsertNoteResult> UpsertNoteAsync(NoteDocument note, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T6: UpsertNote.");
    public Task<WriteResult> UpsertDecisionAsync(string tenantId, string subject, string chose, string because, IReadOnlyList<string> alternatives, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T9: UpsertDecision.");
    public Task<WriteResult> UpsertObservationAsync(string tenantId, string source, string text, object payload, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T9: UpsertObservation.");
    public Task<WriteResult> UpsertFactAsync(string tenantId, string text, string? sourceThread, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T9: UpsertFact.");
    public Task<WriteResult> UpsertSummaryAsync(string tenantId, string text, string threadId, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T9: UpsertSummary.");
    public Task<string> UpsertEntityAsync(string tenantId, string canonicalName, IReadOnlyList<string> aliases, string type, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T9: UpsertEntity.");
    public Task<string> UpsertEdgeAsync(string tenantId, string fromId, string toId, string kind, double weight, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T9: UpsertEdge.");

    // ----- IMemoryRepository: reads -----
    public Task<List<PostSearchHit>> SearchAsync(float[] queryVec, IReadOnlyList<MemoryKind> kinds, IReadOnlyList<string> tenants, int k, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T7: Search with KNN.");
    public Task<string?> ReadPostHashAsync(string key, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T9: ReadPostHash.");
    public Task<JsonDocument?> ReadNoteDocumentAsync(string collection, string noteKey, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T9: ReadNoteDocument.");
    public Task<List<(string id, string targetCollection, string targetKey)>> ListPendingEmbeddingsAsync(int limit = 100, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T9: ListPendingEmbeddings.");

    // ----- IMemoryRepository: deletes -----
    public Task<int> DeleteStalePostsAsync(IReadOnlyCollection<(string Collection, string Slug)> currentPosts, IReadOnlyCollection<string>? scopedCollections = null, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T8: DeleteStalePosts.");
    public Task<int> DeleteStaleNotesAsync(IReadOnlyList<string> currentKeys, string tenant, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T8: DeleteStaleNotes.");

    // ----- IMemoryRepository: query -----
    public Task<List<T>> QueryAsync<T>(string query, Dictionary<string, object> bindVars, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T11: raw QueryAsync for recall engine.");

    // ----- IEmbeddingMigrator -----
    public Task<MigrationResult> MigrateEmbeddingsAsync(string confirmToken, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T10: MigrateEmbeddings.");
    public Task<EmbeddingConfig?> ReadEmbeddingConfigAsync(CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T10: ReadEmbeddingConfig.");

    public void Dispose()
    {
        _schemaLock.Dispose();
        // ISurrealDbClient is managed by DI; the client itself is not disposed here.
    }
}
