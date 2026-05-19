using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using ArangoDBNetStandard;
using ArangoDBNetStandard.CollectionApi.Models;
using ArangoDBNetStandard.Transport.Http;
using Darbee.Gateway.Memory.Models;

namespace Darbee.Gateway.Memory;

public sealed class MemoryStore : IDisposable
{
    private readonly ArangoDBClient _arango;
    private readonly HttpApiTransport _transport;
    private readonly HttpClient _rawHttp;
    private readonly string _baseUrl;
    private readonly string _db;
    private readonly string _user;
    private readonly string _pass;
    private readonly string _embeddingModelId;
    private readonly int _embeddingDimension;
    private readonly int _vectorNLists;
    private readonly IEmbeddingClient? _embeddings;
    private readonly ConcurrentDictionary<string, bool> _vectorIndexReady = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _vectorIndexLocks = new();
    private volatile bool _schemaReady;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private Exception? _schemaError;

    public MemoryStore(string url, string db, string user, string pass, string embeddingModelId, int embeddingDimension, int vectorNLists, HttpClient rawHttp, IEmbeddingClient? embeddings = null)
    {
        _baseUrl = url.TrimEnd('/');
        _db = db;
        _user = user;
        _pass = pass;
        _embeddingModelId = embeddingModelId;
        _embeddingDimension = embeddingDimension;
        _vectorNLists = vectorNLists;
        _rawHttp = rawHttp;
        _embeddings = embeddings;
        _transport = HttpApiTransport.UsingBasicAuth(new Uri(url), db, user, pass);
        _arango = new ArangoDBClient(_transport);
    }

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        foreach (var name in new[]
        {
            MemoryCollections.Decisions,
            MemoryCollections.Observations,
            MemoryCollections.Facts,
            MemoryCollections.Summaries,
            MemoryCollections.Entities,
            MemoryCollections.PendingEmbeddings,
            MemoryCollections.Posts,    // NEW
            MemoryCollections.Meta,     // NEW
        })
        {
            await EnsureCollectionAsync(name, isEdge: false);
        }

        await EnsureCollectionAsync(MemoryCollections.Edges, isEdge: true);

        foreach (var content in new[]
        {
            MemoryCollections.Decisions,
            MemoryCollections.Observations,
            MemoryCollections.Facts,
            MemoryCollections.Summaries
        })
        {
            await EnsurePersistentIndexAsync(content, new[] { "tenant_id", "status", "created_at" });
        }

        await EnsurePersistentIndexAsync(MemoryCollections.Entities, new[] { "tenant_id", "canonical_name" });
        await EnsurePersistentIndexAsync(MemoryCollections.Entities, new[] { "tenant_id", "aliases[*]" });
        await EnsurePersistentIndexAsync(MemoryCollections.Edges, new[] { "tenant_id", "kind" });

        // NEW: persistent indexes on memory_posts
        await EnsurePersistentIndexAsync(MemoryCollections.Posts, new[] { "tenant_id", "status", "vector_kind" });
        await EnsurePersistentIndexAsync(MemoryCollections.Posts, new[] { "collection", "slug" });

