# Graph-Backed RAG Implementation Plan

> **2026-05-17 — Stack drift update:** This work was paused at task A6. Since then:
> - `LmStudioEmbeddingClient` has been renamed to `OpenAiCompatibleEmbeddingClient` (same behavior, accurate name).
> - The embedding stack switched from LM Studio + `nomic-embed-text-v1.5` (768-dim) to llama.cpp + `qwen3-embedding-8b` (4096-dim).
> - The bridge now reads `LLM_CHAT_URL` and `LLM_EMBEDDING_URL` (split). `LMSTUDIO_URL` is back-compat with a deprecation warning.
> - `EnsureSchemaAsync` is now invoked lazily (first-use) rather than at startup, so the new `POST /api/admin/migrate-embeddings` endpoint stays reachable during config-mismatch states.
> - Posts are now stored as `MemoryKind.Post` in a `memory_posts` collection. See [`docs/superpowers/specs/2026-05-16-content-rag-design.md`](specs/2026-05-16-content-rag-design.md).
> - `MemoryPlugin` (kernel functions `RememberDecision`/`RememberObservation`/`LinkMemory`) was added in parallel via commit `131f509`, advancing Phase 11 B2.
>
> When resuming Phase 11 task B3 and beyond, the above is the current state. `MemoryStore` and `IEmbeddingClient` references in the original spec/plan below are correct in spirit; just substitute the new class name and config keys.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the stubbed `ArangoPlugin` with a real `MemoryPlugin` backed by a hybrid graph + vector ArangoDB store, enabling cross-session recall of architectural decisions, observations, and conversation facts in DAIS Bridge.

**Architecture:** Layered SK 1.75 memory model — built-in `WhiteboardProvider` (short-term, no code), new `DarbeesContextProvider : AIContextProvider` (auto long-term extract), and explicit `MemoryPlugin` kernel functions — all backed by a single `MemoryStore` that does Arango I/O. Hybrid recall = entity extraction → 1-hop graph expansion → vector top-K rerank, single database, normalized collections per content kind, `tenant_id` field for isolation.

**Tech Stack:** C# .NET 9, Microsoft.SemanticKernel 1.75.0, ArangoDBNetStandard 2.0.0, ArangoDB 3.12.x (current stable; vector index ships as an experimental feature — v4 with first-class vector index is in development and not yet released), LM Studio (`/v1/embeddings`), xUnit 2.9.3.

**Spec:** [docs/superpowers/specs/2026-05-09-graph-backed-rag-design.md](../specs/2026-05-09-graph-backed-rag-design.md)

---

## Pre-flight

- [ ] **Verify build is green at HEAD.**

```bash
dotnet build dais-bridge/dais-bridge.csproj
dotnet test dais-bridge.tests/dais-bridge.tests.csproj
```

Expected: build succeeds; 11 tests pass.

- [ ] **Verify ArangoDB is available locally for integration tests.**

The official Docker image is `arangodb`. Use `arangodb:3.12` for major-version pinning or `arangodb:latest` (currently aliases to the 3.12.x stable line). v4 is in development and not published.

**The `--vector-index` startup flag is required.** Without it, `POST /_api/index` with `type: "vector"` returns `400 / errorNum 10 "vector index feature is not enabled"`. `--experimental-vector-index` is a deprecated alias that logs a rename warning but still works.

```bash
docker run -d --name arango-test -e ARANGO_ROOT_PASSWORD=password -p 8529:8529 arangodb:3.12 --vector-index
```

Wait ~10s, then:

```bash
curl -u root:password http://localhost:8529/_api/version
```

Expected: JSON response with `"version": "3.12.x"`. If the response is below 3.12, upgrade — earlier minor versions don't expose the experimental vector index API.

- [ ] **Verify LM Studio is running with an embedding model loaded AND obtain its API token.**

LM Studio now requires `Authorization: Bearer <token>` on all `/v1/*` calls. Generate a token in LM Studio's developer settings (or copy the existing one). Save it in `appsettings.json` as `AI:LMStudioApiKey` — see Task A6 — or as the `LMSTUDIO_API_KEY` environment variable (overrides config). Without this, every embedding call returns `invalid_api_key`.

In LM Studio, load `nomic-embed-text-v1.5` (768-dim) on the server. Then:

```bash
$env:LMSTUDIO_API_KEY="<your-token>"
curl http://localhost:1234/v1/embeddings -H "Content-Type: application/json" -H "Authorization: Bearer $env:LMSTUDIO_API_KEY" -d "{\"model\":\"nomic-embed-text-v1.5\",\"input\":\"hello\"}"
```

Expected: JSON response with a 768-element `data[0].embedding` array.

If embeddings return a different dimension, update the `EmbeddingDimension` config value used throughout the plan.

---

## Phase A — Substrate

Goal: ship `Memory/Models`, `IEmbeddingClient`, `LmStudioEmbeddingClient`, and `MemoryStore` with schema migration + write paths. No recall, no plugin yet.

### Task A1: Add Memory namespace and model records

**Files:**
- Create: `dais-bridge/Memory/Models/MemoryItem.cs`
- Create: `dais-bridge/Memory/Models/MemoryEdge.cs`
- Create: `dais-bridge/Memory/Models/WriteResult.cs`
- Create: `dais-bridge/Memory/Models/RecallResult.cs`
- Create: `dais-bridge/Memory/Models/MemoryKind.cs`

- [ ] **Step 1: Create `MemoryKind.cs`.**

```csharp
namespace Darbee.Gateway.Memory.Models;

public enum MemoryKind
{
    Decision,
    Observation,
    Fact,
    Summary,
    Entity
}

public static class MemoryCollections
{
    public const string Decisions = "memory_decisions";
    public const string Observations = "memory_observations";
    public const string Facts = "memory_facts";
    public const string Summaries = "memory_summaries";
    public const string Entities = "memory_entities";
    public const string Edges = "memory_edges";
    public const string PendingEmbeddings = "memory_pending_embeddings";

    public static string ForKind(MemoryKind kind) => kind switch
    {
        MemoryKind.Decision => Decisions,
        MemoryKind.Observation => Observations,
        MemoryKind.Fact => Facts,
        MemoryKind.Summary => Summaries,
        MemoryKind.Entity => Entities,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
```

- [ ] **Step 2: Create `MemoryItem.cs`.**

```csharp
namespace Darbee.Gateway.Memory.Models;

public record MemoryItem(
    string Key,
    MemoryKind Kind,
    string Text,
    float[]? Embedding,
    string TenantId,
    string Status,            // "ready" | "pending_embedding"
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Dictionary<string, object>? Metadata = null);
```

- [ ] **Step 3: Create `MemoryEdge.cs`.**

```csharp
namespace Darbee.Gateway.Memory.Models;

public record MemoryEdge(
    string Key,
    string From,              // collection/key form: "memory_decisions/abc123"
    string To,
    string Kind,              // "mentions" | "depends-on" | "supersedes" | "tagged" | "about-file"
    double Weight,
    string TenantId,
    DateTime CreatedAt);
```

- [ ] **Step 4: Create `WriteResult.cs`.**

```csharp
namespace Darbee.Gateway.Memory.Models;

public record WriteResult(string Id, bool Completed, bool Queued)
{
    public static WriteResult Ready(string id) => new(id, Completed: true, Queued: false);
    public static WriteResult Pending(string id) => new(id, Completed: false, Queued: true);
}
```

- [ ] **Step 5: Create `RecallResult.cs`.**

```csharp
namespace Darbee.Gateway.Memory.Models;

public record ScoredMemoryItem(
    MemoryItem Item,
    double Cosine,
    double Proximity,
    double Score,
    int? HopsFromQuery,
    IReadOnlyList<string> PathEntityKeys);

public record RecallResult(
    IReadOnlyList<ScoredMemoryItem> Items,
    IReadOnlyList<string> ExtractedEntityIds);
```

- [ ] **Step 6: Verify build.**

```bash
dotnet build dais-bridge/dais-bridge.csproj
```

Expected: SUCCESS, no warnings.

- [ ] **Step 7: Commit.**

```bash
git add dais-bridge/Memory/Models/
git commit -m "feat(memory): add Memory model records and collection name registry"
```

---

### Task A2: IEmbeddingClient interface + failing test

**Files:**
- Create: `dais-bridge/Memory/IEmbeddingClient.cs`
- Create: `dais-bridge.tests/Memory/LmStudioEmbeddingClientTests.cs`

- [ ] **Step 1: Create the interface.**

```csharp
namespace Darbee.Gateway.Memory;

public interface IEmbeddingClient
{
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
    int Dimension { get; }
}
```

- [ ] **Step 2: Write a failing test for `LmStudioEmbeddingClient` (does not exist yet).**

```csharp
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Darbee.Gateway.Memory;

namespace Darbee.Gateway.Tests.Memory;

public class LmStudioEmbeddingClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    [Fact]
    public async Task EmbedAsync_PostsExpectedShapeAndParsesFloatArray()
    {
        var responseJson = "{\"data\":[{\"embedding\":[0.1,0.2,0.3,0.4]}]}";
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler);
        var client = new LmStudioEmbeddingClient(http, "http://localhost:1234/v1", "nomic-embed-text-v1.5", expectedDimension: 4);

        var result = await client.EmbedAsync("hello world");

        Assert.Equal(new float[] { 0.1f, 0.2f, 0.3f, 0.4f }, result);
        Assert.Single(handler.Requests);
        var sent = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.EndsWith("/v1/embeddings", sent.RequestUri!.ToString());
        var bodyJson = await sent.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(bodyJson);
        Assert.Equal("nomic-embed-text-v1.5", doc.RootElement.GetProperty("model").GetString());
        Assert.Equal("hello world", doc.RootElement.GetProperty("input").GetString());
    }
}
```

- [ ] **Step 3: Run test — expect failure.**

```bash
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter "FullyQualifiedName~LmStudioEmbeddingClientTests"
```

Expected: FAIL with "type or namespace name 'LmStudioEmbeddingClient' could not be found".

- [ ] **Step 4: Commit interface + failing test.**

```bash
git add dais-bridge/Memory/IEmbeddingClient.cs dais-bridge.tests/Memory/LmStudioEmbeddingClientTests.cs
git commit -m "test(memory): add failing LmStudioEmbeddingClient test and IEmbeddingClient interface"
```

---

### Task A3: LmStudioEmbeddingClient implementation

**Files:**
- Create: `dais-bridge/Memory/LmStudioEmbeddingClient.cs`

- [ ] **Step 1: Implement.**

```csharp
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Darbee.Gateway.Memory;

public sealed class LmStudioEmbeddingClient : IEmbeddingClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _modelId;

    public int Dimension { get; }

    private readonly string? _apiKey;

    public LmStudioEmbeddingClient(HttpClient http, string baseUrl, string modelId, int expectedDimension, string? apiKey = null)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _modelId = modelId;
        _apiKey = apiKey;
        Dimension = expectedDimension;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var batch = await EmbedBatchAsync(new[] { text }, cancellationToken);
        return batch[0];
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        var url = $"{_baseUrl}/embeddings";
        var body = new EmbeddingRequest(_modelId, texts.Count == 1 ? (object)texts[0] : texts);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        }
        var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var parsed = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Embedding response was null.");
        var vectors = parsed.Data.Select(d => d.Embedding).ToArray();
        foreach (var v in vectors)
        {
            if (v.Length != Dimension)
            {
                throw new InvalidOperationException(
                    $"Embedding dimension mismatch: configured {Dimension}, received {v.Length}. " +
                    $"Reload the embedding model or update appsettings.");
            }
        }
        return vectors;
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] object Input);

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("data")] List<EmbeddingData> Data);

    private sealed record EmbeddingData(
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
```

- [ ] **Step 2: Run test — expect pass.**

```bash
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter "FullyQualifiedName~LmStudioEmbeddingClientTests"
```

Expected: PASS.

- [ ] **Step 3: Add a second test for batch and dimension mismatch.**

Append to `LmStudioEmbeddingClientTests.cs`:

```csharp
    [Fact]
    public async Task EmbedAsync_ThrowsOnDimensionMismatch()
    {
        var responseJson = "{\"data\":[{\"embedding\":[0.1,0.2]}]}"; // 2 dims, expecting 4
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler);
        var client = new LmStudioEmbeddingClient(http, "http://localhost:1234/v1", "nomic-embed-text-v1.5", expectedDimension: 4);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.EmbedAsync("x"));
    }

    [Fact]
    public async Task EmbedBatchAsync_SendsArrayInputAndReturnsEachVector()
    {
        var responseJson = "{\"data\":[{\"embedding\":[0.1,0.2]},{\"embedding\":[0.3,0.4]}]}";
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler);
        var client = new LmStudioEmbeddingClient(http, "http://localhost:1234/v1", "test-model", expectedDimension: 2);

        var result = await client.EmbedBatchAsync(new[] { "a", "b" });

        Assert.Equal(2, result.Count);
        Assert.Equal(new float[] { 0.1f, 0.2f }, result[0]);
        Assert.Equal(new float[] { 0.3f, 0.4f }, result[1]);
        var bodyJson = await handler.Requests[0].Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(bodyJson);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("input").ValueKind);
    }
```

