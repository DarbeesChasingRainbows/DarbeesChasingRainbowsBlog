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

    public async Task EnsureVectorIndexAsync(string collection, CancellationToken ct = default)
    {
        if (_vectorIndexReady.TryGetValue(collection, out var cached) && cached) return;

        var indexes = await ListIndexesAsync(collection);

        foreach (var idx in indexes.Where(i => i.Type == "vector" && i.TrainingState != "ready"))
        {
            await DeleteIndexAsync(idx.Id);
        }

        if (indexes.Any(i => i.Type == "vector"
            && i.TrainingState == "ready"
            && i.Params?.Dimension == _embeddingDimension
            && i.Params?.NLists == _vectorNLists))
        {
            _vectorIndexReady[collection] = true;
            return;
        }

        var docCount = await CountDocumentsAsync(collection);
        if (docCount < _vectorNLists) return;

        var url = $"{_baseUrl}/_db/{_db}/_api/index?collection={collection}";
        var body = new
        {
            type = "vector",
            fields = new[] { "embedding" },
            @params = new { dimension = _embeddingDimension, metric = "cosine", nLists = _vectorNLists }
        };
        var (ok, errorNum, content) = await PostJsonRawAsync(url, body);
        if (ok)
        {
            _vectorIndexReady[collection] = true;
            return;
        }
        if (errorNum == 1555) return;
        throw new InvalidOperationException($"Vector index creation failed (errorNum={errorNum}) on '{collection}': {content}");
    }

    public async Task<bool> HasUsableVectorIndexAsync(string collection)
    {
        var indexes = await ListIndexesAsync(collection);
        return indexes.Any(i => i.Type == "vector" && i.TrainingState == "ready");
    }

    public async Task<int> CountVectorIndexesAsync(string collection)
    {
        var indexes = await ListIndexesAsync(collection);
        return indexes.Count(i => i.Type == "vector");
    }

    public async Task<List<string>> ListCollectionsAsync()
    {
        var result = await _arango.Collection.GetCollectionsAsync();
        return result.Result.Select(c => c.Name).ToList();
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

    public async Task<List<(string id, string targetCollection, string targetKey)>> ListPendingEmbeddingsAsync(int limit = 100)
    {
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
