using System.Text.Json;
using Darbee.Gateway.Domain.Models;
using Darbee.Gateway.Domain.Ports;
using Darbee.Gateway.Domain.Services;
using Darbee.Gateway.Domain.ValueObjects;
using Darbee.Gateway.Domain.Exceptions;
using Darbee.Gateway.Domain.Events;
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
    private readonly IDomainEventDispatcher _dispatcher;

    // ----- lazy schema state (double-checked locking, mirrors MemoryStore) -----
    private volatile bool _schemaReady;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private Exception? _schemaError;

    public SurrealDbMemoryRepository(
        ISurrealDbClient surreal,
        string embeddingModelId,
        int embeddingDimension,
        IEmbeddingClient embeddings,
        IDomainEventDispatcher dispatcher)
    {
        _surreal = surreal;
        _embeddingModelId = embeddingModelId;
        _embeddingDimension = embeddingDimension;
        _embeddings = embeddings;
        _dispatcher = dispatcher;
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
        // JsonElement cannot be deserialized by the Dahomey CBOR engine the SDK uses internally.
        // Use Dictionary<string, object?> and then extract table names via System.Text.Json.
        var first = resp.GetValue<Dictionary<string, object?>>(0);
        if (first is null) return new List<string>();
        var json = System.Text.Json.JsonSerializer.Serialize(first);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var tables = new List<string>();
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("tables", out var tablesEl))
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

        if (summary == VectorWriteOutcome.Embedded || body == VectorWriteOutcome.Embedded)
        {
            await _dispatcher.DispatchAsync(new PostEmbeddedEvent(post.Slug, post.Collection, new TenantId("public"), DateTime.UtcNow), ct);
        }

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
                "SELECT VALUE hash FROM type::record($table, $id) WHERE status = 'ready'",
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
            "UPSERT type::record($table, $id) CONTENT $doc;",
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
            "SELECT VALUE hash FROM type::record($table, $id) WHERE status = 'ready'",
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
            "SELECT VALUE created_at FROM type::record($table, $id)",
            new Dictionary<string, object?> { ["table"] = collection, ["id"] = recordId },
            ct);

        var now = DateTime.UtcNow.ToString("o");
        var doc = new Dictionary<string, object?>
        {
            ["note_key"] = note.Key,
            ["tenant_id"] = note.TenantId.Value,
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
            "UPSERT type::record($table, $id) CONTENT $doc;",
            new Dictionary<string, object?>
            {
                ["table"] = collection,
                ["id"] = recordId,
                ["doc"] = doc,
            },
            ct);

        await _dispatcher.DispatchAsync(new NoteEmbeddedEvent(note.Key, note.Kind.ToString(), note.TenantId, DateTime.UtcNow), ct);

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
    public Task<WriteResult> UpsertDecisionAsync(
        TenantId tenantId, string subject, string chose, string because,
        IReadOnlyList<string> alternatives, CancellationToken ct = default)
    {
        var text = $"Decision: {subject}. Chose {chose} because {because}. Alternatives considered: {string.Join(", ", alternatives)}";
        var now = DateTime.UtcNow.ToString("O");
        var doc = new Dictionary<string, object?>
        {
            ["text"] = text,
            ["tenant_id"] = tenantId.Value,
            ["subject"] = subject,
            ["chose"] = chose,
            ["because"] = because,
            ["alternatives"] = alternatives,
            ["created_at"] = now,
            ["updated_at"] = now,
        };
        return EnsureSchemaThenUpsertAsync(MemoryCollections.Decisions, text, doc, ct);
    }

    public Task<WriteResult> UpsertObservationAsync(
        TenantId tenantId, string source, string text, object payload, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.ToString("O");
        var doc = new Dictionary<string, object?>
        {
            ["text"] = text,
            ["tenant_id"] = tenantId.Value,
            ["source"] = source,
            ["payload"] = payload,
            ["created_at"] = now,
            ["updated_at"] = now,
        };
        return EnsureSchemaThenUpsertAsync(MemoryCollections.Observations, text, doc, ct);
    }

    public Task<WriteResult> UpsertFactAsync(
        TenantId tenantId, string text, string? sourceThread, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.ToString("O");
        var doc = new Dictionary<string, object?>
        {
            ["text"] = text,
            ["tenant_id"] = tenantId.Value,
            ["source_thread"] = sourceThread,
            ["created_at"] = now,
            ["updated_at"] = now,
        };
        return EnsureSchemaThenUpsertAsync(MemoryCollections.Facts, text, doc, ct);
    }

    public Task<WriteResult> UpsertSummaryAsync(
        TenantId tenantId, string text, string threadId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.ToString("O");
        var doc = new Dictionary<string, object?>
        {
            ["text"] = text,
            ["tenant_id"] = tenantId.Value,
            ["thread_id"] = threadId,
            ["created_at"] = now,
            ["updated_at"] = now,
        };
        return EnsureSchemaThenUpsertAsync(MemoryCollections.Summaries, text, doc, ct);
    }

    public async Task<string> UpsertEntityAsync(
        TenantId tenantId, string canonicalName, IReadOnlyList<string> aliases, string type, CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);

        // Deterministic key: sha1(tenant + canonical_name) so the same entity dedupes across calls.
        var recordId = ContentHash.Sha1Hex($"{tenantId.Value}|{canonicalName}");
        var doc = new Dictionary<string, object?>
        {
            ["canonical_name"] = canonicalName,
            ["aliases"] = aliases,
            ["type"] = type,
            ["tenant_id"] = tenantId.Value,
            ["created_at"] = DateTime.UtcNow.ToString("O"),
        };
        await _surreal.RawQuery(
            "UPSERT type::record($table, $id) CONTENT $doc;",
            new Dictionary<string, object?>
            {
                ["table"] = MemoryCollections.Entities,
                ["id"] = recordId,
                ["doc"] = doc,
            },
            ct);
        return recordId;
    }

    public async Task<string> UpsertEdgeAsync(
        TenantId tenantId, string fromId, string toId, string kind, double weight, CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);

        // Deterministic key so the same (from, to, kind) edge dedupes.
        var recordId = ContentHash.Sha1Hex($"{tenantId.Value}|{fromId}->{toId}|{kind}");

        // SurrealDB RELATE creates a graph edge. fromId/toId are colon-separated record references
        // e.g. "memory_entities:abc123". Split on ':' to get table and id parts.
        var sql = @"