- [ ] **Step 4: Run all embedding tests.**

```bash
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter "FullyQualifiedName~LmStudioEmbeddingClientTests"
```

Expected: 3 PASS.

- [ ] **Step 5: Commit.**

```bash
git add dais-bridge/Memory/LmStudioEmbeddingClient.cs dais-bridge.tests/Memory/LmStudioEmbeddingClientTests.cs
git commit -m "feat(memory): implement LmStudioEmbeddingClient with batch + dim validation"
```

---

### Task A4: MemoryStore — schema migration + lazy vector index

**Files:**
- Create: `dais-bridge/Memory/MemoryStore.cs`
- Create: `dais-bridge.tests/Memory/MemoryStoreSchemaTests.cs`
- Create: `dais-bridge.tests/Memory/MemoryStoreVectorIndexTests.cs`

ArangoDBNetStandard 2.0.0 typed API does not expose the vector index. The vector index is created via raw HTTP `POST /_api/index?collection=<name>` with a JSON body. `MemoryStore` will own a `HttpClient` for these escape-hatch calls in addition to the `ArangoDBClient` for typed operations.

**Vector-index constraints (empirically verified 2026-05-12 — see spec §10.2):**

- Body shape: `type: "vector"`, `fields: ["embedding"]`, `params: { dimension, metric, nLists }`.
- Cold start: vector index POST on an empty / under-trained collection returns `500 / errorNum 1555 "vector index not ready"`, **but persists an unusable index entry that AQL will prefer over later good ones** — must be cleaned up.
- `nLists` must be ≤ document count. Configurable via `Memory:VectorNLists` (default 1 for dev/test).
- AQL similarity: `APPROX_NEAR_COSINE`. Must be bound via `LET sim = APPROX_NEAR_COSINE(...)` and reused — two direct calls in one query → errorNum 1554.
- Server requires `--vector-index` startup flag (see Pre-flight); without it POST returns errorNum 10.

**Design: lazy vector index.** `EnsureSchemaAsync` creates collections + non-vector indexes only. A new public `EnsureVectorIndexAsync(collection)` is called from write paths (Task A5) after each successful insert. It is idempotent and caches "usable index exists" per collection in memory after first observation, so the steady-state cost is one cache check.

- [ ] **Step 1: Write integration test for schema migration (collections + non-vector indexes only).**

```csharp
using System.Net.Http;
using ArangoDBNetStandard;
using ArangoDBNetStandard.Transport.Http;
using Darbee.Gateway.Memory;

namespace Darbee.Gateway.Tests.Memory;

[Trait("Category", "Integration")]
public class MemoryStoreSchemaTests
{
    private static readonly string TestDbBase = "darbees_memory_test";

    private static string ArangoUrl =>
        Environment.GetEnvironmentVariable("ARANGO_TEST_URL") ?? "http://localhost:8529";

    private static string ArangoUser =>
        Environment.GetEnvironmentVariable("ARANGO_TEST_USER") ?? "root";

    private static string ArangoPass =>
        Environment.GetEnvironmentVariable("ARANGO_TEST_PASS") ?? "password";

    private static bool ArangoEnabled =>
        Environment.GetEnvironmentVariable("ARANGO_TEST_URL") != null
        || Environment.GetEnvironmentVariable("ARANGO_TEST_RUN") == "1";

    private static async Task<string> CreateUniqueDb()
    {
        var dbName = $"{TestDbBase}_{Guid.NewGuid():N}";
        var rootTransport = HttpApiTransport.UsingBasicAuth(new Uri(ArangoUrl), "_system", ArangoUser, ArangoPass);
        using var rootClient = new ArangoDBClient(rootTransport);
        await rootClient.Database.PostDatabaseAsync(new ArangoDBNetStandard.DatabaseApi.Models.PostDatabaseBody { Name = dbName });
        return dbName;
    }

    private static async Task DropDb(string dbName)
    {
        var rootTransport = HttpApiTransport.UsingBasicAuth(new Uri(ArangoUrl), "_system", ArangoUser, ArangoPass);
        using var rootClient = new ArangoDBClient(rootTransport);
        try { await rootClient.Database.DeleteDatabaseAsync(dbName); } catch { }
    }

    [Fact]
    public async Task EnsureSchemaAsync_CreatesAllCollectionsAndPersistentIndexes_Idempotent()
    {
        if (!ArangoEnabled) return;
        var dbName = await CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass, embeddingDimension: 768, vectorNLists: 1, http);

            await store.EnsureSchemaAsync();
            await store.EnsureSchemaAsync();

            var collections = await store.ListCollectionsAsync();
            Assert.Contains("memory_decisions", collections);
            Assert.Contains("memory_observations", collections);
            Assert.Contains("memory_facts", collections);
            Assert.Contains("memory_summaries", collections);
            Assert.Contains("memory_entities", collections);
            Assert.Contains("memory_edges", collections);
            Assert.Contains("memory_pending_embeddings", collections);
        }
        finally
        {
            await DropDb(dbName);
        }
    }
}
```

- [ ] **Step 2: Write integration test for lazy vector index lifecycle.**

```csharp
using System.Net.Http;
using System.Net.Http.Json;
using ArangoDBNetStandard;
using ArangoDBNetStandard.Transport.Http;
using Darbee.Gateway.Memory;

namespace Darbee.Gateway.Tests.Memory;

[Trait("Category", "Integration")]
public class MemoryStoreVectorIndexTests
{
    private static readonly string TestDbBase = "darbees_memory_test";

    private static string ArangoUrl =>
        Environment.GetEnvironmentVariable("ARANGO_TEST_URL") ?? "http://localhost:8529";
    private static string ArangoUser =>
        Environment.GetEnvironmentVariable("ARANGO_TEST_USER") ?? "root";
    private static string ArangoPass =>
        Environment.GetEnvironmentVariable("ARANGO_TEST_PASS") ?? "password";
    private static bool ArangoEnabled =>
        Environment.GetEnvironmentVariable("ARANGO_TEST_URL") != null
        || Environment.GetEnvironmentVariable("ARANGO_TEST_RUN") == "1";

    private static async Task<string> CreateUniqueDb()
    {
        var dbName = $"{TestDbBase}_{Guid.NewGuid():N}";
        var rootTransport = HttpApiTransport.UsingBasicAuth(new Uri(ArangoUrl), "_system", ArangoUser, ArangoPass);
        using var rootClient = new ArangoDBClient(rootTransport);
        await rootClient.Database.PostDatabaseAsync(new ArangoDBNetStandard.DatabaseApi.Models.PostDatabaseBody { Name = dbName });
        return dbName;
    }

    private static async Task DropDb(string dbName)
    {
        var rootTransport = HttpApiTransport.UsingBasicAuth(new Uri(ArangoUrl), "_system", ArangoUser, ArangoPass);
        using var rootClient = new ArangoDBClient(rootTransport);
        try { await rootClient.Database.DeleteDatabaseAsync(dbName); } catch { }
    }

    private static async Task InsertDocAsync(HttpClient http, string baseUrl, string db, string collection, string user, string pass, int dim)
    {
        var url = $"{baseUrl}/_db/{db}/_api/document/{collection}";
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{user}:{pass}")));
        request.Content = JsonContent.Create(new { embedding = Enumerable.Repeat(0.1f, dim).ToArray() });
        (await http.SendAsync(request)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task EnsureVectorIndexAsync_NoOps_WhenCollectionHasFewerDocsThanNLists()
    {
        if (!ArangoEnabled) return;
        var dbName = await CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass, embeddingDimension: 768, vectorNLists: 5, http);
            await store.EnsureSchemaAsync();

            await store.EnsureVectorIndexAsync("memory_decisions");

            var hasVectorIndex = await store.HasUsableVectorIndexAsync("memory_decisions");
            Assert.False(hasVectorIndex);
        }
        finally { await DropDb(dbName); }
    }

    [Fact]
    public async Task EnsureVectorIndexAsync_CreatesUsableIndex_WhenDocsMeetThreshold()
    {
        if (!ArangoEnabled) return;
        var dbName = await CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass, embeddingDimension: 768, vectorNLists: 1, http);
            await store.EnsureSchemaAsync();

            await InsertDocAsync(http, ArangoUrl, dbName, "memory_decisions", ArangoUser, ArangoPass, 768);

            await store.EnsureVectorIndexAsync("memory_decisions");

            Assert.True(await store.HasUsableVectorIndexAsync("memory_decisions"));
        }
        finally { await DropDb(dbName); }
    }

    [Fact]
    public async Task EnsureVectorIndexAsync_CleansUpUnusableIndexes_BeforeRetrying()
    {
        if (!ArangoEnabled) return;
        var dbName = await CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass, embeddingDimension: 768, vectorNLists: 1, http);
            await store.EnsureSchemaAsync();

            // First attempt on empty collection — leaves an unusable index entry.
            await store.EnsureVectorIndexAsync("memory_decisions");
            Assert.False(await store.HasUsableVectorIndexAsync("memory_decisions"));

            // Insert a doc, retry — must clean up unusable index and create a fresh usable one.
            await InsertDocAsync(http, ArangoUrl, dbName, "memory_decisions", ArangoUser, ArangoPass, 768);
            await store.EnsureVectorIndexAsync("memory_decisions");

            Assert.True(await store.HasUsableVectorIndexAsync("memory_decisions"));
            Assert.Equal(1, await store.CountVectorIndexesAsync("memory_decisions"));
        }
        finally { await DropDb(dbName); }
    }
}
```

- [ ] **Step 3: Run tests — expect failure (MemoryStore does not exist).**

```bash
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter "FullyQualifiedName~MemoryStore"
```

Expected: FAIL — `MemoryStore` not found.

- [ ] **Step 4: Implement `MemoryStore`.**

```csharp
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
        var (ok, errorNum, _) = await PostJsonRawAsync(url, body);
        if (ok)
        {
            _vectorIndexReady[collection] = true;
            return;
        }
        if (errorNum == 1555) return;
        throw new InvalidOperationException($"Vector index creation failed (errorNum={errorNum}) on '{collection}'.");
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
                Type = isEdge ? 3 : 2
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
        request.Content = JsonContent.Create(body);
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
```

- [ ] **Step 5: Run tests against running ArangoDB.**

```bash
export ARANGO_TEST_RUN=1
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter "FullyQualifiedName~MemoryStore"
```

Expected: 4 PASS (1 schema + 3 vector index). If FAIL, verify ArangoDB 3.12 is running with `--vector-index` (see Pre-flight) and `ARANGO_TEST_URL` points at it.

- [ ] **Step 6: Commit.**

```bash
git add dais-bridge/Memory/MemoryStore.cs dais-bridge.tests/Memory/MemoryStoreSchemaTests.cs dais-bridge.tests/Memory/MemoryStoreVectorIndexTests.cs
git commit -m "feat(memory): MemoryStore schema + lazy vector index lifecycle"
```

---

### Task A5: MemoryStore — write paths

**Files:**
- Modify: `dais-bridge/Memory/MemoryStore.cs`
- Create: `dais-bridge.tests/Memory/MemoryStoreWriteTests.cs`

- [ ] **Step 1: Write integration test for content write + pending-embedding fallback.**

```csharp
using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;

namespace Darbee.Gateway.Tests.Memory;

[Trait("Category", "Integration")]
public class MemoryStoreWriteTests
{
    private sealed class ConstantEmbeddingClient(int dim, float[]? value = null) : IEmbeddingClient
    {
        public int Dimension => dim;
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
            => Task.FromResult(value ?? Enumerable.Repeat(0.1f, dim).ToArray());
        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => value ?? Enumerable.Repeat(0.1f, dim).ToArray()).ToArray());
    }

    private sealed class FailingEmbeddingClient(int dim) : IEmbeddingClient
    {
        public int Dimension => dim;
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
            => throw new HttpRequestException("LM Studio unavailable");
        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            => throw new HttpRequestException("LM Studio unavailable");
    }

    [Fact]
    public async Task UpsertDecisionAsync_WhenEmbeddingSucceeds_ReturnsCompleted()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDbStatic();
        try
        {
            using var http = new HttpClient();
            var store = new MemoryStore(MemoryStoreSchemaTests.ArangoUrlStatic, dbName,
                MemoryStoreSchemaTests.ArangoUserStatic, MemoryStoreSchemaTests.ArangoPassStatic,
                embeddingDimension: 4, vectorNLists: 1, http, new ConstantEmbeddingClient(4));
            await store.EnsureSchemaAsync();

            var result = await store.UpsertDecisionAsync(
                tenantId: "admin",
                subject: "PostCard kind union",
                chose: "discriminated union",
                because: "type narrowing in TS6",
                alternatives: new[] { "polymorphic", "any" });

            Assert.True(result.Completed);
            Assert.False(result.Queued);
            Assert.False(string.IsNullOrEmpty(result.Id));
        }
        finally { await MemoryStoreSchemaTests.DropDbStatic(dbName); }
    }

    [Fact]
    public async Task UpsertDecisionAsync_WhenEmbeddingFails_QueuesPending()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDbStatic();
        try
        {
            using var http = new HttpClient();
            var store = new MemoryStore(MemoryStoreSchemaTests.ArangoUrlStatic, dbName,
                MemoryStoreSchemaTests.ArangoUserStatic, MemoryStoreSchemaTests.ArangoPassStatic,
                embeddingDimension: 4, vectorNLists: 1, http, new FailingEmbeddingClient(4));
            await store.EnsureSchemaAsync();

            var result = await store.UpsertDecisionAsync(
                tenantId: "admin", subject: "x", chose: "a", because: "b", alternatives: Array.Empty<string>());

            Assert.False(result.Completed);
            Assert.True(result.Queued);

            var pending = await store.ListPendingEmbeddingsAsync();
            Assert.Single(pending);
            Assert.Equal(MemoryCollections.Decisions, pending[0].targetCollection);
        }
        finally { await MemoryStoreSchemaTests.DropDbStatic(dbName); }
    }
}
```

