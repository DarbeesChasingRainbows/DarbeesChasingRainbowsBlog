using System.Text.Json;
using Darbee.Gateway.Domain.Models;
using Darbee.Gateway.Domain.Ports;
using Darbee.Gateway.Domain.Services;
using Darbee.Gateway.Domain.ValueObjects;
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

    public async Task<UpsertPostResult> UpsertPostAsync(PostDocument post, bool force, CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);

        var summaryText = PostTextComposer.ComposeSummary(post);
        var bodyText = PostTextComposer.ComposeBody(post);

        var summary = await UpsertOnePostVectorAsync(post, "summary", summaryText, force, ct);
        var body = await UpsertOnePostVectorAsync(post, "body", bodyText, force, ct);

        return new UpsertPostResult(
            Slug: post.Slug,
            Collection: post.Collection,
            Summary: summary,
            Body: body);
    }

    private async Task<VectorWriteOutcome> UpsertOnePostVectorAsync(
        PostDocument post, string vectorKind, string text, bool force, CancellationToken ct)
    {
        var recordId = $"{post.Collection}__{post.Slug}__{vectorKind}";
        var hash = ContentHash.From(text, _embeddingModelId).Value;

        // Cache check: if existing doc has same hash AND status=ready AND !force, skip embed.
        if (!force)
        {
            var existingHash = await SelectScalarAsync<string>(
                "SELECT VALUE hash FROM type::thing($table, $id) WHERE status = 'ready'",
                new Dictionary<string, object?> { ["table"] = "memory_posts", ["id"] = recordId },
                ct);
            if (existingHash == hash)
                return VectorWriteOutcome.Cached;
        }

        float[] embedding;
        try
        {
            embedding = await _embeddings.EmbedAsync(text, ct);
        }
        catch
        {
            return VectorWriteOutcome.Failed;
        }
        if (embedding.Length != _embeddingDimension)
            throw new InvalidOperationException(
                $"Embedding dimension mismatch: expected {_embeddingDimension}, got {embedding.Length}");

        var now = DateTime.UtcNow.ToString("o");
        var doc = new Dictionary<string, object?>
        {
            ["collection"] = post.Collection,
            ["slug"] = post.Slug,
            ["title"] = post.Title,
            ["description"] = post.Description,
            ["ai_summary"] = post.AiSummary,
            ["vector_kind"] = vectorKind,
            ["tenant_id"] = "public",
            ["status"] = "ready",
            ["text"] = text,
            ["hash"] = hash,
            ["embedding"] = embedding,
            ["kind"] = "post",
            ["source"] = "blog",
            ["created_at"] = now,
            ["updated_at"] = now,
        };

        await _surreal.RawQuery(
            "UPSERT type::thing($table, $id) CONTENT $doc;",
            new Dictionary<string, object?>
            {
                ["table"] = "memory_posts",
                ["id"] = recordId,
                ["doc"] = doc,
            },
            ct);

        return VectorWriteOutcome.Embedded;
    }

    public async Task<UpsertNoteResult> UpsertNoteAsync(NoteDocument note, CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);

        var collection = MemoryCollections.ForKind(note.Kind);
        var recordId = ContentHash.Sha1Hex(note.Key);
        var hash = ContentHash.From(note.Text, _embeddingModelId).Value;

        var existingHash = await SelectScalarAsync<string>(
            "SELECT VALUE hash FROM type::thing($table, $id) WHERE status = 'ready'",
            new Dictionary<string, object?> { ["table"] = collection, ["id"] = recordId },
            ct);
        if (existingHash == hash)
            return new UpsertNoteResult(note.Key, VectorWriteOutcome.Cached);

        float[] embedding;
        try
        {
            embedding = await _embeddings.EmbedAsync(note.Text, ct);
        }
        catch (Exception ex)
        {
            return new UpsertNoteResult(note.Key, VectorWriteOutcome.Failed, ex.Message);
        }
        if (embedding.Length != _embeddingDimension)
            throw new InvalidOperationException(
                $"Embedding dimension mismatch: expected {_embeddingDimension}, got {embedding.Length}");

        // Read existing created_at to preserve it on overwrite.
        var existingCreatedAt = await SelectScalarAsync<string>(
            "SELECT VALUE created_at FROM type::thing($table, $id)",
            new Dictionary<string, object?> { ["table"] = collection, ["id"] = recordId },
            ct);

        var now = DateTime.UtcNow.ToString("o");
        var doc = new Dictionary<string, object?>
        {
            ["note_key"] = note.Key,
            ["tenant_id"] = note.TenantId,
            ["kind"] = note.Kind.ToString().ToLowerInvariant(),
            ["title"] = note.Title,
            ["text"] = note.Text,
            ["hash"] = hash,
            ["embedding"] = embedding,
            ["status"] = "ready",
            ["source"] = "obsidian",
            ["metadata"] = note.Metadata ?? new Dictionary<string, object>(),
            ["created_at"] = existingCreatedAt ?? now,
            ["updated_at"] = now,
        };

        await _surreal.RawQuery(
            "UPSERT type::thing($table, $id) CONTENT $doc;",
            new Dictionary<string, object?>
            {
                ["table"] = collection,
                ["id"] = recordId,
                ["doc"] = doc,
            },
            ct);

        return new UpsertNoteResult(note.Key, VectorWriteOutcome.Embedded);
    }

    private async Task<T?> SelectScalarAsync<T>(
        string surql,
        Dictionary<string, object?> parameters,
        CancellationToken ct)
    {
        var resp = await _surreal.RawQuery(surql, parameters, ct);
        // SELECT VALUE ... returns a flat array (no field wrapping). resp[0] is the first statement result.
        var first = resp.GetValue<List<T>>(0);
        if (first is null || first.Count == 0) return default;
        return first[0];
    }
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

    public async Task<List<PostSearchHit>> SearchAsync(
        float[] queryVec,
        IReadOnlyList<MemoryKind> kinds,
        IReadOnlyList<string> tenants,
        int k,
        CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);

        if (queryVec.Length != _embeddingDimension)
            throw new InvalidOperationException(
                $"Query vector dimension mismatch: expected {_embeddingDimension}, got {queryVec.Length}");

        if (kinds.Count == 0 || tenants.Count == 0 || k <= 0)
            return new List<PostSearchHit>();

        var allHits = new List<PostSearchHit>();

        foreach (var kind in kinds)
        {
            var collection = MemoryCollections.ForKind(kind);
            var hits = await SearchOneCollectionAsync(collection, kind, queryVec, tenants, k, ct);
            allHits.AddRange(hits);
        }

        // Final top-K by similarity DESC across the union.
        return allHits.OrderByDescending(h => h.Sim).Take(k).ToList();
    }

    private async Task<List<PostSearchHit>> SearchOneCollectionAsync(
        string collection,
        MemoryKind kind,
        float[] queryVec,
        IReadOnlyList<string> tenants,
        int k,
        CancellationToken ct)
    {
        // Different projections for posts vs notes. Posts have slug+collection+vector_kind;
        // notes have note_key. Build a single SELECT and let null fields stay null.
        var sql = $@"
SELECT
    record::id(id) AS key,
    slug,
    collection,
    vector_kind,
    note_key,
    kind,
    tenant_id,
    title,
    description,
    text,
    ai_summary,
    pub_date,
    category,
    tags,
    1.0 - vector::distance::knn() AS sim
FROM {collection}
WHERE tenant_id IN $tenants
  AND status = 'ready'
  AND embedding <|{k * 2}, COSINE|> $qvec
ORDER BY sim DESC
LIMIT {k * 2};";

        var parameters = new Dictionary<string, object?>
        {
            ["tenants"] = tenants,
            ["qvec"] = queryVec,
        };

        var resp = await _surreal.RawQuery(sql, parameters, ct);
        var rows = resp.GetValue<List<SurrealSearchRow>>(0) ?? new List<SurrealSearchRow>();
        return rows.Select(r => MapRowToHit(r, collection, kind)).ToList();
    }

    private static PostSearchHit MapRowToHit(SurrealSearchRow row, string collection, MemoryKind kind)
    {
        // Notes use note_key as the human key; posts use slug+collection+vector_kind.
        var slug = row.Slug ?? row.NoteKey ?? row.Key ?? string.Empty;
        var coll = row.Collection ?? collection;
        var vk = row.VectorKind ?? kind.ToString().ToLowerInvariant();
        return new PostSearchHit
        {
            Key = row.Key ?? string.Empty,
            Slug = slug,
            Collection = coll,
            VectorKind = vk,
            Kind = row.Kind ?? kind.ToString().ToLowerInvariant(),
            TenantId = row.TenantId,
            Title = row.Title ?? string.Empty,
            Text = row.Text ?? string.Empty,
            Description = row.Description ?? string.Empty,
            AiSummary = row.AiSummary,
            PubDate = row.PubDate,
            Category = row.Category,
            Tags = row.Tags ?? (IReadOnlyList<string>)Array.Empty<string>(),
            Sim = row.Sim,
        };
    }

    private sealed class SurrealSearchRow
    {
        [System.Text.Json.Serialization.JsonPropertyName("key")] public string? Key { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("slug")] public string? Slug { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("collection")] public string? Collection { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("vector_kind")] public string? VectorKind { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("note_key")] public string? NoteKey { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("kind")] public string? Kind { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("tenant_id")] public string? TenantId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("title")] public string? Title { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("description")] public string? Description { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("text")] public string? Text { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ai_summary")] public string? AiSummary { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("pub_date")] public string? PubDate { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("category")] public string? Category { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("tags")] public List<string>? Tags { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("sim")] public double Sim { get; set; }
    }
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