RELATE type::record(string::split($from, ':')[0], string::split($from, ':')[1])
    -> memory_edges
    -> type::record(string::split($to, ':')[0], string::split($to, ':')[1])
SET id = type::record('memory_edges', $id),
    kind = $kind,
    weight = $weight,
    tenant_id = $tenant,
    created_at = $now;";

        var parameters = new Dictionary<string, object?>
        {
            ["from"] = fromId,
            ["to"] = toId,
            ["id"] = recordId,
            ["kind"] = kind,
            ["weight"] = weight,
            ["tenant"] = tenantId.Value,
            ["now"] = DateTime.UtcNow.ToString("O"),
        };
        await _surreal.RawQuery(sql, parameters, ct);
        return recordId;
    }

    private async Task<WriteResult> EnsureSchemaThenUpsertAsync(
        string collection, string text, Dictionary<string, object?> doc, CancellationToken ct)
    {
        await EnsureSchemaIfNeededAsync(ct);
        return await UpsertContentAsync(collection, text, doc, ct);
    }

    private async Task<WriteResult> UpsertContentAsync(
        string collection,
        string text,
        Dictionary<string, object?> doc,
        CancellationToken ct)
    {
        // Deterministic key from created_at + first 64 chars of text to keep writes idempotent within a window.
        var seed = $"{doc.GetValueOrDefault("created_at")}|{(text.Length > 64 ? text[..64] : text)}";
        var recordId = ContentHash.Sha1Hex(seed);
        doc["status"] = "pending_embedding";

        await _surreal.RawQuery(
            "UPSERT type::record($table, $id) CONTENT $doc;",
            new Dictionary<string, object?> { ["table"] = collection, ["id"] = recordId, ["doc"] = doc },
            ct);

        try
        {
            var emb = await _embeddings.EmbedAsync(text, ct);
            if (emb.Length != _embeddingDimension)
                throw new InvalidOperationException(
                    $"Embedding dimension mismatch: expected {_embeddingDimension}, got {emb.Length}");

            await _surreal.RawQuery(
                "UPDATE type::record($table, $id) SET embedding = $emb, status = 'ready', updated_at = $now;",
                new Dictionary<string, object?>
                {
                    ["table"] = collection,
                    ["id"] = recordId,
                    ["emb"] = emb,
                    ["now"] = DateTime.UtcNow.ToString("O"),
                },
                ct);

            return WriteResult.Ready(recordId);
        }
        catch
        {
            // Embedding unavailable or failed — document stays in pending_embedding state.
            return WriteResult.Pending(recordId);
        }
    }

    // ----- IMemoryRepository: reads -----

    public async Task<List<PostSearchHit>> SearchAsync(
        float[] queryVec,
        IReadOnlyList<MemoryKind> kinds,
        IReadOnlyList<TenantId> tenants,
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
        IReadOnlyList<TenantId> tenants,
        int k,
        CancellationToken ct)
    {
        // Project scalar fields only (no raw record-id `id`, no `embedding` vector).
        // The SDK's CBOR engine cannot deserialize Dictionary<string, object?> (its
        // object-value converter rejects scalars) — a concrete POCO with snake_case
        // property names matching the SurrealDB column names is the reliable path.
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
            ["tenants"] = tenants.Select(t => t.Value).ToList(),
            ["qvec"] = queryVec,
        };

        var resp = await _surreal.RawQuery(sql, parameters, ct);
        var rows = resp.GetValue<List<SurrealSearchRow>>(0) ?? new List<SurrealSearchRow>();
        return rows.Select(r => MapRowToHit(r, collection, kind)).ToList();
    }

    private static PostSearchHit MapRowToHit(SurrealSearchRow row, string collection, MemoryKind kind)
    {
        // Notes use note_key as the human key; posts use slug.
        var slug = row.slug ?? row.note_key ?? row.key ?? string.Empty;
        return new PostSearchHit
        {
            Key = row.key ?? string.Empty,
            Slug = slug,
            Collection = row.collection ?? collection,
            VectorKind = row.vector_kind ?? kind.ToString().ToLowerInvariant(),
            Kind = row.kind ?? kind.ToString().ToLowerInvariant(),
            TenantId = row.tenant_id != null ? new TenantId(row.tenant_id) : null,
            Title = row.title ?? string.Empty,
            Text = row.text ?? string.Empty,
            Description = row.description ?? string.Empty,
            AiSummary = row.ai_summary,
            PubDate = row.pub_date,
            Category = row.category,
            Tags = row.tags ?? (IReadOnlyList<string>)Array.Empty<string>(),
            Sim = row.sim,
        };
    }

    // Property names are snake_case to match the SurrealDB column names verbatim — the
    // SDK's CBOR engine maps by member name and ignores System.Text.Json attributes.