This test references helpers that need to be exposed; refactor `MemoryStoreSchemaTests` first to make them static and reusable.

- [ ] **Step 2: Refactor `MemoryStoreSchemaTests` to expose static helpers.**

Replace the `private static readonly`/`private static` accessors with `internal static` so the new test class can use them, and rename `CreateUniqueDb` → `CreateUniqueDbStatic`, `DropDb` → `DropDbStatic`. Also expose `ArangoUrlStatic`, `ArangoUserStatic`, `ArangoPassStatic`, `ArangoEnabled` (already internal-ready). Concrete patch:

```csharp
// In MemoryStoreSchemaTests.cs — change all private statics to internal statics:
internal static string ArangoUrlStatic =>
    Environment.GetEnvironmentVariable("ARANGO_TEST_URL") ?? "http://localhost:8529";
internal static string ArangoUserStatic =>
    Environment.GetEnvironmentVariable("ARANGO_TEST_USER") ?? "root";
internal static string ArangoPassStatic =>
    Environment.GetEnvironmentVariable("ARANGO_TEST_PASS") ?? "password";
internal static bool ArangoEnabled =>
    Environment.GetEnvironmentVariable("ARANGO_TEST_URL") != null
    || Environment.GetEnvironmentVariable("ARANGO_TEST_RUN") == "1";

internal static async Task<string> CreateUniqueDbStatic()
{
    var dbName = $"{TestDbBase}_{Guid.NewGuid():N}";
    var rootTransport = HttpApiTransport.UsingBasicAuth(new Uri(ArangoUrlStatic), "_system", ArangoUserStatic, ArangoPassStatic);
    using var rootClient = new ArangoDBClient(rootTransport);
    await rootClient.Database.PostDatabaseAsync(new ArangoDBNetStandard.DatabaseApi.Models.PostDatabaseBody { Name = dbName });
    return dbName;
}

internal static async Task DropDbStatic(string dbName)
{
    var rootTransport = HttpApiTransport.UsingBasicAuth(new Uri(ArangoUrlStatic), "_system", ArangoUserStatic, ArangoPassStatic);
    using var rootClient = new ArangoDBClient(rootTransport);
    try { await rootClient.Database.DeleteDatabaseAsync(dbName); } catch { /* best-effort */ }
}
```

Update the existing `EnsureSchemaAsync_CreatesAllCollectionsAndIndexes_Idempotent` test to call the renamed helpers.

- [ ] **Step 3: Run write test — expect failure (UpsertDecisionAsync does not exist).**

```bash
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter "FullyQualifiedName~MemoryStoreWriteTests"
```

Expected: FAIL — `UpsertDecisionAsync` not defined.

- [ ] **Step 4: Add write methods to `MemoryStore`.**

Append to `MemoryStore.cs`:

```csharp
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
```

- [ ] **Step 5: Run write tests.**

```bash
$env:ARANGO_TEST_RUN="1"
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter "FullyQualifiedName~MemoryStore"
```

Expected: 6 PASS (1 schema + 3 vector index + 2 write).

- [ ] **Step 6: Commit.**

```bash
git add dais-bridge/Memory/MemoryStore.cs dais-bridge.tests/Memory/MemoryStoreWriteTests.cs dais-bridge.tests/Memory/MemoryStoreSchemaTests.cs
git commit -m "feat(memory): MemoryStore content/edge/entity write paths with two-phase embedding"
```

---

### Task A6: Wire into Program.cs DI (no consumer yet)

**Files:**
- Modify: `dais-bridge/Program.cs`
- Modify: `dais-bridge/appsettings.json`

- [ ] **Step 1: Extend `appsettings.json` with embedding + memory config.**

Replace the `AI` block and append a `Memory` block:

```json
  "AI": {
    "LMStudioUrl": "http://localhost:1234/v1",
    "ModelId": "local-model",
    "EmbeddingModelId": "nomic-embed-text-v1.5",
    "EmbeddingDimension": 768,
    "LMStudioApiKey": ""
  },
  "Memory": {
    "VectorNLists": 100,
    "RecallAlpha": 0.7,
    "RecallBeta": 0.3,
    "DefaultTopK": 8,
    "DefaultExpandHops": 1,
    "CacheQueryEmbeddings": true,
    "QueryEmbeddingTtlSeconds": 300,
    "PendingEmbeddingRetryIntervalSeconds": 30,
    "PendingEmbeddingMaxAttempts": 5
  }
```

- [ ] **Step 2: Wire DI in `Program.cs` (between configuration reads and kernel registration).**

Insert after the existing config reads:

```csharp
        var embeddingModelId = builder.Configuration["AI:EmbeddingModelId"] ?? "nomic-embed-text-v1.5";
        var embeddingDimension = int.Parse(builder.Configuration["AI:EmbeddingDimension"] ?? "768");
        var vectorNLists = int.Parse(builder.Configuration["Memory:VectorNLists"] ?? "100");

        builder.Services.AddHttpClient("memory");
        var lmStudioApiKey = Environment.GetEnvironmentVariable("LMSTUDIO_API_KEY")
                            ?? builder.Configuration["AI:LMStudioApiKey"];
        builder.Services.AddSingleton<IEmbeddingClient>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("memory");
            return new LmStudioEmbeddingClient(http, lmStudioUrl, embeddingModelId, embeddingDimension, lmStudioApiKey);
        });
        builder.Services.AddSingleton<MemoryStore>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("memory");
            return new MemoryStore(arangoUrl, arangoDb, arangoUser, arangoPass, embeddingDimension, vectorNLists, http, sp.GetRequiredService<IEmbeddingClient>());
        });
```

Add the using directives at the top of `Program.cs`:

```csharp
using Darbee.Gateway.Memory;
```

- [ ] **Step 3: Run schema migration on app start.**

After `var app = builder.Build();` add:

```csharp
        using (var scope = app.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<MemoryStore>();
            await store.EnsureSchemaAsync();
        }
```

Convert `Main` signature to `public static async Task Main(string[] args)` if not already.

- [ ] **Step 4: Verify build + existing tests still pass.**

```bash
dotnet build dais-bridge/dais-bridge.csproj
dotnet test dais-bridge.tests/dais-bridge.tests.csproj
```

Expected: build SUCCESS; 14 tests pass (11 original + 3 new memory).

- [ ] **Step 5: Commit.**

```bash
git add dais-bridge/Program.cs dais-bridge/appsettings.json
git commit -m "feat(memory): register IEmbeddingClient + MemoryStore in DI; run EnsureSchemaAsync at startup"
```

---

## Phase B — Explicit layer

Goal: ship `TenantContextAccessor`, `MemoryPlugin`, swap kernels' `ArangoPlugin` → `MemoryPlugin`, and add cross-tenant integration test.

### Task B1: TenantContext + accessor

**Files:**
- Modify: `dais-bridge/Models/TenantContext.cs`
- Create: `dais-bridge/Models/ITenantContextAccessor.cs`
- Create: `dais-bridge/Models/TenantContextAccessor.cs`
- Create: `dais-bridge.tests/Memory/TenantContextAccessorTests.cs`

- [ ] **Step 1: Extend `TenantContext.cs`.**

Replace the existing class:

```csharp
namespace Darbee.Gateway.Models;

public sealed class TenantContext
{
    public string TenantId { get; init; } = string.Empty;
    public string TenantName { get; init; } = string.Empty;
    public string Configuration { get; init; } = string.Empty;

    public static TenantContext Admin { get; } = new() { TenantId = "admin", TenantName = "Admin" };
    public static TenantContext ForKid(string kidId, string? displayName = null) =>
        new() { TenantId = $"kid:{kidId}", TenantName = displayName ?? kidId };
}
```

- [ ] **Step 2: Create `ITenantContextAccessor.cs`.**

```csharp
namespace Darbee.Gateway.Models;

public interface ITenantContextAccessor
{
    TenantContext? Current { get; set; }
    TenantContext Required => Current
        ?? throw new InvalidOperationException("Tenant context not set on this call.");
}
```

- [ ] **Step 3: Create `TenantContextAccessor.cs` (AsyncLocal-based).**

```csharp
namespace Darbee.Gateway.Models;

public sealed class TenantContextAccessor : ITenantContextAccessor
{
    private static readonly AsyncLocal<TenantContext?> _current = new();
    public TenantContext? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
```

- [ ] **Step 4: Write tests.**

```csharp
using Darbee.Gateway.Models;

namespace Darbee.Gateway.Tests.Memory;

public class TenantContextAccessorTests
{
    [Fact]
    public void Current_DefaultsToNull()
    {
        var acc = new TenantContextAccessor();
        Assert.Null(acc.Current);
    }

    [Fact]
    public void Required_ThrowsWhenNotSet()
    {
        var acc = new TenantContextAccessor();
        Assert.Throws<InvalidOperationException>(() => acc.Required);
    }

    [Fact]
    public async Task AsyncLocal_FlowsAcrossAwait()
    {
        var acc = new TenantContextAccessor();
        acc.Current = TenantContext.ForKid("lila");
        await Task.Yield();
        Assert.Equal("kid:lila", acc.Required.TenantId);
    }

    [Fact]
    public async Task AsyncLocal_IsolatesParallelTasks()
    {
        var acc = new TenantContextAccessor();
        var t1 = Task.Run(async () =>
        {
            acc.Current = TenantContext.ForKid("a");
            await Task.Delay(20);
            return acc.Required.TenantId;
        });
        var t2 = Task.Run(async () =>
        {
            acc.Current = TenantContext.ForKid("b");
            await Task.Delay(20);
            return acc.Required.TenantId;
        });
        var ids = await Task.WhenAll(t1, t2);
        Assert.Contains("kid:a", ids);
        Assert.Contains("kid:b", ids);
    }
}
```

- [ ] **Step 5: Run tests.**

```bash
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter "FullyQualifiedName~TenantContextAccessorTests"
```

Expected: 4 PASS.

- [ ] **Step 6: Commit.**

```bash
git add dais-bridge/Models/TenantContext.cs dais-bridge/Models/ITenantContextAccessor.cs dais-bridge/Models/TenantContextAccessor.cs dais-bridge.tests/Memory/TenantContextAccessorTests.cs
git commit -m "feat(tenant): introduce ITenantContextAccessor with AsyncLocal-backed implementation"
```

---

### Task B2: MemoryPlugin (kernel functions)

**Files:**
- Create: `dais-bridge/Plugins/MemoryPlugin.cs`
- Create: `dais-bridge.tests/Memory/MemoryPluginTests.cs`

- [ ] **Step 1: Write failing tests.**

```csharp
using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;
using Darbee.Gateway.Models;
using Darbee.Gateway.Plugins;

namespace Darbee.Gateway.Tests.Memory;

[Trait("Category", "Integration")]
public class MemoryPluginTests
{
    [Fact]
    public async Task RememberDecision_WhenTenantSet_WritesUnderTenant()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDbStatic();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStoreWriteTests.ConstantEmbeddingClient(4);
            var store = new MemoryStore(MemoryStoreSchemaTests.ArangoUrlStatic, dbName,
                MemoryStoreSchemaTests.ArangoUserStatic, MemoryStoreSchemaTests.ArangoPassStatic,
                4, 1, http, emb);
            await store.EnsureSchemaAsync();

            var acc = new TenantContextAccessor { Current = TenantContext.Admin };
            var plugin = new MemoryPlugin(store, acc);

            var json = await plugin.RememberDecision("subject", "x", "because", new[] { "y" });
            Assert.Contains("\"completed\":true", json);
        }
        finally { await MemoryStoreSchemaTests.DropDbStatic(dbName); }
    }

    [Fact]
    public async Task RememberDecision_WhenTenantUnset_Throws()
    {
        using var http = new HttpClient();
        var store = new MemoryStore("http://localhost:8529", "ignored", "root", "password", 4, 1, http);
        var acc = new TenantContextAccessor();
        var plugin = new MemoryPlugin(store, acc);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => plugin.RememberDecision("s", "c", "b", Array.Empty<string>()));
    }
}
```

This requires `MemoryStoreWriteTests.ConstantEmbeddingClient` to be `internal` not `private`. Update its access modifier in Task A5's test file:

```csharp
internal sealed class ConstantEmbeddingClient(int dim, float[]? value = null) : IEmbeddingClient
```

- [ ] **Step 2: Run — expect failure (MemoryPlugin missing).**

```bash
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter "FullyQualifiedName~MemoryPluginTests"
```