        var current = new EmbeddingConfig(_embeddingModelId, _embeddingDimension);
        var stored = await ReadEmbeddingConfigAsync(ct);
        if (stored is null)
        {
            await WriteEmbeddingConfigAsync(current, isFirstTime: true, ct);
        }
        else if (!stored.Equals(current))
        {
            throw new EmbeddingConfigMismatchException(stored, current);
        }
    }

    public async Task EnsureSchemaIfNeededAsync(CancellationToken ct = default)
    {
        if (_schemaReady) return;
        if (_schemaError is not null)
            throw _schemaError;

        await _schemaLock.WaitAsync(ct);
        try
        {
            if (_schemaReady) return;
            if (_schemaError is not null)
                throw _schemaError;
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

    internal void InvalidateSchemaReady()
    {
        _schemaReady = false;
        _schemaError = null;
    }

    internal async Task InsertRawPostAsync(Dictionary<string, object?> doc, CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);
        var url = $"{_baseUrl}/_db/{_db}/_api/document/{MemoryCollections.Posts}?overwrite=true";
        var (ok, errorNum, content) = await PostJsonRawAsync(url, doc);
        if (!ok) throw new InvalidOperationException($"InsertRawPostAsync failed: {content}");
    }

    internal async Task InsertRawPostAsync(Dictionary<string, object?> doc, string collection, CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);
        var url = $"{_baseUrl}/_db/{_db}/_api/document/{collection}?overwrite=true";
        var (ok, errorNum, content) = await PostJsonRawAsync(url, doc);
        if (!ok) throw new InvalidOperationException($"InsertRawPostAsync failed on '{collection}': {content}");
    }

    private async Task UpsertRawDocumentAsync(string collection, Dictionary<string, object?> doc, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/_db/{_db}/_api/document/{collection}?overwrite=true";
        var (ok, errorNum, content) = await PostJsonRawAsync(url, doc);
        if (!ok) throw new InvalidOperationException($"UpsertRawDocumentAsync failed on '{collection}/{doc["_key"]}': {content}");
    }

    private async Task<JsonDocument?> ReadDocumentByKeyAsync(string collection, string arangoKey, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/_db/{_db}/_api/document/{collection}/{arangoKey}";
        using var request = BuildAuthedRequest(HttpMethod.Get, url);
        using var response = await _rawHttp.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(content);
    }

    // Not gated: called from EnsureSchemaAsync during bootstrap — gating would deadlock.
    public async Task<EmbeddingConfig?> ReadEmbeddingConfigAsync(CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/_db/{_db}/_api/document/{MemoryCollections.Meta}/embedding_config";
        using var request = BuildAuthedRequest(HttpMethod.Get, url);
        using var response = await _rawHttp.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(content);
        var model = doc.RootElement.GetProperty("model").GetString() ?? "";
        var dim = doc.RootElement.GetProperty("dimension").GetInt32();
        return new EmbeddingConfig(model, dim);
    }

    private async Task WriteEmbeddingConfigAsync(EmbeddingConfig config, bool isFirstTime, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.ToString("O");
        var doc = new Dictionary<string, object?>
        {
            ["_key"] = "embedding_config",
            ["model"] = config.Model,
            ["dimension"] = config.Dimension,
            ["last_set_at"] = now,
        };
        if (isFirstTime) doc["first_set_at"] = now;

        var url = isFirstTime
            ? $"{_baseUrl}/_db/{_db}/_api/document/{MemoryCollections.Meta}"
            : $"{_baseUrl}/_db/{_db}/_api/document/{MemoryCollections.Meta}/embedding_config";
        var method = isFirstTime ? HttpMethod.Post : HttpMethod.Patch;
        var (ok, errorNum, content) = await PostJsonRawAsync(url, doc, method);
        if (ok || errorNum == 1210 /* duplicate */) return;
        throw new InvalidOperationException($"Failed to write embedding_config (errorNum={errorNum}): {content}");
    }

    public async Task<MigrationResult> MigrateEmbeddingsAsync(
        string confirmToken,
        CancellationToken ct = default)
    {
        if (confirmToken != "preserve-and-reembed" && confirmToken != "wipe-and-reset")
            throw new ArgumentException(
                $"Invalid confirm token '{confirmToken}'. Accepted: 'preserve-and-reembed' or 'wipe-and-reset'.",
                nameof(confirmToken));

        // Minimal bootstrap: ensure memory_meta exists; do NOT call EnsureSchemaIfNeededAsync
        // (this endpoint must work even when the full schema is in mismatch state).
        await EnsureCollectionAsync(MemoryCollections.Meta, isEdge: false);

        var current = new EmbeddingConfig(_embeddingModelId, _embeddingDimension);
        var previous = await ReadEmbeddingConfigAsync(ct);

        var indexesDropped = new List<string>();
        var docsMarked = new Dictionary<string, int>();

        if (previous is not null && !previous.Equals(current))
        {
            foreach (var collection in new[]
            {
                MemoryCollections.Decisions,
                MemoryCollections.Observations,
                MemoryCollections.Facts,
                MemoryCollections.Summaries,
                MemoryCollections.Posts,
            })
            {
                // Ensure the collection exists before working on it.
                await EnsureCollectionAsync(collection, isEdge: false);

                // Drop vector indexes
                var indexes = await ListIndexesAsync(collection);
                foreach (var idx in indexes.Where(i => i.Type == "vector"))
                {
                    await DeleteIndexAsync(idx.Id);
                    indexesDropped.Add($"{collection}/{idx.Id.Split('/').Last()}");
                }

                int affected = 0;
                if (confirmToken == "preserve-and-reembed")
                {
                    // keepNull:false strips the embedding attribute entirely instead of
                    // leaving it as JSON null. Required because a sparse vector index in
                    // ArangoDB 3.12 still rejects writes that include the indexed field as
                    // explicit null ("array expected for vector attribute"). Stripping the
                    // attribute is equivalent for our purposes — the doc is marked pending
                    // and will be re-embedded asynchronously.
                    var aql = """
                        FOR doc IN @@col
                          FILTER doc.embedding != null
                          UPDATE doc WITH {
                            embedding: null,
                            status: "pending_embedding",
                            updated_at: DATE_ISO8601(DATE_NOW())
                          } IN @@col OPTIONS { keepNull: false }
                          RETURN OLD._key
                        """;
                    var cursor = await _arango.Cursor.PostCursorAsync<string>(
                        new ArangoDBNetStandard.CursorApi.Models.PostCursorBody
                        {
                            Query = aql,
                            BindVars = new Dictionary<string, object> { ["@col"] = collection },
                        });
                    var keys = cursor.Result.ToList();
                    affected = keys.Count;

                    foreach (var key in keys)
                        await EnqueuePendingEmbeddingAsync(collection, key);
                }
                else  // wipe-and-reset
                {
                    var aql = """
                        FOR doc IN @@col
                          FILTER doc.embedding != null
                          REMOVE doc IN @@col
                          RETURN OLD._key
                        """;
                    var cursor = await _arango.Cursor.PostCursorAsync<string>(
                        new ArangoDBNetStandard.CursorApi.Models.PostCursorBody
                        {
                            Query = aql,
                            BindVars = new Dictionary<string, object> { ["@col"] = collection },
                        });
                    affected = cursor.Result.Count();
                }

                docsMarked[collection] = affected;
            }

            _vectorIndexReady.Clear();
        }

        // Always write/refresh the config doc with current values
        await WriteEmbeddingConfigAsync(current, isFirstTime: previous is null, ct);

        // Reset schema-ready so the next request re-runs EnsureSchemaAsync against fresh state
        InvalidateSchemaReady();

        // Count queue size after
        await EnsureCollectionAsync(MemoryCollections.PendingEmbeddings, isEdge: false);
        var queueAql = "FOR p IN @@col COLLECT WITH COUNT INTO n RETURN n";
        var queueCursor = await _arango.Cursor.PostCursorAsync<int>(
            new ArangoDBNetStandard.CursorApi.Models.PostCursorBody
            {
                Query = queueAql,
                BindVars = new Dictionary<string, object> { ["@col"] = MemoryCollections.PendingEmbeddings },
            });
        var queueSizeAfter = queueCursor.Result.FirstOrDefault();

        return new MigrationResult(
            previous,
            current,
            indexesDropped,
            docsMarked,
            queueSizeAfter);
    }

    public async Task EnsureVectorIndexAsync(string collection, CancellationToken ct = default)
    {
        if (_vectorIndexReady.TryGetValue(collection, out var cached) && cached) return;

        var sem = _vectorIndexLocks.GetOrAdd(collection, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            // Re-check inside the lock — another thread may have just finished.
            if (_vectorIndexReady.TryGetValue(collection, out var c2) && c2) return;

            var indexes = await ListIndexesAsync(collection);

            // 1. Ready index with correct params → done.
            if (indexes.Any(i => i.Type == "vector"
                && i.TrainingState == "ready"
                && i.Params?.Dimension == _embeddingDimension
                && i.Params?.NLists == _vectorNLists))
            {
                _vectorIndexReady[collection] = true;
                return;
            }

            // 2. Training-in-progress index with correct params → don't disturb it.
            //    It will become ready; the next write/read will see it.
            if (indexes.Any(i => i.Type == "vector"
                && i.TrainingState != "ready"
                && i.Params?.Dimension == _embeddingDimension
                && i.Params?.NLists == _vectorNLists))
            {
                return;
            }

            // 3. Drop genuinely-stale vector indexes (wrong dimension or nLists).
            //    Swallow 1212 (already gone) — another thread may have raced us.
            foreach (var idx in indexes.Where(i =>
                i.Type == "vector"
                && (i.Params?.Dimension != _embeddingDimension || i.Params?.NLists != _vectorNLists)))
            {
                try { await DeleteIndexAsync(idx.Id); }
                catch { /* already dropped or in flight */ }
            }

            // 4. Create a new index — only if we have enough docs to train.
            var docCount = await CountDocumentsAsync(collection);
            if (docCount < _vectorNLists) return;

            var url = $"{_baseUrl}/_db/{_db}/_api/index?collection={collection}";
            var body = new
            {
                type = "vector",
                fields = new[] { "embedding" },
                // sparse=true: skip docs without an embedding field. Required because our write
                // path is two-stage (POST pending doc → PATCH with embedding once it returns from
                // the embedding service, or enqueue for retry on failure). Without sparse the
                // initial POST fails with "vector field not present in document".
                sparse = true,
                // defaultNProbe=8 overrides ArangoDB's default of 1, which is too aggressive
                // and produces empty results when LIMIT is small. Faiss best practice is nProbe
                // proportional to sqrt(nLists); 8 is a sensible floor for our nLists range.
                @params = new
                {
                    dimension = _embeddingDimension,
                    metric = "cosine",
                    nLists = _vectorNLists,
                    defaultNProbe = 8,
                }
            };
            var (ok, errorNum, content) = await PostJsonRawAsync(url, body);
            if (ok)
            {
                _vectorIndexReady[collection] = true;
                return;
            }

            // 1555: vector index feature not enabled on the server (--vector-index flag).
            //       Treat as soft no-op so write paths still work.
            // 1212: index dropped mid-flight (race with a parallel writer that just won).
            //       Re-check on next call; current write has already succeeded.
            // 1210/1207: duplicate index already created by a parallel writer. Same handling.
            if (errorNum == 1555 || errorNum == 1212 || errorNum == 1210 || errorNum == 1207) return;
            throw new InvalidOperationException($"Vector index creation failed (errorNum={errorNum}) on '{collection}': {content}");
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task<bool> HasUsableVectorIndexAsync(string collection)
    {
        var indexes = await ListIndexesAsync(collection);
        return indexes.Any(i => i.Type == "vector" && i.TrainingState == "ready");
    }

    /// <summary>
    /// Fast, cached, read-only check used by read paths (SearchAsync, VectorTopKAsync)
    /// to decide between APPROX_NEAR_COSINE (trained Faiss IVF index available) and
    /// COSINE_SIMILARITY (exact scan fallback). Never attempts to create or train.
    /// Once a trained index is discovered, the result is cached for the lifetime of
    /// this MemoryStore instance.
    /// </summary>
    public async Task<bool> IsVectorIndexReadyAsync(string collection, CancellationToken ct = default)
    {
        if (_vectorIndexReady.TryGetValue(collection, out var cached) && cached) return true;

        try
        {
            var indexes = await ListIndexesAsync(collection);
            var ready = indexes.Any(i =>
                i.Type == "vector"
                && i.TrainingState == "ready"
                && i.Params?.Dimension == _embeddingDimension
                && i.Params?.NLists == _vectorNLists);
            if (ready) _vectorIndexReady[collection] = true;
            return ready;
        }
        catch
        {
            // Collection may not exist yet, or server may not support vector indexes.
            // Fall back to exact path silently.
            return false;
        }
    }

    public async Task<int> CountVectorIndexesAsync(string collection)
    {
        var indexes = await ListIndexesAsync(collection);
        return indexes.Count(i => i.Type == "vector");
    }

    public async Task<List<string>> ListCollectionsAsync(CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);
        var result = await _arango.Collection.GetCollectionsAsync();
        return result.Result.Select(c => c.Name).ToList();
    }

    /// <summary>
    /// Returns the raw post document as a JsonDocument. <b>Caller must dispose.</b>
    /// Test-oriented API — production code should use ReadPostHashAsync or add a
    /// purpose-built reader rather than work with the raw JSON.
    /// </summary>
    public async Task<JsonDocument?> ReadPostDocumentAsync(string key, CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);
        var url = $"{_baseUrl}/_db/{_db}/_api/document/{MemoryCollections.Posts}/{key}";
        using var request = BuildAuthedRequest(HttpMethod.Get, url);
        using var response = await _rawHttp.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(content);
    }

    public async Task<string?> ReadPostHashAsync(string key, CancellationToken ct = default)
    {
        using var doc = await ReadPostDocumentAsync(key, ct);
        if (doc is null) return null;
        if (!doc.RootElement.TryGetProperty("hash", out var hashProp)) return null;
        return hashProp.GetString();
    }

    internal record PostCacheState(string Hash, string Status);

    internal async Task<PostCacheState?> ReadPostCacheStateAsync(string key, CancellationToken ct = default)
    {
        using var doc = await ReadPostDocumentAsync(key, ct);
        if (doc is null) return null;
        var root = doc.RootElement;
        if (!root.TryGetProperty("hash", out var hashProp)) return null;
        var hash = hashProp.GetString();
        if (hash is null) return null;
        var status = root.TryGetProperty("status", out var statusProp)
            ? statusProp.GetString() ?? ""
            : "";
        return new PostCacheState(hash, status);
    }

    public async Task<UpsertPostResult> UpsertPostAsync(
        PostDocument post,
        bool force,
        CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);
        if (_embeddings is null)
            throw new InvalidOperationException("Embedding client is required for post upserts.");

        var summaryText = PostTextComposer.ComposeSummary(post);
        var bodyText = PostTextComposer.ComposeBody(post);

        var summaryOutcome = await UpsertOnePostVectorAsync(post, "summary", summaryText, force, ct);
        var bodyOutcome = await UpsertOnePostVectorAsync(post, "body", bodyText, force, ct);

        return new UpsertPostResult(post.Slug, post.Collection, summaryOutcome, bodyOutcome);
    }

    private async Task<VectorWriteOutcome> UpsertOnePostVectorAsync(
        PostDocument post, string vectorKind, string text, bool force, CancellationToken ct)
    {
        var key = $"{post.Collection}__{post.Slug}__{vectorKind}";
        var hash = ComputeHash(text, _embeddingModelId);

        if (!force)
        {
            var existing = await ReadPostCacheStateAsync(key, ct);
            // Cache hit only when both the embed text AND the doc's status are
            // current. A pending_embedding doc with a matching hash means the
            // embedding was cleared (e.g., by MigrateEmbeddingsAsync) and must
            // be regenerated regardless of hash equality.
            if (existing is not null && existing.Hash == hash && existing.Status == "ready")
                return VectorWriteOutcome.Cached;
        }

        var embedding = await _embeddings!.EmbedAsync(text, ct);
        var now = DateTime.UtcNow.ToString("O");
        var doc = new Dictionary<string, object?>
        {
            ["_key"] = key,
            ["slug"] = post.Slug,
            ["collection"] = post.Collection,
            ["vector_kind"] = vectorKind,
            ["kind"] = "post",
            ["tenant_id"] = "public",
            ["text"] = text,
            ["embedding"] = embedding,
            ["hash"] = hash,
            ["title"] = post.Title,
            ["description"] = post.Description,
            ["pub_date"] = post.PubDate,
            ["category"] = post.Category,
            ["tags"] = post.Tags,
            ["entity_mentions"] = post.EntityMentions,
            ["ai_summary"] = post.AiSummary,
            ["status"] = "ready",
            ["created_at"] = now,
            ["updated_at"] = now,
        };

        var url = $"{_baseUrl}/_db/{_db}/_api/document/{MemoryCollections.Posts}?overwrite=true";
        var (ok, errorNum, content) = await PostJsonRawAsync(url, doc);
        if (!ok)
            throw new InvalidOperationException($"Post upsert failed (errorNum={errorNum}): {content}");

        // Lazy vector-index creation: when docCount crosses nLists, this builds the
        // Faiss IVF index so subsequent SearchAsync calls can use APPROX_NEAR_COSINE.
        await EnsureVectorIndexAsync(MemoryCollections.Posts, ct);
        return VectorWriteOutcome.Embedded;
    }

    public async Task<UpsertNoteResult> UpsertNoteAsync(NoteDocument note, CancellationToken ct = default)
    {
        if (_embeddings is null)
            throw new InvalidOperationException("MemoryStore was constructed without an IEmbeddingClient — cannot upsert notes");

        await EnsureSchemaIfNeededAsync(ct);

        var collection = MemoryCollections.ForKind(note.Kind);
        var arangoKey = Sha1Hex(note.Key);
        var hash = Sha256Hex($"{_embeddingModelId}\n{note.Text}");

        // Cache check: if existing doc has same hash AND status=ready, skip embed.
        using var existing = await ReadDocumentByKeyAsync(collection, arangoKey, ct);
        if (existing is not null)
        {
            var existingHash = existing.RootElement.TryGetProperty("hash", out var h) ? h.GetString() : null;
            var existingStatus = existing.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
            if (existingHash == hash && existingStatus == "ready")
                return new UpsertNoteResult(note.Key, VectorWriteOutcome.Cached);
        }

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

        var now = DateTime.UtcNow.ToString("o");
        var createdAt = existing is not null && existing.RootElement.TryGetProperty("created_at", out var c)
            ? c.GetString() ?? now
            : now;

        var doc = new Dictionary<string, object?>
        {
            ["_key"] = arangoKey,
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
            ["created_at"] = createdAt,
            ["updated_at"] = now,
        };

        await UpsertRawDocumentAsync(collection, doc, ct);
        await EnsureVectorIndexAsync(collection, ct);
        return new UpsertNoteResult(note.Key, VectorWriteOutcome.Embedded);
    }

    public async Task<JsonDocument?> ReadNoteDocumentAsync(string collection, string noteKey, CancellationToken ct = default)
    {
        var arangoKey = Sha1Hex(noteKey);
        return await ReadDocumentByKeyAsync(collection, arangoKey, ct);
    }

    public async Task<int> DeleteStaleNotesAsync(
        IReadOnlyList<string> currentKeys,
        string tenant,
        CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);

        int totalDeleted = 0;
        foreach (var collection in new[]
        {
            MemoryCollections.Observations,
            MemoryCollections.Facts,
            MemoryCollections.Decisions,
        })
        {
            var aql = $@"
                FOR d IN {collection}
                  FILTER d.tenant_id == @tenant
                  FILTER d.source == ""obsidian""
                  FILTER d.note_key NOT IN @currentKeys
                  REMOVE d IN {collection}
                  RETURN OLD";
            var bindVars = new Dictionary<string, object>
            {
                ["tenant"] = tenant,
                ["currentKeys"] = currentKeys,
            };
            var cursor = await _arango.Cursor.PostCursorAsync<object>(
                new ArangoDBNetStandard.CursorApi.Models.PostCursorBody
                {
                    Query = aql,
                    BindVars = bindVars,
                });
            totalDeleted += cursor.Result?.Count() ?? 0;
        }
        return totalDeleted;
    }

    // Hex SHA-1 digest, used to derive Arango `_key` values from human-readable
    // note keys (paths with slashes are not valid Arango keys).
    private static string Sha1Hex(string s)
    {
        using var sha = System.Security.Cryptography.SHA1.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // Plain hex SHA-256 digest. Distinct from the post-side `ComputeHash` helper, which
    // returns `"sha256:" + hex` of `$"{modelId}:{text}"` for legacy compatibility with
    // the posts cache schema. Notes hash `$"{modelId}\n{text}"` without the prefix.
    private static string Sha256Hex(string s)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public async Task<int> DeleteStalePostsAsync(
        IReadOnlyCollection<(string Collection, string Slug)> currentPosts,
        IReadOnlyCollection<string>? scopedCollections = null,
        CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);

        var pairs = currentPosts.Select(p => $"{p.Collection}__{p.Slug}").ToArray();
        var scope = scopedCollections?.ToArray() ?? Array.Empty<string>();
        var useScope = scope.Length > 0;

        var aql = useScope ? """
            FOR doc IN @@col
              FILTER doc.tenant_id == "public"
              FILTER doc.collection IN @scope
              FILTER CONCAT(doc.collection, "__", doc.slug) NOT IN @pairs
              REMOVE doc IN @@col
              RETURN OLD._key
            """ : """
            FOR doc IN @@col
              FILTER doc.tenant_id == "public"
              FILTER CONCAT(doc.collection, "__", doc.slug) NOT IN @pairs
              REMOVE doc IN @@col
              RETURN OLD._key
            """;

        var bindVars = new Dictionary<string, object>
        {
            ["@col"] = MemoryCollections.Posts,
            ["pairs"] = pairs,
        };
        if (useScope) bindVars["scope"] = scope;

        var cursor = await _arango.Cursor.PostCursorAsync<string>(
            new ArangoDBNetStandard.CursorApi.Models.PostCursorBody
            {
                Query = aql,
                BindVars = bindVars,
            });
        return cursor.Result.Count();
    }

    public async Task<List<PostSearchHit>> SearchAsync(
        float[] queryVec,
        IReadOnlyList<MemoryKind> kinds,
        IReadOnlyList<string> tenants,
        int rawK,
        CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);
        if (rawK < 1) throw new ArgumentOutOfRangeException(nameof(rawK), "must be >= 1");

        var allHits = new List<PostSearchHit>();

        // Posts.
        //
        // We deliberately use COSINE_SIMILARITY (exact) instead of APPROX_NEAR_COSINE.
        // Rationale: ArangoDB 3.12's APPROX_NEAR_COSINE does NOT support pre-filtering
        // (https://github.com/arangodb/arangodb/issues/21690 — only post-filter is allowed,
        // meaning the LIMIT is applied BEFORE the tenant filter). That makes tenant
        // isolation impossible to guarantee — a tenant with many docs can starve other
        // tenants' results. Pre-filter pushdown lands in ArangoDB 4.0.
        //
        // Exact cosine is O(N) per tenant, but for our workload (≤ 10k vectors per
        // tenant, 768-dim embeddings) this is sub-millisecond on AVX-512 hardware.
        // A trained Faiss IVF index still exists (sparse, on the embedding field) —
        // ready for APPROX_NEAR_COSINE the day pre-filter pushdown is available.
        if (kinds.Contains(MemoryKind.Post))
        {
            var aql = """
                LET q = @query_vec
                FOR doc IN @@col
                  FILTER doc.tenant_id IN @tenants
                  FILTER doc.status == "ready"
                  FILTER doc.vector_kind IN ["summary", "body"]
                  LET sim = COSINE_SIMILARITY(doc.embedding, q)
                  SORT sim DESC
                  LIMIT @raw_k
                  RETURN {
                    key:         doc._key,
                    slug:        doc.slug,
                    collection:  doc.collection,
                    vector_kind: doc.vector_kind,
                    kind:        doc.kind != null ? doc.kind : "post",
                    tenant_id:   doc.tenant_id,
                    title:       doc.title,
                    text:        doc.text,
                    description: doc.description,
                    ai_summary:  doc.ai_summary,
                    pub_date:    doc.pub_date,
                    category:    doc.category,
                    tags:        doc.tags,
                    sim:         sim
                  }
                """;
            var bindVars = new Dictionary<string, object>
            {
                ["@col"] = MemoryCollections.Posts,
                ["query_vec"] = queryVec,
                ["tenants"] = tenants.ToArray(),
                ["raw_k"] = rawK,
            };

            var cursor = await _arango.Cursor.PostCursorAsync<SearchRow>(
                new ArangoDBNetStandard.CursorApi.Models.PostCursorBody
                {
                    Query = aql,
                    BindVars = bindVars,
                });

            allHits.AddRange(cursor.Result.Select(r => new PostSearchHit
            {
                Key = r.key,
                Slug = r.slug ?? "",
                Collection = r.collection ?? "",
                VectorKind = r.vector_kind ?? "body",
                Kind = r.kind ?? "post",
                TenantId = r.tenant_id,
                Title = r.title ?? "",
                Text = r.text ?? "",
                Description = r.description ?? "",
                AiSummary = r.ai_summary,
                PubDate = r.pub_date,
                Category = r.category,
                Tags = r.tags ?? Array.Empty<string>(),
                Sim = r.sim,
            }));
        }

        // Note kinds (observation, fact, decision)
        var noteKindCollections = new List<(MemoryKind kind, string collection)>
        {
            (MemoryKind.Observation, MemoryCollections.Observations),
            (MemoryKind.Fact, MemoryCollections.Facts),
            (MemoryKind.Decision, MemoryCollections.Decisions),
        };
        // See "Posts" branch above for rationale on COSINE_SIMILARITY vs APPROX_NEAR_COSINE.
        foreach (var (kind, collection) in noteKindCollections)
        {
            if (!kinds.Contains(kind)) continue;

            var aql = """
                LET q = @query_vec
                FOR doc IN @@col
                  FILTER doc.tenant_id IN @tenants
                  FILTER doc.status == "ready"
                  LET sim = COSINE_SIMILARITY(doc.embedding, q)
                  SORT sim DESC
                  LIMIT @raw_k
                  RETURN {
                    key:         doc._key,
                    slug:        doc.note_key,
                    collection:  "",
                    vector_kind: doc.kind != null ? doc.kind : @kind_str,
                    kind:        doc.kind != null ? doc.kind : @kind_str,
                    tenant_id:   doc.tenant_id,
                    title:       doc.title,
                    text:        doc.text,
                    description: "",
                    ai_summary:  null,
                    pub_date:    null,
                    category:    null,
                    tags:        [],
                    sim:         sim
                  }
                """;
            var bindVars = new Dictionary<string, object>
            {
                ["@col"] = collection,
                ["query_vec"] = queryVec,
                ["tenants"] = tenants.ToArray(),
                ["raw_k"] = rawK,
                ["kind_str"] = kind.ToString().ToLowerInvariant(),
            };

            var cursor = await _arango.Cursor.PostCursorAsync<SearchRow>(
                new ArangoDBNetStandard.CursorApi.Models.PostCursorBody
                {
                    Query = aql,
                    BindVars = bindVars,
                });

            allHits.AddRange(cursor.Result.Select(r => new PostSearchHit
            {
                Key = r.key,
                Slug = r.slug ?? "",
                Collection = r.collection ?? "",
                VectorKind = r.vector_kind ?? kind.ToString().ToLowerInvariant(),
                Kind = r.kind ?? kind.ToString().ToLowerInvariant(),
                TenantId = r.tenant_id,
                Title = r.title ?? "",
                Text = r.text ?? "",
                Description = r.description ?? "",
                AiSummary = r.ai_summary,
                PubDate = r.pub_date,
                Category = r.category,
                Tags = r.tags ?? Array.Empty<string>(),
                Sim = r.sim,
            }));
        }

        return allHits;
    }

    private sealed record SearchRow(
        string key, string? slug, string? collection, string? vector_kind,
        string? kind, string? tenant_id,
        string? title, string? text, string? description, string? ai_summary,
        string? pub_date, string? category, IReadOnlyList<string>? tags, double sim);

    private static string ComputeHash(string text, string modelId)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"{modelId}:{text}"));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task EnsureCollectionAsync(string name, bool isEdge)
    {
        try
        {
            await _arango.Collection.PostCollectionAsync(new PostCollectionBody
            {
                Name = name,
                Type = isEdge ? CollectionType.Edge : CollectionType.Document
            });
        }
        catch (ApiErrorException ex) when (ex.ApiError.ErrorNum == 1207) { }
    }

    private async Task EnsurePersistentIndexAsync(string collection, string[] fields)
    {
        var url = $"{_baseUrl}/_db/{_db}/_api/index?collection={collection}";
        var body = new { type = "persistent", fields };
        var (ok, errorNum, content) = await PostJsonRawAsync(url, body);
        if (ok || errorNum == 1210 || errorNum == 1207) return;
        throw new InvalidOperationException($"Persistent index creation failed (errorNum={errorNum}): {content}");
    }

    private async Task<long> CountDocumentsAsync(string collection)
    {
        var url = $"{_baseUrl}/_db/{_db}/_api/collection/{collection}/count";
        using var request = BuildAuthedRequest(HttpMethod.Get, url);
        using var response = await _rawHttp.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        return doc.RootElement.GetProperty("count").GetInt64();
    }

    private sealed class IndexEntry
    {
        public string Id { get; init; } = "";
        public string Type { get; init; } = "";
        public string? TrainingState { get; init; }
        public IndexParams? Params { get; init; }
    }

    private sealed class IndexParams
    {
        public int? Dimension { get; init; }
        public int? NLists { get; init; }
        public string? Metric { get; init; }
    }

    private async Task<List<IndexEntry>> ListIndexesAsync(string collection)
    {
        var url = $"{_baseUrl}/_db/{_db}/_api/index?collection={collection}";
        using var request = BuildAuthedRequest(HttpMethod.Get, url);
        using var response = await _rawHttp.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var list = new List<IndexEntry>();
        foreach (var el in doc.RootElement.GetProperty("indexes").EnumerateArray())
        {
            IndexParams? p = null;
            if (el.TryGetProperty("params", out var pe) && pe.ValueKind == JsonValueKind.Object)
            {
                p = new IndexParams
                {
                    Dimension = pe.TryGetProperty("dimension", out var d) ? d.GetInt32() : null,
                    NLists = pe.TryGetProperty("nLists", out var n) ? n.GetInt32() : null,
                    Metric = pe.TryGetProperty("metric", out var m) ? m.GetString() : null
                };
            }
            list.Add(new IndexEntry
            {
                Id = el.GetProperty("id").GetString() ?? "",
                Type = el.GetProperty("type").GetString() ?? "",
                TrainingState = el.TryGetProperty("trainingState", out var ts) ? ts.GetString() : null,
                Params = p
            });
        }
        return list;
    }

    private async Task DeleteIndexAsync(string indexId)
    {
        var url = $"{_baseUrl}/_db/{_db}/_api/index/{indexId}";
        using var request = BuildAuthedRequest(HttpMethod.Delete, url);
        using var response = await _rawHttp.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return;
        response.EnsureSuccessStatusCode();
    }

    private HttpRequestMessage BuildAuthedRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_user}:{_pass}")));
        return request;
    }

    private async Task<(bool ok, int errorNum, string content)> PostJsonRawAsync(string url, object body, HttpMethod? method = null)
    {
        method ??= HttpMethod.Post;
        var request = BuildAuthedRequest(method, url);
        var json = JsonSerializer.Serialize(body);
        request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await _rawHttp.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode) return (true, 0, content);
        int errorNum = 0;
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("errorNum", out var n)) errorNum = n.GetInt32();
        }
        catch { }
        return (false, errorNum, content);
    }

    public async Task<WriteResult> UpsertDecisionAsync(
        string tenantId, string subject, string chose, string because,
        IReadOnlyList<string> alternatives, CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);
        ValidateTenantId(tenantId);
        var text = $"Decision: {subject}. Chose {chose} because {because}. Alternatives considered: {string.Join(", ", alternatives)}";
        var doc = new Dictionary<string, object?>
        {
            ["text"] = text,
            ["tenant_id"] = tenantId,
            ["chose"] = chose,
            ["because"] = because,
            ["alternatives"] = alternatives,
            ["status"] = "pending_embedding",
            ["created_at"] = DateTime.UtcNow.ToString("O"),
            ["updated_at"] = DateTime.UtcNow.ToString("O")
        };
        return await UpsertContentAsync(MemoryCollections.Decisions, text, doc, ct);
    }

    public async Task<WriteResult> UpsertObservationAsync(
        string tenantId, string source, string text, object payload, CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);
        ValidateTenantId(tenantId);
        var doc = new Dictionary<string, object?>
        {
            ["text"] = text,
            ["tenant_id"] = tenantId,
            ["source"] = source,
            ["payload"] = payload,
            ["status"] = "pending_embedding",
            ["created_at"] = DateTime.UtcNow.ToString("O"),
            ["updated_at"] = DateTime.UtcNow.ToString("O")
        };
        return await UpsertContentAsync(MemoryCollections.Observations, text, doc, ct);
    }

    public async Task<WriteResult> UpsertFactAsync(
        string tenantId, string text, string? sourceThread, CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);
        ValidateTenantId(tenantId);
        var doc = new Dictionary<string, object?>
        {
            ["text"] = text,
            ["tenant_id"] = tenantId,
            ["source_thread"] = sourceThread,
            ["status"] = "pending_embedding",
            ["created_at"] = DateTime.UtcNow.ToString("O"),
            ["updated_at"] = DateTime.UtcNow.ToString("O")
        };
        return await UpsertContentAsync(MemoryCollections.Facts, text, doc, ct);
    }

    public async Task<WriteResult> UpsertSummaryAsync(
        string tenantId, string text, string threadId, CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);
        ValidateTenantId(tenantId);
        var doc = new Dictionary<string, object?>
        {
            ["text"] = text,
            ["tenant_id"] = tenantId,
            ["thread_id"] = threadId,
            ["status"] = "pending_embedding",
            ["created_at"] = DateTime.UtcNow.ToString("O"),
            ["updated_at"] = DateTime.UtcNow.ToString("O")
        };
        return await UpsertContentAsync(MemoryCollections.Summaries, text, doc, ct);
    }

    public async Task<string> UpsertEntityAsync(
        string tenantId, string canonicalName, IReadOnlyList<string> aliases, string type, CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);
        ValidateTenantId(tenantId);
        var doc = new Dictionary<string, object?>
        {
            ["canonical_name"] = canonicalName,
            ["aliases"] = aliases,
            ["type"] = type,
            ["tenant_id"] = tenantId,
            ["created_at"] = DateTime.UtcNow.ToString("O")
        };
        var insert = await _arango.Document.PostDocumentAsync(MemoryCollections.Entities, doc);
        return insert._key;
    }

    public async Task<string> UpsertEdgeAsync(
        string tenantId, string fromId, string toId, string kind, double weight, CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);
        ValidateTenantId(tenantId);
        var doc = new Dictionary<string, object?>
        {
            ["_from"] = fromId,
            ["_to"] = toId,
            ["kind"] = kind,
            ["weight"] = weight,
            ["tenant_id"] = tenantId,
            ["created_at"] = DateTime.UtcNow.ToString("O")
        };
        var insert = await _arango.Document.PostDocumentAsync(MemoryCollections.Edges, doc);
        return insert._key;
    }

    /// <summary>
    /// Generic AQL query helper used by MemoryRecallEngine.
    /// Ensures schema (cached) before issuing the cursor request.
    /// </summary>
    public async Task<List<T>> QueryAsync<T>(string aql, Dictionary<string, object> bindVars, CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);
        var cursor = await _arango.Cursor.PostCursorAsync<T>(
            new ArangoDBNetStandard.CursorApi.Models.PostCursorBody { Query = aql, BindVars = bindVars });
        return cursor.Result.ToList();
    }

    public async Task<List<(string id, string targetCollection, string targetKey)>> ListPendingEmbeddingsAsync(int limit = 100, CancellationToken ct = default)
    {
        await EnsureSchemaIfNeededAsync(ct);
        var aql = "FOR p IN @@col SORT p.queued_at ASC LIMIT @limit RETURN { id: p._key, targetCollection: p.target_collection, targetKey: p.target_key }";
        var bindVars = new Dictionary<string, object> { ["@col"] = MemoryCollections.PendingEmbeddings, ["limit"] = limit };
        var cursor = await _arango.Cursor.PostCursorAsync<PendingEmbeddingRow>(
            new ArangoDBNetStandard.CursorApi.Models.PostCursorBody { Query = aql, BindVars = bindVars });
        return cursor.Result.Select(r => (r.id, r.targetCollection, r.targetKey)).ToList();
    }

    private sealed record PendingEmbeddingRow(string id, string targetCollection, string targetKey);

    private async Task<WriteResult> UpsertContentAsync(string collection, string text, Dictionary<string, object?> doc, CancellationToken ct)
    {
        var insert = await _arango.Document.PostDocumentAsync(collection, doc);
        var key = insert._key;

        if (_embeddings is null) return WriteResult.Pending(key);
        try
        {
            var emb = await _embeddings.EmbedAsync(text, ct);
            var update = new Dictionary<string, object?>
            {
                ["embedding"] = emb,
                ["status"] = "ready",
                ["updated_at"] = DateTime.UtcNow.ToString("O")
            };
            await _arango.Document.PatchDocumentAsync<Dictionary<string, object?>, Dictionary<string, object?>>(
                collection, key, update);
            await EnsureVectorIndexAsync(collection, ct);
            return WriteResult.Ready(key);
        }
        catch
        {
            await EnqueuePendingEmbeddingAsync(collection, key);
            return WriteResult.Pending(key);
        }
    }

    private async Task EnqueuePendingEmbeddingAsync(string targetCollection, string targetKey)
    {
        var doc = new Dictionary<string, object?>
        {
            ["target_collection"] = targetCollection,
            ["target_key"] = targetKey,
            ["attempts"] = 0,
            ["queued_at"] = DateTime.UtcNow.ToString("O")
        };
        await _arango.Document.PostDocumentAsync(MemoryCollections.PendingEmbeddings, doc);
    }

    private static void ValidateTenantId(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new InvalidOperationException("Tenant ID must be a non-empty string.");
    }

    public void Dispose()
    {
        _arango.Dispose();
        _transport.Dispose();
    }
}