#pragma warning disable IDE1006 // intentional snake_case to mirror DB columns
    private sealed class SurrealSearchRow
    {
        public string? key { get; set; }
        public string? slug { get; set; }
        public string? collection { get; set; }
        public string? vector_kind { get; set; }
        public string? note_key { get; set; }
        public string? kind { get; set; }
        public string? tenant_id { get; set; }
        public string? title { get; set; }
        public string? description { get; set; }
        public string? text { get; set; }
        public string? ai_summary { get; set; }
        public string? pub_date { get; set; }
        public string? category { get; set; }
        public List<string>? tags { get; set; }
        public double sim { get; set; }
    }
#pragma warning restore IDE1006
    public async Task<string?> ReadPostHashAsync(string key, CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);
        return await SelectScalarAsync<string>(
            "SELECT VALUE hash FROM type::record($table, $id);",
            new Dictionary<string, object?> { ["table"] = "memory_posts", ["id"] = key },
            ct);
    }

    public async Task<JsonDocument?> ReadNoteDocumentAsync(string collection, string noteKey, CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);
        var recordId = ContentHash.Sha1Hex(noteKey);

        // Project explicit scalar fields only. `SELECT *` would include the raw record-id
        // and the embedding vector, neither of which the SDK's CBOR engine can deserialize
        // into a generic container. A concrete POCO with snake_case members is reliable.
        var resp = await _surreal.RawQuery(
            @"SELECT
                record::id(id) AS key,
                note_key,
                tenant_id,
                kind,
                title,
                text,
                hash,
                status,
                source,
                created_at,
                updated_at
              FROM type::record($table, $id);",
            new Dictionary<string, object?> { ["table"] = collection, ["id"] = recordId },
            ct);

        // SELECT returns an array of rows; expect 0 or 1.
        var rows = resp.GetValue<List<SurrealNoteRow>>(0);
        if (rows is null || rows.Count == 0) return null;
        var json = System.Text.Json.JsonSerializer.Serialize(rows[0]);
        return JsonDocument.Parse(json);
    }