Expected: FAIL — `MemoryPlugin` not found.

- [ ] **Step 3: Implement `MemoryPlugin`.**

```csharp
using System.ComponentModel;
using System.Text.Json;
using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;
using Darbee.Gateway.Models;
using Microsoft.SemanticKernel;

namespace Darbee.Gateway.Plugins;

public sealed class MemoryPlugin
{
    private readonly MemoryStore _store;
    private readonly ITenantContextAccessor _tenant;

    public MemoryPlugin(MemoryStore store, ITenantContextAccessor tenant)
    {
        _store = store;
        _tenant = tenant;
    }

    [KernelFunction, Description("Records an architectural or design decision into long-term memory.")]
    public async Task<string> RememberDecision(
        [Description("What the decision is about")] string subject,
        [Description("What was chosen")] string chose,
        [Description("Why it was chosen")] string because,
        [Description("Alternatives considered (may be empty)")] IReadOnlyList<string> alternatives)
    {
        var t = _tenant.Required;
        var result = await _store.UpsertDecisionAsync(t.TenantId, subject, chose, because, alternatives);
        return JsonSerializer.Serialize(new { id = result.Id, completed = result.Completed, queued = result.Queued });
    }

    [KernelFunction, Description("Records a structured observation (commit, geo-run, research, asset action) into long-term memory.")]
    public async Task<string> RememberObservation(
        [Description("Source category: commit | geo-run | research | asset")] string source,
        [Description("Human-readable summary text")] string text,
        [Description("Optional JSON-serializable payload as a string")] string payloadJson)
    {
        var t = _tenant.Required;
        object payload = string.IsNullOrWhiteSpace(payloadJson) ? new { } : JsonSerializer.Deserialize<object>(payloadJson)!;
        var result = await _store.UpsertObservationAsync(t.TenantId, source, text, payload);
        return JsonSerializer.Serialize(new { id = result.Id, completed = result.Completed, queued = result.Queued });
    }

    [KernelFunction, Description("Connects two memory items with a typed edge.")]
    public async Task<string> LinkMemory(
        [Description("Full _id of source, e.g. memory_decisions/abc")] string fromId,
        [Description("Full _id of target")] string toId,
        [Description("Edge kind: mentions | depends-on | supersedes | tagged | about-file")] string edgeKind,
        [Description("Edge weight (default 1.0)")] double weight = 1.0)
    {
        var t = _tenant.Required;
        return await _store.UpsertEdgeAsync(t.TenantId, fromId, toId, edgeKind, weight);
    }
}
```

- [ ] **Step 4: Run tests.**

```bash
$env:ARANGO_TEST_RUN="1"
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter "FullyQualifiedName~MemoryPluginTests"
```

Expected: 2 PASS.

- [ ] **Step 5: Commit.**

```bash
git add dais-bridge/Plugins/MemoryPlugin.cs dais-bridge.tests/Memory/MemoryPluginTests.cs dais-bridge.tests/Memory/MemoryStoreWriteTests.cs
git commit -m "feat(memory): MemoryPlugin with RememberDecision/Observation/LinkMemory kernel functions"
```

---

### Task B3: Replace ArangoPlugin in Program.cs and delete it

**Files:**
- Modify: `dais-bridge/Program.cs`
- Delete: `dais-bridge/Plugins/ArangoPlugin.cs`
- Delete: `dais-bridge.tests/ArangoPluginTests.cs`

- [ ] **Step 1: Register `ITenantContextAccessor` in DI.**

In `Program.cs`, before the kernel registrations:

```csharp
        builder.Services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
```

Add `using Darbee.Gateway.Models;` if not already present.

- [ ] **Step 2: Replace ArangoPlugin with MemoryPlugin in both kernels.**

In the `kernel-kidsafe` registration, remove:

```csharp
kernelBuilder.Plugins.AddFromObject(new ArangoPlugin(arangoUrl, arangoDb, arangoUser, arangoPass), "ArangoDB");
```

Add:

```csharp
            var memStore = sp.GetRequiredService<MemoryStore>();
            var memTenant = sp.GetRequiredService<ITenantContextAccessor>();
            kernelBuilder.Plugins.AddFromObject(new MemoryPlugin(memStore, memTenant), "Memory");
```

Repeat for `kernel-admin`.

- [ ] **Step 3: Delete the stub files.**

```bash
git rm dais-bridge/Plugins/ArangoPlugin.cs dais-bridge.tests/ArangoPluginTests.cs
```

- [ ] **Step 4: Build + run all tests.**

```bash
dotnet build dais-bridge/dais-bridge.csproj
dotnet test dais-bridge.tests/dais-bridge.tests.csproj
```

Expected: build SUCCESS. All non-Arango tests pass; Arango-tagged tests pass with `ARANGO_TEST_RUN=1`.

- [ ] **Step 5: Commit.**

```bash
git add dais-bridge/Program.cs
git commit -m "refactor(memory): replace ArangoPlugin with MemoryPlugin in both kernels and delete stub"
```

---

### Task B4: Hub OnConnectedAsync sets TenantContext

**Files:**
- Modify: `dais-bridge/Hubs/KidSafeHub.cs`
- Modify: `dais-bridge/Hubs/ParentHub.cs`

- [ ] **Step 1: Update `KidSafeHub.cs`.**

```csharp
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Darbee.Gateway.Models;

namespace Darbee.Gateway.Hubs;

public class KidSafeHub : Hub
{
    private readonly ILogger<KidSafeHub> _logger;
    private readonly ITenantContextAccessor _tenantAccessor;

    public KidSafeHub(ILogger<KidSafeHub> logger, ITenantContextAccessor tenantAccessor)
    {
        _logger = logger;
        _tenantAccessor = tenantAccessor;
    }

    private void SetTenant()
    {
        var kidId = Context.UserIdentifier ?? Context.ConnectionId;
        _tenantAccessor.Current = TenantContext.ForKid(kidId);
    }

    public override Task OnConnectedAsync()
    {
        SetTenant();
        _logger.LogInformation("KidSafeHub connected: tenant={Tenant}", _tenantAccessor.Required.TenantId);
        return base.OnConnectedAsync();
    }

    public async Task SendMessage(string user, string message)
    {
        SetTenant();
        _logger.LogInformation("KidSafeHub: Message received from {User}: {Message}", user, message);
        await Clients.Caller.SendAsync("ReceiveMessage", "Gateway", $"Hi {user}! I received your message: \"{message}\". Checking if it's safe...");
    }
}
```

`SetTenant()` is called both `OnConnectedAsync` and on each method invocation because SignalR scopes are per-method-call; AsyncLocal can be lost between hub method invocations on the same connection.

- [ ] **Step 2: Update `ParentHub.cs`.**

```csharp
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Darbee.Gateway.Models;

namespace Darbee.Gateway.Hubs;

public class ParentHub : Hub
{
    private readonly ILogger<ParentHub> _logger;
    private readonly ITenantContextAccessor _tenantAccessor;

    public ParentHub(ILogger<ParentHub> logger, ITenantContextAccessor tenantAccessor)
    {
        _logger = logger;
        _tenantAccessor = tenantAccessor;
    }

    private void SetTenant() => _tenantAccessor.Current = TenantContext.Admin;

    public override Task OnConnectedAsync()
    {
        SetTenant();
        _logger.LogInformation("ParentHub connected: tenant={Tenant}", _tenantAccessor.Required.TenantId);
        return base.OnConnectedAsync();
    }

    public async Task SendAlert(string alertType, string details)
    {
        SetTenant();
        _logger.LogWarning("ParentHub: Alert triggered - {Type}: {Details}", alertType, details);
        await Clients.All.SendAsync("ReceiveAlert", alertType, details);
    }

    public async Task RequestApproval(string requestId, string description)
    {
        SetTenant();
        _logger.LogInformation("ParentHub: Approval requested for {RequestId}: {Description}", requestId, description);
        await Clients.All.SendAsync("ApprovalRequired", requestId, description);
    }
}
```

- [ ] **Step 3: Build + test.**

```bash
dotnet build dais-bridge/dais-bridge.csproj
dotnet test dais-bridge.tests/dais-bridge.tests.csproj
```

Expected: build SUCCESS, all tests pass.

- [ ] **Step 4: Commit.**

```bash
git add dais-bridge/Hubs/KidSafeHub.cs dais-bridge/Hubs/ParentHub.cs
git commit -m "feat(tenant): hubs set TenantContext on connect and each method invocation"
```

---

### Task B5: Cross-tenant isolation integration test

**Files:**
- Create: `dais-bridge.tests/Memory/CrossTenantIsolationTests.cs`

- [ ] **Step 1: Write test.**

```csharp
using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;
using Darbee.Gateway.Models;

namespace Darbee.Gateway.Tests.Memory;

[Trait("Category", "Integration")]
public class CrossTenantIsolationTests
{
    [Fact]
    public async Task ListContent_ScopedByTenantId_NeverLeaksAcrossTenants()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDbStatic();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStoreWriteTests.ConstantEmbeddingClient(4);
            var store = new MemoryStore(MemoryStoreSchemaTests.ArangoUrlStatic, dbName,
                MemoryStoreSchemaTests.ArangoUserStatic, MemoryStoreSchemaTests.ArangoPassStatic,
                4, 1, http, emb);
            await store.EnsureSchemaAsync();

            await store.UpsertDecisionAsync("admin", "policy", "allow", "trusted", Array.Empty<string>());
            await store.UpsertDecisionAsync("kid:lila", "snack", "carrots", "healthy", Array.Empty<string>());
            await store.UpsertDecisionAsync("kid:lila", "policy", "allow", "trusted", Array.Empty<string>()); // identical text to admin's

            var kidItems = await store.ListByTenantAsync("kid:lila", MemoryKind.Decision);
            var adminItems = await store.ListByTenantAsync("admin", MemoryKind.Decision);

            Assert.Equal(2, kidItems.Count);
            Assert.Single(adminItems);
            Assert.All(kidItems, i => Assert.Equal("kid:lila", i.TenantId));
            Assert.All(adminItems, i => Assert.Equal("admin", i.TenantId));
        }
        finally { await MemoryStoreSchemaTests.DropDbStatic(dbName); }
    }
}
```

- [ ] **Step 2: Add `ListByTenantAsync` to `MemoryStore.cs`.**

```csharp
    public async Task<List<MemoryItem>> ListByTenantAsync(string tenantId, MemoryKind kind, int limit = 100, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        var col = MemoryCollections.ForKind(kind);
        var aql = "FOR d IN @@col FILTER d.tenant_id == @tenantId SORT d.created_at DESC LIMIT @limit RETURN d";
        var bindVars = new Dictionary<string, object>
        {
            ["@col"] = col,
            ["tenantId"] = tenantId,
            ["limit"] = limit
        };
        var cursor = await _arango.Cursor.PostCursorAsync<Dictionary<string, JsonElement>>(
            new ArangoDBNetStandard.CursorApi.Models.PostCursorBody { Query = aql, BindVars = bindVars });
        return cursor.Result.Select(r => MapToMemoryItem(r, kind)).ToList();
    }

    private static MemoryItem MapToMemoryItem(Dictionary<string, JsonElement> r, MemoryKind kind) => new(
        Key: r["_key"].GetString() ?? "",
        Kind: kind,
        Text: r.TryGetValue("text", out var t) ? t.GetString() ?? "" : "",
        Embedding: r.TryGetValue("embedding", out var e) && e.ValueKind == JsonValueKind.Array
            ? e.EnumerateArray().Select(x => x.GetSingle()).ToArray() : null,
        TenantId: r["tenant_id"].GetString() ?? "",
        Status: r.TryGetValue("status", out var s) ? s.GetString() ?? "ready" : "ready",
        CreatedAt: r.TryGetValue("created_at", out var c) ? DateTime.Parse(c.GetString() ?? "") : default,
        UpdatedAt: r.TryGetValue("updated_at", out var u) ? DateTime.Parse(u.GetString() ?? "") : default,
        Metadata: null);
```

- [ ] **Step 3: Run cross-tenant test.**

```bash
$env:ARANGO_TEST_RUN="1"
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter "FullyQualifiedName~CrossTenantIsolationTests"
```

Expected: 1 PASS.

- [ ] **Step 4: Commit.**

```bash
git add dais-bridge/Memory/MemoryStore.cs dais-bridge.tests/Memory/CrossTenantIsolationTests.cs
git commit -m "test(memory): cross-tenant isolation invariant on ListByTenantAsync"
```

---

## Phase C — Recall

Goal: ship `MemoryRecallEngine` with hybrid algorithm and add `Recall` kernel function.

### Task C1: ExtractEntities (substring + fallback flag)

**Files:**
- Create: `dais-bridge/Memory/MemoryRecallEngine.cs`
- Create: `dais-bridge.tests/Memory/MemoryRecallEngineTests.cs`

NER fallback is deferred — Phase C ships substring matching only. The fallback is a documented hook (`Func<string, Task<IReadOnlyList<string>>>?`) that defaults to a no-op; Phase D wires it up.

- [ ] **Step 1: Write failing tests for `ExtractEntitiesAsync`.**

