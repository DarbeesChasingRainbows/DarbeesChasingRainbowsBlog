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
    private readonly int _embeddingDimension;
    private readonly int _vectorNLists;
    private readonly IEmbeddingClient? _embeddings;
    private readonly ConcurrentDictionary<string, bool> _vectorIndexReady = new();

    public MemoryStore(string url, string db, string user, string pass, int embeddingDimension, int vectorNLists, HttpClient rawHttp, IEmbeddingClient? embeddings = null)
    {
        _baseUrl = url.TrimEnd('/');
        _db = db;
        _user = user;
        _pass = pass;
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
            MemoryCollections.PendingEmbeddings
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

    private async Task<(bool ok, int errorNum, string content)> PostJsonRawAsync(string url, object body)
    {
        var request = BuildAuthedRequest(HttpMethod.Post, url);
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

    public void Dispose()
    {
        _arango.Dispose();
        _transport.Dispose();
    }
}