#pragma warning disable IDE1006 // intentional snake_case to mirror DB columns
    private sealed class SurrealNoteRow
    {
        public string? key { get; set; }
        public string? note_key { get; set; }
        public string? tenant_id { get; set; }
        public string? kind { get; set; }
        public string? title { get; set; }
        public string? text { get; set; }
        public string? hash { get; set; }
        public string? status { get; set; }
        public string? source { get; set; }
        public string? created_at { get; set; }
        public string? updated_at { get; set; }
    }
#pragma warning restore IDE1006

    public async Task<List<(string id, string targetCollection, string targetKey)>> ListPendingEmbeddingsAsync(
        int limit = 100, CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);
        var sql = $@"
SELECT
    record::id(id) AS id,
    target_collection AS targetCollection,
    target_key AS targetKey
FROM {MemoryCollections.PendingEmbeddings}
ORDER BY queued_at ASC
LIMIT $limit;";
        var resp = await _surreal.RawQuery(
            sql,
            new Dictionary<string, object?> { ["limit"] = limit },
            ct);
        var rows = resp.GetValue<List<PendingEmbeddingRow>>(0) ?? new List<PendingEmbeddingRow>();
        return rows.Select(r => (r.Id, r.TargetCollection, r.TargetKey)).ToList();
    }

    private sealed class PendingEmbeddingRow
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")] public string Id { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("targetCollection")] public string TargetCollection { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("targetKey")] public string TargetKey { get; set; } = "";
    }

    // ----- IMemoryRepository: deletes -----

    public async Task<int> DeleteStalePostsAsync(
        IReadOnlyCollection<(string Collection, string Slug)> currentPosts,
        IReadOnlyCollection<string>? scopedCollections = null,
        CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);

        // Default scope: collections present in the current set.
        IReadOnlyCollection<string> scope = scopedCollections
            ?? currentPosts.Select(p => p.Collection).Distinct().ToList();

        if (scope.Count == 0) return 0;

        // Build the keep-list as "collection|slug" strings for the IN test.
        // '|' is not valid in slug or collection vocabulary, so no collisions possible.
        var keepKeys = currentPosts.Select(p => $"{p.Collection}|{p.Slug}").ToList();

        var sql = @"