```csharp
using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;

namespace Darbee.Gateway.Tests.Memory;

[Trait("Category", "Integration")]
public class MemoryRecallEngineTests
{
    [Fact]
    public async Task ExtractEntities_MatchesCanonicalNameSubstring()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDbStatic();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStoreWriteTests.ConstantEmbeddingClient(4);
            var store = new MemoryStore(MemoryStoreSchemaTests.ArangoUrlStatic, dbName,
                MemoryStoreSchemaTests.ArangoUserStatic, MemoryStoreSchemaTests.ArangoPassStatic,
                4, 1, http, emb);
            await store.EnsureSchemaAsync();

            var chickenId = await store.UpsertEntityAsync("admin", "chickens", new[] { "chooks" }, "concept");
            await store.UpsertEntityAsync("admin", "PostCard", Array.Empty<string>(), "file");

            var engine = new MemoryRecallEngine(store, emb, alpha: 0.7, beta: 0.3);

            var result = await engine.ExtractEntitiesAsync("admin", "who fed the chickens yesterday");

            Assert.Single(result);
            Assert.Equal($"memory_entities/{chickenId}", result[0]);
        }
        finally { await MemoryStoreSchemaTests.DropDbStatic(dbName); }
    }

    [Fact]
    public async Task ExtractEntities_AlsoMatchesAliases()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDbStatic();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStoreWriteTests.ConstantEmbeddingClient(4);
            var store = new MemoryStore(MemoryStoreSchemaTests.ArangoUrlStatic, dbName,
                MemoryStoreSchemaTests.ArangoUserStatic, MemoryStoreSchemaTests.ArangoPassStatic,
                4, 1, http, emb);
            await store.EnsureSchemaAsync();
            var id = await store.UpsertEntityAsync("admin", "chickens", new[] { "chooks", "hens" }, "concept");

            var engine = new MemoryRecallEngine(store, emb, alpha: 0.7, beta: 0.3);
            var result = await engine.ExtractEntitiesAsync("admin", "checked the chooks this morning");

            Assert.Contains($"memory_entities/{id}", result);
        }
        finally { await MemoryStoreSchemaTests.DropDbStatic(dbName); }
    }
}
```

- [ ] **Step 2: Implement `MemoryRecallEngine.ExtractEntitiesAsync`.**

```csharp
using System.Text.Json;
using ArangoDBNetStandard.CursorApi.Models;
using Darbee.Gateway.Memory.Models;

namespace Darbee.Gateway.Memory;

public sealed class MemoryRecallEngine
{
    private readonly MemoryStore _store;
    private readonly IEmbeddingClient _embeddings;
    private readonly double _alpha;
    private readonly double _beta;
    private readonly Func<string, Task<IReadOnlyList<string>>>? _nerFallback;

    public MemoryRecallEngine(MemoryStore store, IEmbeddingClient embeddings, double alpha, double beta,
        Func<string, Task<IReadOnlyList<string>>>? nerFallback = null)
    {
        _store = store;
        _embeddings = embeddings;
        _alpha = alpha;
        _beta = beta;
        _nerFallback = nerFallback;
    }

    public async Task<IReadOnlyList<string>> ExtractEntitiesAsync(string tenantId, string query, CancellationToken ct = default)
    {
        var aql = @"FOR e IN @@col
                      FILTER e.tenant_id == @tenantId
                      LET hit = CONTAINS(LOWER(@query), LOWER(e.canonical_name))
                                OR LENGTH(FOR a IN e.aliases FILTER CONTAINS(LOWER(@query), LOWER(a)) RETURN 1) > 0
                      FILTER hit
                      RETURN e._id";
        var bindVars = new Dictionary<string, object>
        {
            ["@col"] = MemoryCollections.Entities,
            ["tenantId"] = tenantId,
            ["query"] = query
        };
        var ids = await _store.QueryAsync<string>(aql, bindVars, ct);
        if (ids.Count > 0 || _nerFallback is null) return ids;
        return await _nerFallback(query);
    }
}
```

- [ ] **Step 3: Add `QueryAsync<T>` helper to `MemoryStore.cs`.**

```csharp
    public async Task<List<T>> QueryAsync<T>(string aql, Dictionary<string, object> bindVars, CancellationToken ct = default)
    {
        var cursor = await _arango.Cursor.PostCursorAsync<T>(
            new ArangoDBNetStandard.CursorApi.Models.PostCursorBody { Query = aql, BindVars = bindVars });
        return cursor.Result.ToList();
    }
```

- [ ] **Step 4: Run tests.**

```bash
$env:ARANGO_TEST_RUN="1"
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter "FullyQualifiedName~MemoryRecallEngineTests"
```

Expected: 2 PASS.

- [ ] **Step 5: Commit.**

```bash
git add dais-bridge/Memory/MemoryRecallEngine.cs dais-bridge/Memory/MemoryStore.cs dais-bridge.tests/Memory/MemoryRecallEngineTests.cs
git commit -m "feat(memory): MemoryRecallEngine.ExtractEntitiesAsync with substring matching"
```

---

### Task C2: GraphExpand

**Files:**
- Modify: `dais-bridge/Memory/MemoryRecallEngine.cs`
- Modify: `dais-bridge.tests/Memory/MemoryRecallEngineTests.cs`

- [ ] **Step 1: Write failing test.**

Append to `MemoryRecallEngineTests.cs`:

```csharp
    [Fact]
    public async Task GraphExpand_FindsItemsConnectedToEntityWithinHopBudget()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDbStatic();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStoreWriteTests.ConstantEmbeddingClient(4);
            var store = new MemoryStore(MemoryStoreSchemaTests.ArangoUrlStatic, dbName,
                MemoryStoreSchemaTests.ArangoUserStatic, MemoryStoreSchemaTests.ArangoPassStatic,
                4, 1, http, emb);
            await store.EnsureSchemaAsync();

            var entityId = await store.UpsertEntityAsync("admin", "PostCard", Array.Empty<string>(), "file");
            var dec = await store.UpsertDecisionAsync("admin", "PostCard kind union", "discriminated union", "TS6 narrowing", Array.Empty<string>());
            await store.UpsertEdgeAsync("admin", $"memory_decisions/{dec.Id}", $"memory_entities/{entityId}", "mentions", 1.0);

            var engine = new MemoryRecallEngine(store, emb, alpha: 0.7, beta: 0.3);
            var expanded = await engine.GraphExpandAsync("admin", new[] { $"memory_entities/{entityId}" }, expandHops: 1);

            Assert.Single(expanded);
            Assert.Equal(MemoryKind.Decision, expanded[0].Item.Kind);
            Assert.Equal(1, expanded[0].HopsFromQuery);
        }
        finally { await MemoryStoreSchemaTests.DropDbStatic(dbName); }
    }
```

- [ ] **Step 2: Run — expect failure.**

```bash
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter "FullyQualifiedName~MemoryRecallEngineTests"
```

Expected: 2 PASS, 1 FAIL on `GraphExpandAsync` not found.

- [ ] **Step 3: Implement `GraphExpandAsync`.**

Append to `MemoryRecallEngine.cs`:

```csharp
    public sealed record GraphCandidate(MemoryItem Item, int HopsFromQuery, IReadOnlyList<string> PathEntityKeys);

    public async Task<IReadOnlyList<GraphCandidate>> GraphExpandAsync(
        string tenantId, IReadOnlyList<string> entityIds, int expandHops, CancellationToken ct = default)
    {
        if (entityIds.Count == 0) return Array.Empty<GraphCandidate>();
        var aql = @"FOR e IN @entityIds
                     FOR v, edge, p IN 1..@hops ANY e @@edges
                       FILTER v.tenant_id == @tenantId
                       FILTER PARSE_IDENTIFIER(v._id).collection != @entitiesCol
                       RETURN DISTINCT { item: v, hops: LENGTH(p.edges), path: p.vertices[*]._id }";
        var bindVars = new Dictionary<string, object>
        {
            ["entityIds"] = entityIds,
            ["hops"] = expandHops,
            ["tenantId"] = tenantId,
            ["@edges"] = MemoryCollections.Edges,
            ["entitiesCol"] = MemoryCollections.Entities
        };
        var rows = await _store.QueryAsync<GraphRow>(aql, bindVars, ct);
        return rows.Select(r => new GraphCandidate(
            Item: MaterializeItem(r.item),
            HopsFromQuery: r.hops,
            PathEntityKeys: r.path
        )).ToList();
    }

    private sealed record GraphRow(Dictionary<string, JsonElement> item, int hops, List<string> path);

    private static MemoryItem MaterializeItem(Dictionary<string, JsonElement> r)
    {
        var idStr = r["_id"].GetString() ?? "";
        var col = idStr.Split('/')[0];
        var kind = col switch
        {
            "memory_decisions" => MemoryKind.Decision,
            "memory_observations" => MemoryKind.Observation,
            "memory_facts" => MemoryKind.Fact,
            "memory_summaries" => MemoryKind.Summary,
            "memory_entities" => MemoryKind.Entity,
            _ => throw new InvalidOperationException($"Unknown collection {col}")
        };
        return new MemoryItem(
            Key: r["_key"].GetString() ?? "",
            Kind: kind,
            Text: r.TryGetValue("text", out var t) ? t.GetString() ?? "" : "",
            Embedding: r.TryGetValue("embedding", out var e) && e.ValueKind == JsonValueKind.Array
                ? e.EnumerateArray().Select(x => x.GetSingle()).ToArray() : null,
            TenantId: r["tenant_id"].GetString() ?? "",
            Status: r.TryGetValue("status", out var s) ? s.GetString() ?? "ready" : "ready",
            CreatedAt: r.TryGetValue("created_at", out var c) ? DateTime.Parse(c.GetString() ?? "") : default,
            UpdatedAt: r.TryGetValue("updated_at", out var u) ? DateTime.Parse(u.GetString() ?? "") : default);
    }
```

- [ ] **Step 4: Run all engine tests.**

```bash
$env:ARANGO_TEST_RUN="1"
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter "FullyQualifiedName~MemoryRecallEngineTests"
```

Expected: 3 PASS.

- [ ] **Step 5: Commit.**

```bash
git add dais-bridge/Memory/MemoryRecallEngine.cs dais-bridge.tests/Memory/MemoryRecallEngineTests.cs
git commit -m "feat(memory): MemoryRecallEngine.GraphExpandAsync with hop-distance tracking"
```

---

### Task C3: VectorTopK + Recall composition

**Files:**
- Modify: `dais-bridge/Memory/MemoryRecallEngine.cs`
- Modify: `dais-bridge.tests/Memory/MemoryRecallEngineTests.cs`

- [ ] **Step 1: Write failing test for `RecallAsync`.**

Append:

```csharp
    [Fact]
    public async Task RecallAsync_CombinesGraphAndVectorTopK_AndScores()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDbStatic();
        try
        {
            using var http = new HttpClient();
            // Different embeddings per text so vectorTopK has a meaningful order
            var queryEmb = new float[] { 1f, 0f, 0f, 0f };
            var emb = new TaskC3StubEmbeddings(queryEmb,
                ("Decision: chickens",  new float[] { 0.9f, 0.1f, 0f, 0f }),
                ("Decision: weather",   new float[] { 0f, 1f, 0f, 0f }));
            var store = new MemoryStore(MemoryStoreSchemaTests.ArangoUrlStatic, dbName,
                MemoryStoreSchemaTests.ArangoUserStatic, MemoryStoreSchemaTests.ArangoPassStatic,
                4, 1, http, emb);
            await store.EnsureSchemaAsync();

            var ent = await store.UpsertEntityAsync("admin", "chickens", Array.Empty<string>(), "concept");
            var dec1 = await store.UpsertDecisionAsync("admin", "chickens", "free range", "happier", Array.Empty<string>());
            var dec2 = await store.UpsertDecisionAsync("admin", "weather", "summer", "warm", Array.Empty<string>());
            await store.UpsertEdgeAsync("admin", $"memory_decisions/{dec1.Id}", $"memory_entities/{ent}", "mentions", 1.0);

            var engine = new MemoryRecallEngine(store, emb, alpha: 0.7, beta: 0.3);
            var result = await engine.RecallAsync("admin", "what about the chickens", topK: 2);

            // chickens decision should rank first: high cosine + connected via graph
            Assert.True(result.Items.Count >= 1);
            Assert.Equal("chickens", result.Items[0].Item.Text.Contains("chickens") ? "chickens" : "fail");
        }
        finally { await MemoryStoreSchemaTests.DropDbStatic(dbName); }
    }

    private sealed class TaskC3StubEmbeddings : IEmbeddingClient
    {
        private readonly float[] _queryEmb;
        private readonly Dictionary<string, float[]> _byText = new();
        public int Dimension => 4;
        public TaskC3StubEmbeddings(float[] queryEmb, params (string text, float[] vec)[] map)
        {
            _queryEmb = queryEmb;
            foreach (var (t, v) in map) _byText[t] = v;
        }
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            foreach (var kv in _byText)
                if (text.Contains(kv.Key)) return Task.FromResult(kv.Value);
            return Task.FromResult(_queryEmb);
        }
        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(t => EmbedAsync(t).Result).ToArray());
    }
```

- [ ] **Step 2: Run — expect failure.**

- [ ] **Step 3: Implement `RecallAsync` and `VectorTopKAsync`.**

Append to `MemoryRecallEngine.cs`:

```csharp
    public async Task<RecallResult> RecallAsync(
        string tenantId, string query, int topK = 8, int expandHops = 1, CancellationToken ct = default)
    {
        var entityIds = await ExtractEntitiesAsync(tenantId, query, ct);

        var graphCandidates = await GraphExpandAsync(tenantId, entityIds, expandHops, ct);

        float[] queryEmb;
        try { queryEmb = await _embeddings.EmbedAsync(query, ct); }
        catch
        {
            // Embedding service down: graph-only mode
            var graphOnly = graphCandidates
                .Select(c => new ScoredMemoryItem(
                    Item: c.Item, Cosine: 0, Proximity: 1.0 / (1 + c.HopsFromQuery),
                    Score: _beta * (1.0 / (1 + c.HopsFromQuery)),
                    HopsFromQuery: c.HopsFromQuery, PathEntityKeys: c.PathEntityKeys))
                .OrderByDescending(s => s.Score)
                .Take(topK)
                .ToList();
            return new RecallResult(graphOnly, entityIds);
        }

        var vectorCandidates = await VectorTopKAsync(tenantId, queryEmb, 2 * topK, ct);

        // Merge by composite key
        var merged = new Dictionary<string, ScoredMemoryItem>();
        foreach (var c in graphCandidates)
        {
            var key = $"{c.Item.Kind}:{c.Item.Key}";
            var cosine = CosineSimilarity(queryEmb, c.Item.Embedding);
            var proximity = 1.0 / (1 + c.HopsFromQuery);
            merged[key] = new ScoredMemoryItem(
                Item: c.Item, Cosine: cosine, Proximity: proximity,
                Score: _alpha * cosine + _beta * proximity,
                HopsFromQuery: c.HopsFromQuery,
                PathEntityKeys: c.PathEntityKeys);
        }
        foreach (var v in vectorCandidates)
        {
            var key = $"{v.Item.Kind}:{v.Item.Key}";
            if (merged.ContainsKey(key)) continue;
            merged[key] = new ScoredMemoryItem(
                Item: v.Item, Cosine: v.Similarity, Proximity: 0.0,
                Score: _alpha * v.Similarity,
                HopsFromQuery: null, PathEntityKeys: Array.Empty<string>());
        }

        var ordered = merged.Values.OrderByDescending(s => s.Score).Take(topK).ToList();
        return new RecallResult(ordered, entityIds);
    }

    public sealed record VectorCandidate(MemoryItem Item, double Similarity);

    public async Task<IReadOnlyList<VectorCandidate>> VectorTopKAsync(
        string tenantId, float[] queryEmbedding, int limit, CancellationToken ct = default)
    {
        var contentCols = new[]
        {
            MemoryCollections.Decisions,
            MemoryCollections.Observations,
            MemoryCollections.Facts,
            MemoryCollections.Summaries
        };
        var unionParts = string.Join(",\n",
            contentCols.Select(c => $"(FOR x IN {c} FILTER x.tenant_id == @tenantId AND x.status == 'ready' RETURN x)"));
        var aql = $@"FOR i IN UNION({unionParts})
                      LET sim = APPROX_NEAR_COSINE(i.embedding, @queryEmb)
                      SORT sim DESC
                      LIMIT @limit
                      RETURN {{ item: i, similarity: sim }}";
        var bindVars = new Dictionary<string, object>
        {
            ["tenantId"] = tenantId,
            ["queryEmb"] = queryEmbedding,
            ["limit"] = limit
        };
        var rows = await _store.QueryAsync<VectorRow>(aql, bindVars, ct);
        return rows.Select(r => new VectorCandidate(MaterializeItem(r.item), r.similarity)).ToList();
    }

    private sealed record VectorRow(Dictionary<string, JsonElement> item, double similarity);

    private static double CosineSimilarity(float[] a, float[]? b)
    {
        if (b is null || b.Length != a.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        if (na == 0 || nb == 0) return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
```

- [ ] **Step 4: Run all engine tests.**

```bash
$env:ARANGO_TEST_RUN="1"
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter "FullyQualifiedName~MemoryRecallEngineTests"
```

Expected: 4 PASS.

- [ ] **Step 5: Commit.**

```bash
git add dais-bridge/Memory/MemoryRecallEngine.cs dais-bridge.tests/Memory/MemoryRecallEngineTests.cs
git commit -m "feat(memory): hybrid Recall = graph expand + vector top-K with weighted scoring"
```

---

### Task C4: MemoryPlugin.Recall + DI wiring

**Files:**
- Modify: `dais-bridge/Plugins/MemoryPlugin.cs`
- Modify: `dais-bridge/Program.cs`
- Modify: `dais-bridge.tests/Memory/MemoryPluginTests.cs`

- [ ] **Step 1: Add `Recall` kernel function.**

Append to `MemoryPlugin.cs`:

```csharp
    private readonly MemoryRecallEngine _recall;

    public MemoryPlugin(MemoryStore store, ITenantContextAccessor tenant, MemoryRecallEngine recall)
    {
        _store = store;
        _tenant = tenant;
        _recall = recall;
    }

    [KernelFunction, Description("Recalls memories most relevant to a query, combining graph expansion and vector similarity.")]
    public async Task<string> Recall(
        [Description("The natural-language query")] string query,
        [Description("Maximum results (default 8)")] int topK = 8,
        [Description("Graph expansion hops (default 1)")] int expandHops = 1)
    {
        var t = _tenant.Required;
        var result = await _recall.RecallAsync(t.TenantId, query, topK, expandHops);
        return JsonSerializer.Serialize(new
        {
            extractedEntityIds = result.ExtractedEntityIds,
            items = result.Items.Select(i => new
            {
                kind = i.Item.Kind.ToString(),
                key = i.Item.Key,
                text = i.Item.Text,
                cosine = i.Cosine,
                proximity = i.Proximity,
                score = i.Score,
                hops = i.HopsFromQuery,
                path = i.PathEntityKeys
            })
        });
    }
```

Replace the existing one-arg constructor with the three-arg version (delete the old one).

- [ ] **Step 2: Update DI in `Program.cs`.**

```csharp
        builder.Services.AddSingleton<MemoryRecallEngine>(sp =>
        {
            var store = sp.GetRequiredService<MemoryStore>();
            var emb = sp.GetRequiredService<IEmbeddingClient>();
            var alpha = double.Parse(builder.Configuration["Memory:RecallAlpha"] ?? "0.7");
            var beta = double.Parse(builder.Configuration["Memory:RecallBeta"] ?? "0.3");
            return new MemoryRecallEngine(store, emb, alpha, beta);
        });
```

Update both kernel registrations to inject `MemoryRecallEngine`:

```csharp
            var memStore = sp.GetRequiredService<MemoryStore>();
            var memTenant = sp.GetRequiredService<ITenantContextAccessor>();
            var memRecall = sp.GetRequiredService<MemoryRecallEngine>();
            kernelBuilder.Plugins.AddFromObject(new MemoryPlugin(memStore, memTenant, memRecall), "Memory");
```

- [ ] **Step 3: Update `MemoryPluginTests.cs` to pass the recall engine.**

In both existing tests, replace:

```csharp
var plugin = new MemoryPlugin(store, acc);
```

with:

```csharp
var recall = new MemoryRecallEngine(store, emb, 0.7, 0.3);
var plugin = new MemoryPlugin(store, acc, recall);
```

Add a third test:

```csharp
    [Fact]
    public async Task Recall_ReturnsItemsScopedToTenantOnly()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDbStatic();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStoreWriteTests.ConstantEmbeddingClient(4);
            var store = new MemoryStore(MemoryStoreSchemaTests.ArangoUrlStatic, dbName,
                MemoryStoreSchemaTests.ArangoUserStatic, MemoryStoreSchemaTests.ArangoPassStatic,
                4, 1, http, emb);
            await store.EnsureSchemaAsync();
            await store.UpsertDecisionAsync("admin", "policy", "x", "y", Array.Empty<string>());
            await store.UpsertDecisionAsync("kid:a", "snack", "x", "y", Array.Empty<string>());

            var recall = new MemoryRecallEngine(store, emb, 0.7, 0.3);
            var acc = new TenantContextAccessor { Current = TenantContext.ForKid("a") };
            var plugin = new MemoryPlugin(store, acc, recall);

            var json = await plugin.Recall("anything", topK: 8, expandHops: 1);

            Assert.Contains("snack", json);
            Assert.DoesNotContain("policy", json);
        }
        finally { await MemoryStoreSchemaTests.DropDbStatic(dbName); }
    }
```

- [ ] **Step 4: Build + run all tests.**

```bash
$env:ARANGO_TEST_RUN="1"
dotnet build dais-bridge/dais-bridge.csproj
dotnet test dais-bridge.tests/dais-bridge.tests.csproj
```

Expected: build SUCCESS, all tests pass.

- [ ] **Step 5: Commit.**

```bash
git add dais-bridge/Plugins/MemoryPlugin.cs dais-bridge/Program.cs dais-bridge.tests/Memory/MemoryPluginTests.cs
git commit -m "feat(memory): MemoryPlugin.Recall kernel function + DI wiring of MemoryRecallEngine"
```

---

## Phase D — Auto layer (DarbeesContextProvider + WhiteboardProvider)

Goal: implement the AIContextProvider that auto-extracts long-term facts from agent conversations and wire built-in `WhiteboardProvider`.

### Task D1: Confirm SK 1.75 AIContextProvider API

**Files:** none (research task)

The spec defers the exact override method name (`OnMessageAddedAsync` vs. `OnAIInvocationAsync` vs. `OnNewMessageAsync`). This task pins it down before writing code.

- [ ] **Step 1: Query Context7 for the SK 1.75 AIContextProvider abstract methods and required overrides.**

Use the Context7 query:

```
libraryId: /websites/learn_microsoft_en-us_semantic-kernel_frameworks_agent
query: "AIContextProvider abstract methods override pattern OnNewMessage OnSuspend OnResume OnAIInvocation Mem0Provider source"
```

Capture in this task list (commit message will note it):
- The exact base class name (`AIContextProvider` vs. `Microsoft.SemanticKernel.AIContextProvider` vs. namespaced under `Microsoft.SemanticKernel.Agents`)
- The exact lifecycle method overrides (`OnMessageAddedAsync`, `OnSuspendAsync`, `OnResumeAsync`, etc.)
- Whether `ModelInvokedAsync` / pre-invocation hook is exposed for adding extracted facts to context
- Required NuGet package(s) — likely `Microsoft.SemanticKernel.Agents.Abstractions` or `Microsoft.SemanticKernel.Agents.Core`

- [ ] **Step 2: Confirm the `Microsoft.SemanticKernel.Agents.*` package is referenced.**

```bash
grep -E "Agents" dais-bridge/dais-bridge.csproj
```

If absent, add a PackageReference in step 1 of Task D2.

- [ ] **Step 3: Skip commit — research only. Document findings inline in Task D2.**

---

### Task D2: DarbeesContextProvider scaffolding

**Files:**
- Create: `dais-bridge/Providers/DarbeesContextProvider.cs`
- Create: `dais-bridge.tests/Memory/DarbeesContextProviderTests.cs`
- Modify: `dais-bridge/dais-bridge.csproj` (add Agents package if needed)

- [ ] **Step 1: Add Agents NuGet packages if needed.**

If Task D1 found that `AIContextProvider` lives in `Microsoft.SemanticKernel.Agents.Abstractions`, edit the csproj:

```xml
    <PackageReference Include="Microsoft.SemanticKernel.Agents.Abstractions" Version="1.75.0" />
    <PackageReference Include="Microsoft.SemanticKernel.Agents.Core" Version="1.75.0" />
```

Run `dotnet restore`.

- [ ] **Step 2: Implement provider scaffolding.**

```csharp
using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;
using Darbee.Gateway.Models;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Darbee.Gateway.Providers;

public sealed class DarbeesContextProvider : AIContextProvider
{
    private readonly MemoryStore _store;
    private readonly ITenantContextAccessor _tenant;
    private readonly IFactExtractor _extractor;

    public DarbeesContextProvider(MemoryStore store, ITenantContextAccessor tenant, IFactExtractor extractor)
    {
        _store = store;
        _tenant = tenant;
        _extractor = extractor;
    }

    // The exact override signature is pinned by Task D1. Use the lifecycle method that fires
    // after each turn. Below is the SK 1.75 shape; if Task D1 finds a different one, adapt
    // the override but keep the body unchanged.
    public override async Task OnMessageAddedAsync(ChatMessageContent message, CancellationToken cancellationToken = default)
    {
        if (message.Role != AuthorRole.Assistant) return;
        var t = _tenant.Current;
        if (t is null) return;
        var facts = await _extractor.ExtractAsync(message.Content ?? "", cancellationToken);
        foreach (var f in facts)
        {
            var written = await _store.UpsertFactAsync(t.TenantId, f.Text, sourceThread: null, cancellationToken);
            foreach (var entityName in f.MentionedEntities)
            {
                var entityId = await _store.UpsertEntityAsync(t.TenantId, entityName, Array.Empty<string>(), "concept", cancellationToken);
                await _store.UpsertEdgeAsync(t.TenantId, $"memory_facts/{written.Id}", $"memory_entities/{entityId}", "mentions", 1.0, cancellationToken);
            }
        }
    }
}
```