LET $deleted = (
    DELETE FROM memory_posts
    WHERE collection IN $scope
      AND string::concat(collection, '|', slug) NOT IN $keep
    RETURN BEFORE
);
RETURN array::len($deleted);";

        var parameters = new Dictionary<string, object?>
        {
            ["scope"] = scope,
            ["keep"] = keepKeys,
        };

        var resp = await _surreal.RawQuery(sql, parameters, ct);
        // The final statement is `RETURN array::len(...)` — result is at index 1 (0-based).
        int count;
        try
        {
            count = resp.GetValue<int>(1);
        }
        catch
        {
            var arr = resp.GetValue<List<int>>(1);
            count = arr is null || arr.Count == 0 ? 0 : arr[0];
        }

        if (count > 0)
        {
            await _dispatcher.DispatchAsync(new StalePostsDeletedEvent(count, scope, DateTime.UtcNow), ct);
        }
        return count;
    }

    public async Task<int> DeleteStaleNotesAsync(
        IReadOnlyList<string> currentKeys,
        TenantId tenantId,
        CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);

        int total = 0;
        foreach (var collection in new[]
        {
            MemoryCollections.Observations,
            MemoryCollections.Facts,
            MemoryCollections.Decisions,
        })
        {
            var sql = $@"
LET $deleted = (
    DELETE FROM {collection}
    WHERE tenant_id = $tenant
      AND source = 'obsidian'
      AND note_key NOT IN $keep
    RETURN BEFORE
);
RETURN array::len($deleted);";

            var parameters = new Dictionary<string, object?>
            {
                ["tenant"] = tenantId.Value,
                ["keep"] = currentKeys,
            };

            var resp = await _surreal.RawQuery(sql, parameters, ct);
            int n;
            try { n = resp.GetValue<int>(1); }
            catch
            {
                var arr = resp.GetValue<List<int>>(1);
                n = arr is null || arr.Count == 0 ? 0 : arr[0];
            }
            total += n;
        }
        return total;
    }

    // ----- IMemoryRepository: query -----
    public async Task<List<T>> QueryAsync<T>(string query, Dictionary<string, object> bindVars, CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);
        // Bind vars come in as IDictionary<string, object>; the SDK wants object?.
        var p = bindVars.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
        var resp = await _surreal.RawQuery(query, p, ct);
        return resp.GetValue<List<T>>(0) ?? new List<T>();
    }

    // ----- IEmbeddingMigrator -----

    public async Task<EmbeddingConfig?> ReadEmbeddingConfigAsync(CancellationToken ct = default)
    {
        // memory_meta:embedding_config is a fixed record id.
        var resp = await _surreal.RawQuery(
            "SELECT model, dimension FROM type::record($table, $id);",
            new Dictionary<string, object?>
            {
                ["table"] = MemoryCollections.Meta,
                ["id"] = "embedding_config",
            },
            ct);
        var rows = resp.GetValue<List<EmbeddingConfigRow>>(0);
        if (rows is null || rows.Count == 0) return null;
        var r = rows[0];
        if (string.IsNullOrWhiteSpace(r.Model) || r.Dimension <= 0) return null;
        return new EmbeddingConfig(r.Model, r.Dimension);
    }

    private sealed class EmbeddingConfigRow
    {
        [System.Text.Json.Serialization.JsonPropertyName("model")] public string Model { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("dimension")] public int Dimension { get; set; }
    }

    private async Task WriteEmbeddingConfigAsync(EmbeddingConfig config, bool isFirstTime, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.ToString("O");
        var doc = new Dictionary<string, object?>
        {
            ["model"] = config.Model,
            ["dimension"] = config.Dimension,
            ["last_set_at"] = now,
        };
        if (isFirstTime) doc["first_set_at"] = now;

        await _surreal.RawQuery(
            "UPSERT type::record($table, $id) MERGE $doc;",
            new Dictionary<string, object?>
            {
                ["table"] = MemoryCollections.Meta,
                ["id"] = "embedding_config",
                ["doc"] = doc,
            },
            ct);
    }

    public async Task<MigrationResult> MigrateEmbeddingsAsync(
        string confirmToken,
        CancellationToken ct = default)
    {
        if (confirmToken != "preserve-and-reembed" && confirmToken != "wipe-and-reset")
            throw new ArgumentException(
                $"Invalid confirm token '{confirmToken}'. Accepted: 'preserve-and-reembed' or 'wipe-and-reset'.",
                nameof(confirmToken));

        await EnsureSchemaIfNeededAsync(ct);

        var current = new EmbeddingConfig(_embeddingModelId, _embeddingDimension);
        var previous = await ReadEmbeddingConfigAsync(ct);

        var contentCollections = new[]
        {
            MemoryCollections.Posts,
            MemoryCollections.Observations,
            MemoryCollections.Facts,
            MemoryCollections.Decisions,
            MemoryCollections.Summaries,
        };

        // Drop existing HNSW indexes — they'll be recreated against the new dimension
        // on the next EnsureSchemaAsync call (which the constructor flow runs at startup).
        var indexesDropped = new List<string>();
        foreach (var coll in contentCollections)
        {
            var indexName = coll switch
            {
                "memory_posts" => "idx_posts_embed",
                "memory_observations" => "idx_observations_embed",
                "memory_facts" => "idx_facts_embed",
                "memory_decisions" => "idx_decisions_embed",
                "memory_summaries" => "idx_summaries_embed",
                _ => null,
            };
            if (indexName is null) continue;

            try
            {
                await _surreal.RawQuery($"REMOVE INDEX IF EXISTS {indexName} ON TABLE {coll};", null, ct);
                indexesDropped.Add(indexName);
            }
            catch
            {
                // Idempotent — REMOVE INDEX may be a no-op on older SurrealDB versions.
            }
        }

        var docsMarked = new Dictionary<string, int>();
        int queueSize = 0;

        if (confirmToken == "preserve-and-reembed")
        {
            foreach (var coll in contentCollections)
            {
                var sql = $@"
LET $updated = (
    UPDATE {coll}
    SET embedding = NONE, status = 'pending_embedding'
    WHERE embedding IS NOT NONE
    RETURN AFTER
);
RETURN array::len($updated);";
                var resp = await _surreal.RawQuery(sql, null, ct);
                int n;
                try { n = resp.GetValue<int>(1); }
                catch
                {
                    var arr = resp.GetValue<List<int>>(1);
                    n = arr is null || arr.Count == 0 ? 0 : arr[0];
                }
                docsMarked[coll] = n;
                queueSize += n;
            }
        }
        else // wipe-and-reset
        {
            foreach (var coll in contentCollections)
            {
                var sql = $@"
LET $deleted = (DELETE {coll} RETURN BEFORE);
RETURN array::len($deleted);";
                var resp = await _surreal.RawQuery(sql, null, ct);
                int n;
                try { n = resp.GetValue<int>(1); }
                catch
                {
                    var arr = resp.GetValue<List<int>>(1);
                    n = arr is null || arr.Count == 0 ? 0 : arr[0];
                }
                docsMarked[coll] = n;
            }
            queueSize = 0;
        }

        // Recreate the HNSW indexes against the new dimension.
        // Every DEFINE has IF NOT EXISTS, so this is safe to re-run.
        await EnsureSchemaAsync(ct);

        await WriteEmbeddingConfigAsync(current, isFirstTime: previous is null, ct);

        await _dispatcher.DispatchAsync(new EmbeddingConfigChangedEvent(previous, current, queueSize, DateTime.UtcNow), ct);

        return new MigrationResult(
            Previous: previous,
            Current: current,
            IndexesDropped: indexesDropped,
            DocsMarkedForReembed: docsMarked,
            QueueSizeAfter: queueSize);
    }

    public void Dispose()
    {
        _schemaLock.Dispose();
        // ISurrealDbClient is managed by DI; the client itself is not disposed here.
    }
}