- [ ] **Step 3: Define `IFactExtractor` and a stub implementation for tests.**

Create `dais-bridge/Providers/IFactExtractor.cs`:

```csharp
namespace Darbee.Gateway.Providers;

public sealed record ExtractedFact(string Text, IReadOnlyList<string> MentionedEntities);

public interface IFactExtractor
{
    Task<IReadOnlyList<ExtractedFact>> ExtractAsync(string text, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Write tests using the stub extractor.**

```csharp
using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;
using Darbee.Gateway.Models;
using Darbee.Gateway.Providers;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Darbee.Gateway.Tests.Memory;

[Trait("Category", "Integration")]
public class DarbeesContextProviderTests
{
    private sealed class StubExtractor(params ExtractedFact[] facts) : IFactExtractor
    {
        public Task<IReadOnlyList<ExtractedFact>> ExtractAsync(string text, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ExtractedFact>>(facts);
    }

    [Fact]
    public async Task OnMessageAddedAsync_AssistantMessage_ExtractsFactsAndCreatesEntityEdges()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDbStatic();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStoreWriteTests.ConstantEmbeddingClient(4);
            var store = new MemoryStore(MemoryStoreSchemaTests.ArangoUrlStatic, dbName,
                MemoryStoreSchemaTests.ArangoUserStatic, MemoryStoreSchemaTests.ArangoPassStatic,
                4, 1, http, emb);
            await store.EnsureSchemaAsync();
            var acc = new TenantContextAccessor { Current = TenantContext.ForKid("lila") };
            var provider = new DarbeesContextProvider(store, acc,
                new StubExtractor(new ExtractedFact("Lila prefers carrots over chips", new[] { "Lila", "carrots" })));

            await provider.OnMessageAddedAsync(new ChatMessageContent(AuthorRole.Assistant, "anything"));

            var facts = await store.ListByTenantAsync("kid:lila", MemoryKind.Fact);
            Assert.Single(facts);
            Assert.Equal("Lila prefers carrots over chips", facts[0].Text);
        }
        finally { await MemoryStoreSchemaTests.DropDbStatic(dbName); }
    }

    [Fact]
    public async Task OnMessageAddedAsync_UserMessage_DoesNothing()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDbStatic();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStoreWriteTests.ConstantEmbeddingClient(4);
            var store = new MemoryStore(MemoryStoreSchemaTests.ArangoUrlStatic, dbName,
                MemoryStoreSchemaTests.ArangoUserStatic, MemoryStoreSchemaTests.ArangoPassStatic,
                4, 1, http, emb);
            await store.EnsureSchemaAsync();
            var acc = new TenantContextAccessor { Current = TenantContext.Admin };
            var provider = new DarbeesContextProvider(store, acc,
                new StubExtractor(new ExtractedFact("ignored", new[] { "x" })));

            await provider.OnMessageAddedAsync(new ChatMessageContent(AuthorRole.User, "what?"));

            var facts = await store.ListByTenantAsync("admin", MemoryKind.Fact);
            Assert.Empty(facts);
        }
        finally { await MemoryStoreSchemaTests.DropDbStatic(dbName); }
    }
}
```

- [ ] **Step 5: If Task D1's findings indicate a different override name, update both the provider and tests to match.**

Specifically, if SK 1.75 uses `OnAIInvocationAsync(IList<ChatMessageContent>, ...)` instead, change:

```csharp
public override async Task OnAIInvocationAsync(IList<ChatMessageContent> messages, CancellationToken ct = default)
{
    foreach (var m in messages.Where(m => m.Role == AuthorRole.Assistant))
    {
        // identical body
    }
}
```

And update tests to call `OnAIInvocationAsync(new[] { msg })`.

- [ ] **Step 6: Run.**

```bash
$env:ARANGO_TEST_RUN="1"
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter "FullyQualifiedName~DarbeesContextProviderTests"
```

Expected: 2 PASS.

- [ ] **Step 7: Commit.**

```bash
git add dais-bridge/Providers/ dais-bridge.tests/Memory/DarbeesContextProviderTests.cs dais-bridge/dais-bridge.csproj
git commit -m "feat(memory): DarbeesContextProvider auto-extracts facts from assistant messages"
```

---

### Task D3: Hub wiring + WhiteboardProvider

**Files:**
- Modify: `dais-bridge/Hubs/KidSafeHub.cs`
- Modify: `dais-bridge/Hubs/ParentHub.cs`
- Modify: `dais-bridge/Program.cs`
- Create: `dais-bridge/Memory/LmStudioFactExtractor.cs`

- [ ] **Step 1: Implement LM Studio-backed `IFactExtractor`.**

```csharp
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Darbee.Gateway.Providers;

namespace Darbee.Gateway.Memory;

public sealed class LmStudioFactExtractor : IFactExtractor
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _modelId;

    public LmStudioFactExtractor(HttpClient http, string baseUrl, string modelId)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _modelId = modelId;
    }

    public async Task<IReadOnlyList<ExtractedFact>> ExtractAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<ExtractedFact>();
        var prompt = "Extract any new persistent facts from the message below. " +
                     "Return strict JSON: {\"facts\":[{\"text\":string,\"mentioned_entities\":[string,...]}]}. " +
                     "If no facts, return {\"facts\":[]}.\n\nMESSAGE:\n" + text;
        var body = new
        {
            model = _modelId,
            messages = new[] { new { role = "user", content = prompt } },
            temperature = 0.1,
            response_format = new { type = "json_object" }
        };
        try
        {
            using var response = await _http.PostAsJsonAsync($"{_baseUrl}/chat/completions", body, ct);
            response.EnsureSuccessStatusCode();
            var raw = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: ct);
            var content = raw?.Choices.FirstOrDefault()?.Message.Content ?? "{\"facts\":[]}";
            var parsed = JsonSerializer.Deserialize<FactsEnvelope>(content) ?? new FactsEnvelope();
            return parsed.Facts.Select(f => new ExtractedFact(f.Text, f.MentionedEntities)).ToList();
        }
        catch
        {
            return Array.Empty<ExtractedFact>();
        }
    }

    private sealed record ChatResponse([property: JsonPropertyName("choices")] List<Choice> Choices);
    private sealed record Choice([property: JsonPropertyName("message")] Message Message);
    private sealed record Message([property: JsonPropertyName("content")] string Content);
    private sealed record FactsEnvelope { [property: JsonPropertyName("facts")] public List<FactDto> Facts { get; init; } = new(); }
    private sealed record FactDto(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("mentioned_entities")] List<string> MentionedEntities);
}
```

- [ ] **Step 2: Register `IFactExtractor` and `DarbeesContextProvider` in DI.**

In `Program.cs`:

```csharp
        builder.Services.AddSingleton<IFactExtractor>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("memory");
            return new LmStudioFactExtractor(http, lmStudioUrl, modelId);
        });
        builder.Services.AddTransient<DarbeesContextProvider>();
```

Add `using Darbee.Gateway.Providers;` and `using Darbee.Gateway.Memory;` at the top.

- [ ] **Step 3: Wire into hubs.**

The exact pattern depends on how the hub creates an `AgentThread`. For now, surface `DarbeesContextProvider` and `WhiteboardProvider` for whoever holds the thread; the existing hubs just log echo messages, so the wiring sits on a `private void EnsureProviders(AgentThread thread)` helper to be invoked when an agent is actually run. Skeleton:

```csharp
// In KidSafeHub.cs (and ParentHub.cs), add a helper:
private void EnsureProviders(Microsoft.SemanticKernel.Agents.ChatHistoryAgentThread thread,
    DarbeesContextProvider longTerm,
    Microsoft.SemanticKernel.Agents.WhiteboardProvider whiteboard)
{
    if (!thread.AIContextProviders.Contains(longTerm)) thread.AIContextProviders.Add(longTerm);
    if (!thread.AIContextProviders.Contains(whiteboard)) thread.AIContextProviders.Add(whiteboard);
}
```

The actual `AgentThread` creation will land when the agent reasoning loop is wired into the hubs in a future phase. For now, this helper documents the integration shape.

- [ ] **Step 4: Build + tests.**

```bash
dotnet build dais-bridge/dais-bridge.csproj
$env:ARANGO_TEST_RUN="1"
dotnet test dais-bridge.tests/dais-bridge.tests.csproj
```

Expected: build SUCCESS; all tests pass.

- [ ] **Step 5: Commit.**

```bash
git add dais-bridge/Memory/LmStudioFactExtractor.cs dais-bridge/Hubs/ dais-bridge/Program.cs
git commit -m "feat(memory): LmStudioFactExtractor and hub wiring helpers for AIContextProviders"
```

---

## Phase E — Pending-embedding retry

### Task E1: PendingEmbeddingsService BackgroundService

**Files:**
- Create: `dais-bridge/Memory/PendingEmbeddingsService.cs`
- Create: `dais-bridge.tests/Memory/PendingEmbeddingsServiceTests.cs`

- [ ] **Step 1: Implement the background service.**

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Darbee.Gateway.Memory;

public sealed class PendingEmbeddingsOptions
{
    public int RetryIntervalSeconds { get; init; } = 30;
    public int MaxAttempts { get; init; } = 5;
    public int BatchSize { get; init; } = 10;
}

public sealed class PendingEmbeddingsService : BackgroundService
{
    private readonly MemoryStore _store;
    private readonly IEmbeddingClient _embeddings;
    private readonly ILogger<PendingEmbeddingsService> _logger;
    private readonly PendingEmbeddingsOptions _opt;

    public PendingEmbeddingsService(MemoryStore store, IEmbeddingClient embeddings,
        ILogger<PendingEmbeddingsService> logger, IOptions<PendingEmbeddingsOptions> opt)
    {
        _store = store;
        _embeddings = embeddings;
        _logger = logger;
        _opt = opt.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PendingEmbeddingsService loop error");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(_opt.RetryIntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task DrainOnceAsync(CancellationToken ct)
    {
        var batch = await _store.ListPendingEmbeddingsAsync(_opt.BatchSize);
        foreach (var (id, col, key) in batch)
        {
            try
            {
                var text = await _store.GetTextAsync(col, key, ct);
                if (text is null)
                {
                    await _store.RemovePendingAsync(id);
                    continue;
                }
                var emb = await _embeddings.EmbedAsync(text, ct);
                await _store.PatchEmbeddingAsync(col, key, emb);
                await _store.RemovePendingAsync(id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to embed pending {Col}/{Key}", col, key);
                var attempts = await _store.IncrementPendingAttemptsAsync(id, ex.Message);
                if (attempts >= _opt.MaxAttempts)
                {
                    await _store.MoveToDeadLetterAsync(id);
                }
            }
        }
    }
}
```

- [ ] **Step 2: Add the supporting helpers to `MemoryStore.cs`.**

```csharp
    public async Task<string?> GetTextAsync(string collection, string key, CancellationToken ct = default)
    {
        try
        {
            var doc = await _arango.Document.GetDocumentAsync<Dictionary<string, JsonElement>>(collection, key);
            return doc.TryGetValue("text", out var t) ? t.GetString() : null;
        }
        catch { return null; }
    }

    public async Task PatchEmbeddingAsync(string collection, string key, float[] embedding, CancellationToken ct = default)
    {
        await _arango.Document.PatchDocumentAsync<Dictionary<string, object?>, Dictionary<string, object?>>(
            collection, key, new Dictionary<string, object?>
            {
                ["embedding"] = embedding,
                ["status"] = "ready",
                ["updated_at"] = DateTime.UtcNow.ToString("O")
            });
    }

    public async Task RemovePendingAsync(string pendingKey, CancellationToken ct = default)
    {
        try { await _arango.Document.DeleteDocumentAsync<Dictionary<string, object?>>(MemoryCollections.PendingEmbeddings, pendingKey); }
        catch { /* best effort */ }
    }

    public async Task<int> IncrementPendingAttemptsAsync(string pendingKey, string lastError, CancellationToken ct = default)
    {
        var aql = "UPDATE @key WITH { attempts: OLD.attempts + 1, last_error: @err } IN @@col RETURN NEW.attempts";
        var bindVars = new Dictionary<string, object>
        {
            ["key"] = pendingKey,
            ["err"] = lastError,
            ["@col"] = MemoryCollections.PendingEmbeddings
        };
        var result = await QueryAsync<int>(aql, bindVars, ct);
        return result.FirstOrDefault();
    }

    public async Task MoveToDeadLetterAsync(string pendingKey, CancellationToken ct = default)
    {
        // For simplicity, dead-letter == set status='dead' on the pending doc; do not delete.
        var aql = "UPDATE @key WITH { status: 'dead', dead_at: @ts } IN @@col";
        var bindVars = new Dictionary<string, object>
        {
            ["key"] = pendingKey,
            ["ts"] = DateTime.UtcNow.ToString("O"),
            ["@col"] = MemoryCollections.PendingEmbeddings
        };
        await _arango.Cursor.PostCursorAsync<object>(new ArangoDBNetStandard.CursorApi.Models.PostCursorBody { Query = aql, BindVars = bindVars });
    }
```

- [ ] **Step 3: Write integration test for retry success.**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;

namespace Darbee.Gateway.Tests.Memory;

[Trait("Category", "Integration")]
public class PendingEmbeddingsServiceTests
{
    private sealed class FlakyEmbeddings : IEmbeddingClient
    {
        public int Dimension => 4;
        private bool _firstCallFailed;
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            if (!_firstCallFailed) { _firstCallFailed = true; throw new HttpRequestException("transient"); }
            return Task.FromResult(new[] { 0.1f, 0.1f, 0.1f, 0.1f });
        }
        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    [Fact]
    public async Task DrainOnceAsync_OnTransientFailure_RetriesAndCompletes()
    {
        if (!MemoryStoreSchemaTests.ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDbStatic();
        try
        {
            using var http = new HttpClient();
            var emb = new FlakyEmbeddings();
            var store = new MemoryStore(MemoryStoreSchemaTests.ArangoUrlStatic, dbName,
                MemoryStoreSchemaTests.ArangoUserStatic, MemoryStoreSchemaTests.ArangoPassStatic,
                4, 1, http, emb);
            await store.EnsureSchemaAsync();

            // First write fails to embed (queued)
            var write = await store.UpsertDecisionAsync("admin", "x", "a", "b", Array.Empty<string>());
            Assert.True(write.Queued);

            // Drain succeeds on second attempt
            var svc = new PendingEmbeddingsService(store, emb, NullLogger<PendingEmbeddingsService>.Instance,
                Options.Create(new PendingEmbeddingsOptions { MaxAttempts = 3, BatchSize = 10, RetryIntervalSeconds = 1 }));
            await svc.DrainOnceAsync(CancellationToken.None);

            var items = await store.ListByTenantAsync("admin", MemoryKind.Decision);
            Assert.Single(items);
            Assert.Equal("ready", items[0].Status);
        }
        finally { await MemoryStoreSchemaTests.DropDbStatic(dbName); }
    }
}
```

- [ ] **Step 4: Wire `PendingEmbeddingsService` into DI and host.**

In `Program.cs`:

```csharp
        builder.Services.Configure<PendingEmbeddingsOptions>(opts =>
        {
            opts.RetryIntervalSeconds = int.Parse(builder.Configuration["Memory:PendingEmbeddingRetryIntervalSeconds"] ?? "30");
            opts.MaxAttempts = int.Parse(builder.Configuration["Memory:PendingEmbeddingMaxAttempts"] ?? "5");
        });
        builder.Services.AddHostedService<PendingEmbeddingsService>();
```

- [ ] **Step 5: Run.**

```bash
$env:ARANGO_TEST_RUN="1"
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter "FullyQualifiedName~PendingEmbeddingsServiceTests"
```

Expected: 1 PASS.

- [ ] **Step 6: Commit.**

```bash
git add dais-bridge/Memory/PendingEmbeddingsService.cs dais-bridge/Memory/MemoryStore.cs dais-bridge/Program.cs dais-bridge.tests/Memory/PendingEmbeddingsServiceTests.cs
git commit -m "feat(memory): PendingEmbeddingsService retries failed embeddings with dead-letter cap"
```

---

## Phase F — Admin surface

### Task F1: AdminListMemories on kernel-admin only

**Files:**
- Create: `dais-bridge/Plugins/AdminMemoryPlugin.cs`
- Modify: `dais-bridge/Program.cs`

- [ ] **Step 1: Create the admin plugin.**

```csharp
using System.ComponentModel;
using System.Text.Json;
using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;
using Darbee.Gateway.Models;
using Microsoft.SemanticKernel;

namespace Darbee.Gateway.Plugins;

public sealed class AdminMemoryPlugin
{
    private readonly MemoryStore _store;
    private readonly ITenantContextAccessor _tenant;

    public AdminMemoryPlugin(MemoryStore store, ITenantContextAccessor tenant)
    {
        _store = store;
        _tenant = tenant;
    }

    [KernelFunction, Description("(Admin only) Lists memories for any tenant. Caller MUST be on kernel-admin.")]
    public async Task<string> AdminListMemories(
        [Description("Tenant id to inspect, e.g. 'kid:lila' or 'admin'")] string tenantId,
        [Description("Memory kind: Decision | Observation | Fact | Summary")] string kind,
        [Description("Max items (default 50)")] int limit = 50)
    {
        var current = _tenant.Required;
        if (current.TenantId != "admin")
        {
            throw new InvalidOperationException("AdminListMemories can only be called from the admin tenant.");
        }
        var memKind = Enum.Parse<MemoryKind>(kind, ignoreCase: true);
        var items = await _store.ListByTenantAsync(tenantId, memKind, limit);
        return JsonSerializer.Serialize(items.Select(i => new
        {
            i.Key, kind = i.Kind.ToString(), i.Text, i.TenantId, i.Status, i.CreatedAt
        }));
    }
}
```

- [ ] **Step 2: Register on `kernel-admin` only.**

In `Program.cs`, in the `kernel-admin` registration block:

```csharp
            kernelBuilder.Plugins.AddFromObject(new AdminMemoryPlugin(memStore, memTenant), "AdminMemory");
```

(Do **not** add this line to `kernel-kidsafe`.)

- [ ] **Step 3: Build + run.**

```bash
dotnet build dais-bridge/dais-bridge.csproj
dotnet test dais-bridge.tests/dais-bridge.tests.csproj
```

Expected: build SUCCESS, all tests pass.

- [ ] **Step 4: Commit.**

```bash
git add dais-bridge/Plugins/AdminMemoryPlugin.cs dais-bridge/Program.cs
git commit -m "feat(memory): AdminMemoryPlugin registered on kernel-admin only with tenant-id check"
```

---

### Task F2: ParentHub ListMemories method

**Files:**
- Modify: `dais-bridge/Hubs/ParentHub.cs`

- [ ] **Step 1: Add the SignalR method.**

Append to `ParentHub.cs`:

```csharp
    private readonly MemoryStore _memory;

    public ParentHub(ILogger<ParentHub> logger, ITenantContextAccessor tenantAccessor, MemoryStore memory)
    {
        _logger = logger;
        _tenantAccessor = tenantAccessor;
        _memory = memory;
    }

    public async Task<object> ListMemories(string targetTenantId, string kind, int limit = 50)
    {
        SetTenant();
        var memKind = Enum.Parse<MemoryKind>(kind, ignoreCase: true);
        var items = await _memory.ListByTenantAsync(targetTenantId, memKind, limit);
        return new
        {
            tenantId = targetTenantId,
            kind,
            items = items.Select(i => new { i.Key, kind = i.Kind.ToString(), i.Text, i.TenantId, i.Status, i.CreatedAt })
        };
    }
```

Update the constructor; remove the duplicate ones (consolidate into one constructor that takes all three dependencies).

Add `using Darbee.Gateway.Memory; using Darbee.Gateway.Memory.Models;` at the top.

- [ ] **Step 2: Build + run.**

```bash
dotnet build dais-bridge/dais-bridge.csproj
dotnet test dais-bridge.tests/dais-bridge.tests.csproj
```

Expected: build SUCCESS, all tests pass.

- [ ] **Step 3: Commit.**

```bash
git add dais-bridge/Hubs/ParentHub.cs
git commit -m "feat(parent): ParentHub.ListMemories surfaces cross-tenant memory listing"
```

---

## Phase G — Docs + CI

### Task G1: HANDOFF.md, README, security notes

**Files:**
- Modify: `HANDOFF.md`
- Modify: `README.md` (or create memory-specific notes if absent)
- Modify: `docs/security-notes.md` (if relevant)

- [ ] **Step 1: Add Phase 11 entry to `HANDOFF.md`.**

After the existing Phase 10 section, insert a Phase 11 entry summarizing:
- Replaced stubbed `ArangoPlugin` with `MemoryPlugin` + `Memory/` namespace
- New `MemoryStore` (schema migration, two-phase writes, vector index via raw HTTP)
- `MemoryRecallEngine` hybrid recall (graph expand + vector top-K)
- `DarbeesContextProvider` AIContextProvider auto-fact extraction
- `PendingEmbeddingsService` background retry
- `AdminMemoryPlugin` on `kernel-admin` only; `ParentHub.ListMemories` exposed via SignalR
- Cross-tenant isolation enforced via `TenantContext` AsyncLocal accessor
- Embedding via LM Studio `/v1/embeddings` (default `nomic-embed-text-v1.5`, 768 dim)
- Dependency: ArangoDB 3.12.x (vector index, currently an experimental feature; may require `--experimental-vector-index` startup option)

Also append HANDOFF anti-pattern #11: "Don't expose tenant ID as an LLM-bound kernel-function parameter; always read from `ITenantContextAccessor` set by the hub." Number is 11 because 10 already exists.

- [ ] **Step 2: Add ArangoDB version requirement to `README.md`.**

Find the "Tech stack" or "Dependencies" section and add: "ArangoDB 3.12.x (vector index used by Memory layer; currently an experimental feature)".

- [ ] **Step 3: Add a Memory section to `README.md`.**

Add a top-level section explaining:
- Memory layer architecture (link to spec)
- Required services (LM Studio with embedding model loaded; ArangoDB)
- How to run integration tests locally
- How tenant isolation works at a high level

- [ ] **Step 4: Commit.**

```bash
git add HANDOFF.md README.md
git commit -m "docs(memory): document Phase 11 graph-backed RAG implementation"
```

---

### Task G2: CI ArangoDB service container

**Files:**
- Modify: `.github/workflows/ci.yml`

- [ ] **Step 1: Add ArangoDB service to the `dotnet test` job.**

Find the existing job that runs `dotnet test` and add (or amend):

```yaml
      services:
        arango:
          image: arangodb:3.12
          env:
            ARANGO_ROOT_PASSWORD: password
          ports:
            - 8529:8529
          options: >-
            --health-cmd="curl -fsS -u root:password http://localhost:8529/_api/version >/dev/null"
            --health-interval=5s
            --health-timeout=2s
            --health-retries=12
```

In the same job, add an env var so integration tests run:

```yaml
      env:
        ARANGO_TEST_URL: http://localhost:8529
        ARANGO_TEST_USER: root
        ARANGO_TEST_PASS: password
```

- [ ] **Step 2: Verify locally before pushing.**

If you have `act` or another local CI runner, run the workflow. Otherwise, push to a branch and observe the GitHub Actions run.

```bash
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter "Category=Integration"
```

Expected: integration tests run and pass with `ARANGO_TEST_URL` set.

- [ ] **Step 3: Commit.**

```bash
git add .github/workflows/ci.yml
git commit -m "ci(memory): add ArangoDB 3.12 service container so integration tests run on PRs"
```

---

## Final verification

- [ ] **Run all tests with full integration coverage.**

```bash
$env:ARANGO_TEST_RUN="1"
dotnet test dais-bridge.tests/dais-bridge.tests.csproj
```

Expected: all unit + integration tests pass.

- [ ] **Run the gateway and verify schema bootstrap.**

```bash
dotnet run --project dais-bridge/dais-bridge.csproj
```

Expected logs:
- "🚀 Darbee Sovereign Gateway Initializing..."
- ArangoDB collections created or already-present
- No exceptions

- [ ] **Browse to `http://localhost:5000/`** (or whatever port). Expected: "Darbee Sovereign AI Gateway Active".

- [ ] **Confirm spec coverage.** Open `docs/superpowers/specs/2026-05-09-graph-backed-rag-design.md` and check that every section has a corresponding task in this plan. The plan as written maps:
  - §3.1 layered model → Tasks D2, D3
  - §3.2 module layout → Tasks A1–A6, C1–C4, D2–D3
  - §3.3 service registration → Task A6, C4, D3, E1, F1
  - §3.4 plugin surface → Tasks B2, C4, F1
  - §4 schema → Task A4
  - §5 hybrid recall → Tasks C1–C3
  - §6 write paths → Tasks A5, D2
  - §7 tenant isolation → Tasks B1, B4, B5
  - §8 error handling → Tasks A5 (queue), C3 (graph-only fallback), E1 (retry)
  - §9 testing → All test tasks
  - §10 configuration → Task A6
  - §11 migration/rollout → Tasks A6 (EnsureSchemaAsync at startup), B3 (delete ArangoPlugin)
  - §13 anti-patterns → Tasks B1 (tenant in DI not parameter), A4 (bind-vars only)

---

## Notes for implementers

- **Sequential edits to `Program.cs` and `MemoryStore.cs`.** Never run two `Edit` operations on these files in parallel — they grow across many tasks. (HANDOFF anti-pattern #5.)
- **Vector index dimension is locked at first migration.** If `EmbeddingDimension` config changes after data exists, the index must be dropped and recreated, and existing items re-embedded. Currently no automation for this.
- **Integration tests use a unique-per-test database** to avoid cross-test pollution. The fixture cost is ~200ms per test; acceptable.
- **Avoid `&&` as command separator on PowerShell** (HANDOFF anti-pattern #8) — use `;` or pipeline operators.
- **ArangoDBNetStandard 2.0.0** does not expose vector index typed APIs; raw HTTP via the `MemoryStore`'s injected `HttpClient` is intentional, not a workaround.
