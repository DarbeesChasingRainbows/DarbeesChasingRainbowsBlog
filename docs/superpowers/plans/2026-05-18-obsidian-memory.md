# Obsidian ↔ Memory Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Native Obsidian plugin ingests notes flagged `memory: true` into private-tenant collections (observations/facts/decisions) and exposes a sidebar search across posts and notes with a scope toggle.

**Architecture:** Plugin runs in Obsidian on the host, posts JSON to `localhost:5000` via `obsidian.requestUrl` (bypasses CORS). Bridge gains `POST /api/memory/ingest-notes` (upsert + tenant-scoped stale-delete) and extends `/api/memory/search` to accept `kinds` + `tenants` arrays. MemoryStore gains `UpsertNoteAsync` and `DeleteStaleNotesAsync`. Same Arango DB as posts; `tenant_id` is the isolation field. Same embedding model (qwen3-embedding-8b on :8081).

**Tech Stack:** C# .NET 9 + xUnit + ArangoDB-NETStandard (bridge); TypeScript + Obsidian Plugin API + esbuild + Vitest (plugin); same llama.cpp `llama-server` :8081 for embeddings.

**Spec:** `docs/superpowers/specs/2026-05-18-obsidian-memory-design.md` (commit `5c96b67`).

**Branch:** `feature/obsidian-memory` (already created off `master`).

---

## File Map

### Bridge (C#)

| Path | Action | Responsibility |
|---|---|---|
| `dais-bridge/Endpoints/ContentRagDtos.cs` | modify | Add `IngestNotesRequest`, `NoteRecord`, `IngestNotesResponse`, `IngestNoteResult`; extend `SearchRequest` with `Tenants`; extend `SearchResult` with `Kind` + `Tenant` |
| `dais-bridge/Memory/Models/NoteDocument.cs` | create | Input record for `UpsertNoteAsync` |
| `dais-bridge/Memory/Models/UpsertNoteResult.cs` | create | Per-note outcome record |
| `dais-bridge/Memory/MemoryStore.cs` | modify | Add `UpsertNoteAsync` + `DeleteStaleNotesAsync` |
| `dais-bridge/Endpoints/ContentRagEndpoints.cs` | modify | Extend `HandleSearchAsync` for new kinds + `Tenants`; add `HandleIngestNotesAsync`; wire route |
| `dais-bridge/Program.cs` | modify | Register the new `/api/memory/ingest-notes` POST route |
| `dais-bridge.tests/Memory/MemoryStoreNotesTests.cs` | create | 6 integration tests (Arango-backed) for new MemoryStore methods |
| `dais-bridge.tests/Endpoints/ContentRagEndpointsTests.cs` | modify | +5 endpoint tests (ingest-notes, search back-compat, kinds/tenants filter, isolation guard, union ordering) |

### Plugin (TypeScript)

| Path | Action | Responsibility |
|---|---|---|
| `obsidian-plugin/package.json` | create | Plugin npm config: name, scripts (`build`, `dev`, `test`), deps (obsidian, esbuild, vitest, typescript) |
| `obsidian-plugin/tsconfig.json` | create | TS config (ES2022, strict, Obsidian types) |
| `obsidian-plugin/esbuild.config.mjs` | create | Build config: bundle to `main.js`, externals (obsidian, electron) |
| `obsidian-plugin/manifest.json` | create | Obsidian plugin manifest (id `darbee-memory`, version, minAppVersion) |
| `obsidian-plugin/versions.json` | create | Obsidian version map |
| `obsidian-plugin/src/types.ts` | create | Shared interfaces: `Settings`, `NoteRecord`, `IngestPayload`, `SearchHit`, `Scope` |
| `obsidian-plugin/src/ingester.ts` | create | Pure: `parseNoteFrontmatter`, `deriveNoteKey`, `stripMdx`, `buildIngestPayload`, `scopeToFilters` |
| `obsidian-plugin/src/bridge-client.ts` | create | `requestUrl` wrappers: `ingestNotes()`, `searchMemory()` with timeout |
| `obsidian-plugin/src/settings.ts` | create | `DEFAULT_SETTINGS` + `DarbeeMemorySettingTab` |
| `obsidian-plugin/src/sidebar-view.ts` | create | Obsidian `ItemView` subclass: query input, scope toggle, result cards |
| `obsidian-plugin/src/main.ts` | create | Plugin entry: vault events, debounce, commands, sidebar registration, settings |
| `obsidian-plugin/test/ingester.test.ts` | create | 8 Vitest unit tests for pure functions |
| `obsidian-plugin/test/main.test.ts` | create | 4 Vitest integration tests with in-memory `Vault` stub |
| `obsidian-plugin/test/obsidian-stub.ts` | create | Minimal in-memory Vault/Workspace/App stub for tests |
| `obsidian-plugin/README.md` | create | Install / dev / uninstall instructions |

### Repo root

| Path | Action | Responsibility |
|---|---|---|
| `package.json` | modify | Add `obsidian:build`, `obsidian:dev`, `obsidian:link`, `obsidian:unlink`, `test:plugin` scripts |
| `scripts/obsidian-link.sh` | create | Symlink `obsidian-plugin/dist/` into `.obsidian/plugins/darbee-memory/` |
| `scripts/obsidian-unlink.sh` | create | Remove the symlink |
| `.gitignore` | modify | Ignore `obsidian-plugin/node_modules/`, `obsidian-plugin/dist/` |
| `CLAUDE.md` | modify | Add 2 rows to authoring-scripts table; new "Memory ingest" subsection in caveats |

---

## Task 1: Bridge DTOs

**Files:**
- Modify: `/home/deovolente/repos/DarbeesChasingRainbows/dais-bridge/Endpoints/ContentRagDtos.cs`

- [ ] **Step 1: Read the current DTO file**

Read `dais-bridge/Endpoints/ContentRagDtos.cs` to confirm the current `SearchRequest`/`SearchResult` shapes match the spec. Current state (verified at plan-write time, commit `5c96b67`):

```csharp
public sealed record SearchRequest(
    string Query,
    IReadOnlyList<string>? Kinds,
    int K,
    string? Tenant);

public sealed record SearchResult(
    string Slug,
    string Collection,
    string Title,
    string MatchedKind,
    double Score,
    string Snippet,
    string Url);
```

- [ ] **Step 2: Extend `SearchRequest` and `SearchResult`; add ingest DTOs**

Append to `dais-bridge/Endpoints/ContentRagDtos.cs`:

```csharp
public sealed record SearchRequestV2(
    string Query,
    IReadOnlyList<string>? Kinds,
    int K,
    string? Tenant,
    IReadOnlyList<string>? Tenants);

public sealed record NoteRecord(
    string Key,
    string Kind,
    string Text,
    string Title,
    IReadOnlyDictionary<string, object>? Metadata);

public sealed record IngestNotesRequest(
    string Tenant,
    IReadOnlyList<NoteRecord> Notes,
    IReadOnlyList<string> CurrentKeys);

public sealed record IngestNoteResult(
    string Key,
    string Outcome,
    string? Reason);

public sealed record IngestNotesResponse(
    int EmbeddedCount,
    int CachedCount,
    int FailedCount,
    int StaleDeletedCount,
    long DurationMs,
    IReadOnlyList<IngestNoteResult> PerNote);
```

Replace the existing `SearchRequest` with this updated version (back-compat: `Tenant` stays for old callers; `Tenants` plural is new):

```csharp
public sealed record SearchRequest(
    string Query,
    IReadOnlyList<string>? Kinds,
    int K,
    string? Tenant,
    IReadOnlyList<string>? Tenants = null);
```

Replace the existing `SearchResult` with:

```csharp
public sealed record SearchResult(
    string Slug,
    string Collection,
    string Title,
    string MatchedKind,
    double Score,
    string Snippet,
    string Url,
    string Kind = "post",
    string Tenant = "public");
```

Delete the `SearchRequestV2` from the appended block (introduced only as a comparison aid; the modified `SearchRequest` covers it).

- [ ] **Step 3: Run dotnet build to confirm DTOs compile**

```bash
cd /home/deovolente/repos/DarbeesChasingRainbows
dotnet build dais-bridge/dais-bridge.csproj
```

Expected: build succeeds with the new types resolved.

- [ ] **Step 4: Commit**

```bash
git add dais-bridge/Endpoints/ContentRagDtos.cs
git commit -m "feat(bridge): DTOs for ingest-notes and tenant-list search"
```

---

## Task 2: Memory model records

**Files:**
- Create: `/home/deovolente/repos/DarbeesChasingRainbows/dais-bridge/Memory/Models/NoteDocument.cs`
- Create: `/home/deovolente/repos/DarbeesChasingRainbows/dais-bridge/Memory/Models/UpsertNoteResult.cs`

- [ ] **Step 1: Create `NoteDocument.cs`**

```csharp
namespace Darbee.Gateway.Memory.Models;

public sealed record NoteDocument(
    string Key,
    string Title,
    string Text,
    MemoryKind Kind,
    string TenantId,
    IReadOnlyDictionary<string, object>? Metadata = null);
```

- [ ] **Step 2: Create `UpsertNoteResult.cs`**

```csharp
namespace Darbee.Gateway.Memory.Models;

public sealed record UpsertNoteResult(
    string Key,
    VectorWriteOutcome Outcome,
    string? Reason = null);
```

- [ ] **Step 3: Build to confirm types resolve**

```bash
dotnet build dais-bridge/dais-bridge.csproj
```

Expected: success.

- [ ] **Step 4: Commit**

```bash
git add dais-bridge/Memory/Models/NoteDocument.cs dais-bridge/Memory/Models/UpsertNoteResult.cs
git commit -m "feat(bridge): NoteDocument and UpsertNoteResult records"
```

---

## Task 3: `MemoryStore.UpsertNoteAsync` (TDD)

**Files:**
- Create: `/home/deovolente/repos/DarbeesChasingRainbows/dais-bridge.tests/Memory/MemoryStoreNotesTests.cs`
- Modify: `/home/deovolente/repos/DarbeesChasingRainbows/dais-bridge/Memory/MemoryStore.cs`

- [ ] **Step 1: Write the failing test file**

Create `dais-bridge.tests/Memory/MemoryStoreNotesTests.cs`:

```csharp
using System.Net.Http;
using System.Text.Json;
using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;

namespace Darbee.Gateway.Tests.Memory;

[Trait("Category", "Integration")]
public class MemoryStoreNotesTests
{
    private static string ArangoUrl => MemoryStoreSchemaTests.ArangoUrl;
    private static string ArangoUser => MemoryStoreSchemaTests.ArangoUser;
    private static string ArangoPass => MemoryStoreSchemaTests.ArangoPass;
    private static bool ArangoEnabled => MemoryStoreSchemaTests.ArangoEnabled;

    private sealed class StubEmbeddingClient : IEmbeddingClient
    {
        public int Dimension { get; set; } = 4;
        public int EmbedCalls { get; private set; }

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            EmbedCalls++;
            return Task.FromResult(new[] { 0.1f, 0.2f, 0.3f, 0.4f });
        }

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            EmbedCalls += texts.Count;
            return Task.FromResult<IReadOnlyList<float[]>>(
                texts.Select(_ => new[] { 0.1f, 0.2f, 0.3f, 0.4f }).ToArray());
        }
    }

    private static NoteDocument MakeNote(string key = "obsidian://daily/note.md",
                                          MemoryKind kind = MemoryKind.Observation,
                                          string text = "I noticed the cast iron pan rusts in the trailer.",
                                          string tenant = "private") =>
        new NoteDocument(
            Key: key,
            Title: "Note",
            Text: text,
            Kind: kind,
            TenantId: tenant,
            Metadata: new Dictionary<string, object> { ["source"] = "obsidian", ["tags"] = new[] { "rv" } });

    [Fact]
    public async Task UpsertNoteAsync_FreshNote_WritesOneDocToObservations()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new StubEmbeddingClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

            var result = await store.UpsertNoteAsync(MakeNote());

            Assert.Equal(VectorWriteOutcome.Embedded, result.Outcome);
            Assert.Equal(1, emb.EmbedCalls);

            using var doc = await store.ReadNoteDocumentAsync(MemoryCollections.Observations, MakeNote().Key);
            Assert.NotNull(doc);
            Assert.Equal("private", doc!.RootElement.GetProperty("tenant_id").GetString());
            Assert.Equal("ready", doc.RootElement.GetProperty("status").GetString());
            Assert.Equal("obsidian", doc.RootElement.GetProperty("source").GetString());
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task UpsertNoteAsync_SameNoteTwice_SecondIsCacheHit()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new StubEmbeddingClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

            await store.UpsertNoteAsync(MakeNote());
            var callsAfterFirst = emb.EmbedCalls;
            var result2 = await store.UpsertNoteAsync(MakeNote());

            Assert.Equal(VectorWriteOutcome.Cached, result2.Outcome);
            Assert.Equal(callsAfterFirst, emb.EmbedCalls);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task UpsertNoteAsync_HashChanges_ReembedsAndOverwrites()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new StubEmbeddingClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

            await store.UpsertNoteAsync(MakeNote(text: "first version"));
            var callsAfterFirst = emb.EmbedCalls;
            var result2 = await store.UpsertNoteAsync(MakeNote(text: "second version"));

            Assert.Equal(VectorWriteOutcome.Embedded, result2.Outcome);
            Assert.Equal(callsAfterFirst + 1, emb.EmbedCalls);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task UpsertNoteAsync_KindRoutesToCorrectCollection()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new StubEmbeddingClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

            await store.UpsertNoteAsync(MakeNote(kind: MemoryKind.Fact, key: "obsidian://f1.md"));

            using var inFacts = await store.ReadNoteDocumentAsync(MemoryCollections.Facts, "obsidian://f1.md");
            using var inObs = await store.ReadNoteDocumentAsync(MemoryCollections.Observations, "obsidian://f1.md");

            Assert.NotNull(inFacts);
            Assert.Null(inObs);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }
}
```

- [ ] **Step 2: Run the tests to confirm they fail to compile**

```bash
ARANGO_TEST_RUN=1 dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter FullyQualifiedName~MemoryStoreNotesTests 2>&1 | tail -20
```

Expected: compilation error — `UpsertNoteAsync` and `ReadNoteDocumentAsync` don't exist yet.

- [ ] **Step 3: Implement `UpsertNoteAsync` and `ReadNoteDocumentAsync` in MemoryStore.cs**

Open `dais-bridge/Memory/MemoryStore.cs`. After the existing `UpsertOnePostVectorAsync` private method (around line 406+), add these public methods. Place them logically in the file — adjacent to `UpsertPostAsync`.

```csharp
public async Task<UpsertNoteResult> UpsertNoteAsync(NoteDocument note, CancellationToken ct = default)
{
    if (_embeddings is null)
        throw new InvalidOperationException("MemoryStore was constructed without an IEmbeddingClient — cannot upsert notes");

    await EnsureSchemaAsync(ct);

    var collection = MemoryCollections.ForKind(note.Kind);
    var arangoKey = Sha1Hex(note.Key);
    var hash = Sha256Hex($"{_embeddingModelId}\n{note.Text}");

    // Cache check: if existing doc has same hash AND status=ready, skip embed.
    using var existing = await ReadNoteDocumentAsync(collection, note.Key, ct);
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
        throw new EmbeddingConfigMismatchException(
            $"Embedding dimension mismatch: expected {_embeddingDimension}, got {embedding.Length}");

    var now = DateTime.UtcNow.ToString("o");
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
        ["created_at"] = existing is null ? now : existing.RootElement.GetProperty("created_at").GetString(),
        ["updated_at"] = now,
    };

    await InsertRawPostAsync(doc, collection, ct);
    return new UpsertNoteResult(note.Key, VectorWriteOutcome.Embedded);
}

public async Task<JsonDocument?> ReadNoteDocumentAsync(string collection, string noteKey, CancellationToken ct = default)
{
    var arangoKey = Sha1Hex(noteKey);
    return await ReadDocumentAsync(collection, arangoKey, ct);
}

private static string Sha1Hex(string s)
{
    using var sha = System.Security.Cryptography.SHA1.Create();
    var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
    return Convert.ToHexString(bytes).ToLowerInvariant();
}

private static string Sha256Hex(string s)
{
    using var sha = System.Security.Cryptography.SHA256.Create();
    var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
    return Convert.ToHexString(bytes).ToLowerInvariant();
}
```

`InsertRawPostAsync` is the existing helper that takes a collection name plus a doc dict and does a PUT-or-POST upsert. `ReadDocumentAsync` is a similar existing private helper. If either is missing the collection parameter, look for the post-specific version and add a `collection` parameter overload. (If the existing helpers are hard-coded to `memory_posts`, refactor them in this same task to accept a collection argument — the change is mechanical and well-bounded.)

- [ ] **Step 4: Run the tests to confirm they pass**

```bash
ARANGO_TEST_RUN=1 dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter FullyQualifiedName~MemoryStoreNotesTests 2>&1 | tail -10
```

Expected: 4/4 pass.

- [ ] **Step 5: Commit**

```bash
git add dais-bridge/Memory/MemoryStore.cs dais-bridge.tests/Memory/MemoryStoreNotesTests.cs
git commit -m "feat(memory): UpsertNoteAsync routes by kind, caches by hash"
```

---

## Task 4: `MemoryStore.DeleteStaleNotesAsync` (TDD)

**Files:**
- Modify: `/home/deovolente/repos/DarbeesChasingRainbows/dais-bridge.tests/Memory/MemoryStoreNotesTests.cs`
- Modify: `/home/deovolente/repos/DarbeesChasingRainbows/dais-bridge/Memory/MemoryStore.cs`

- [ ] **Step 1: Append failing tests to `MemoryStoreNotesTests.cs`**

Append these two tests to the test class:

```csharp
[Fact]
public async Task DeleteStaleNotesAsync_ScopedByTenantAndSource()
{
    if (!ArangoEnabled) return;
    var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
    try
    {
        using var http = new HttpClient();
        var emb = new StubEmbeddingClient();
        var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
            "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

        // Seed 1 post + 2 obsidian notes + 1 private-tenant non-obsidian doc.
        await store.UpsertPostAsync(MemoryStorePostsTests.MakePost(slug: "one"), force: false);
        await store.UpsertNoteAsync(MakeNote(key: "obsidian://a.md"));
        await store.UpsertNoteAsync(MakeNote(key: "obsidian://b.md"));
        await store.InsertRawPostAsync(new Dictionary<string, object?>
        {
            ["_key"] = "manual1",
            ["note_key"] = "manual://x.md",
            ["tenant_id"] = "private",
            ["kind"] = "observation",
            ["title"] = "Manual",
            ["text"] = "x",
            ["hash"] = "h",
            ["embedding"] = new float[] { 0.1f, 0.2f, 0.3f, 0.4f },
            ["status"] = "ready",
            ["source"] = (string?)null, // intentionally NOT "obsidian"
            ["metadata"] = new Dictionary<string, object>(),
            ["created_at"] = DateTime.UtcNow.ToString("o"),
            ["updated_at"] = DateTime.UtcNow.ToString("o"),
        }, MemoryCollections.Observations);

        // currentKeys empty -> all obsidian-sourced private notes should be removed.
        var deleted = await store.DeleteStaleNotesAsync(Array.Empty<string>(), "private");

        Assert.Equal(2, deleted);
        Assert.NotNull(await store.ReadPostDocumentAsync("blog__one__summary")); // post untouched
        Assert.NotNull(await store.ReadNoteDocumentAsync(MemoryCollections.Observations, "manual://x.md")); // non-obsidian untouched
        Assert.Null(await store.ReadNoteDocumentAsync(MemoryCollections.Observations, "obsidian://a.md"));
        Assert.Null(await store.ReadNoteDocumentAsync(MemoryCollections.Observations, "obsidian://b.md"));
    }
    finally
    {
        await MemoryStoreSchemaTests.DropDb(dbName);
    }
}

[Fact]
public async Task DeleteStaleNotesAsync_DoesNotTouchMemoryPosts()
{
    if (!ArangoEnabled) return;
    var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
    try
    {
        using var http = new HttpClient();
        var emb = new StubEmbeddingClient();
        var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
            "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

        await store.UpsertPostAsync(MemoryStorePostsTests.MakePost(slug: "p1"), force: false);
        await store.UpsertPostAsync(MemoryStorePostsTests.MakePost(slug: "p2"), force: false);

        var deleted = await store.DeleteStaleNotesAsync(Array.Empty<string>(), "private");

        Assert.Equal(0, deleted);
        Assert.NotNull(await store.ReadPostDocumentAsync("blog__p1__summary"));
        Assert.NotNull(await store.ReadPostDocumentAsync("blog__p2__summary"));
    }
    finally
    {
        await MemoryStoreSchemaTests.DropDb(dbName);
    }
}
```

Both tests reference `MemoryStorePostsTests.MakePost` (already exists). If `MakePost` is `internal static`, no extra work; if `private`, expose it as `internal static` in `MemoryStorePostsTests.cs` (one-word change).

- [ ] **Step 2: Run tests; they fail because `DeleteStaleNotesAsync` doesn't exist**

```bash
ARANGO_TEST_RUN=1 dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter FullyQualifiedName~MemoryStoreNotesTests
```

Expected: compilation error.

- [ ] **Step 3: Implement `DeleteStaleNotesAsync` in MemoryStore.cs**

Add this public method to `MemoryStore` (adjacent to `DeleteStalePostsAsync`):

```csharp
public async Task<int> DeleteStaleNotesAsync(
    IReadOnlyList<string> currentKeys,
    string tenant,
    CancellationToken ct = default)
{
    await EnsureSchemaAsync(ct);

    int totalDeleted = 0;
    foreach (var collection in new[]
    {
        MemoryCollections.Observations,
        MemoryCollections.Facts,
        MemoryCollections.Decisions,
    })
    {
        var aql = @$"
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
        var removed = await RunAqlCountAsync(aql, bindVars, ct);
        totalDeleted += removed;
    }
    return totalDeleted;
}
```

`RunAqlCountAsync(aql, bindVars, ct)` is a thin helper that POSTs to `/_api/cursor` and returns the result-array length. If it doesn't exist, add it as a private method (look at the existing AQL helpers in MemoryStore — there's almost certainly one for `DeleteStalePostsAsync` to crib from).

- [ ] **Step 4: Run tests; expect both new tests to pass**

```bash
ARANGO_TEST_RUN=1 dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter FullyQualifiedName~MemoryStoreNotesTests 2>&1 | tail -10
```

Expected: 6/6 pass (4 from Task 3 + 2 new).

- [ ] **Step 5: Commit**

```bash
git add dais-bridge/Memory/MemoryStore.cs dais-bridge.tests/Memory/MemoryStoreNotesTests.cs
git commit -m "feat(memory): DeleteStaleNotesAsync scoped by tenant + source"
```

---

## Task 5: Extend `HandleSearchAsync` (TDD)

**Files:**
- Modify: `/home/deovolente/repos/DarbeesChasingRainbows/dais-bridge.tests/Endpoints/ContentRagEndpointsTests.cs`
- Modify: `/home/deovolente/repos/DarbeesChasingRainbows/dais-bridge/Endpoints/ContentRagEndpoints.cs`

- [ ] **Step 1: Append failing tests to `ContentRagEndpointsTests.cs`**

Add four tests in the existing test class:

```csharp
[Fact]
public async Task HandleSearchAsync_BackCompat_DefaultsToPostsPublic()
{
    if (!ArangoEnabled) return;
    var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
    try
    {
        using var http = new HttpClient();
        var emb = new MemoryStoreNotesTests.StubEmbeddingClient();
        var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
            "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);
        await store.UpsertPostAsync(MemoryStorePostsTests.MakePost(slug: "p1"), force: false);
        await store.UpsertNoteAsync(MemoryStoreNotesTests.MakeNote(key: "obsidian://n1.md"));

        var req = new SearchRequest(Query: "hello", Kinds: null, K: 5, Tenant: null, Tenants: null);
        var resp = await ContentRagEndpoints.HandleSearchAsync(req, store, emb);

        Assert.All(resp.Results, r => Assert.Equal("post", r.Kind));
        Assert.All(resp.Results, r => Assert.Equal("public", r.Tenant));
    }
    finally
    {
        await MemoryStoreSchemaTests.DropDb(dbName);
    }
}

[Fact]
public async Task HandleSearchAsync_KindsFilter_ReturnsOnlyObservations()
{
    if (!ArangoEnabled) return;
    var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
    try
    {
        using var http = new HttpClient();
        var emb = new MemoryStoreNotesTests.StubEmbeddingClient();
        var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
            "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);
        await store.UpsertPostAsync(MemoryStorePostsTests.MakePost(slug: "p1"), force: false);
        await store.UpsertNoteAsync(MemoryStoreNotesTests.MakeNote(key: "obsidian://n1.md"));

        var req = new SearchRequest(
            Query: "hello",
            Kinds: new[] { "observation" },
            K: 5,
            Tenant: null,
            Tenants: new[] { "private" });
        var resp = await ContentRagEndpoints.HandleSearchAsync(req, store, emb);

        Assert.All(resp.Results, r => Assert.Equal("observation", r.Kind));
        Assert.All(resp.Results, r => Assert.Equal("private", r.Tenant));
    }
    finally
    {
        await MemoryStoreSchemaTests.DropDb(dbName);
    }
}

[Fact]
public async Task HandleSearchAsync_TenantIsolation_PrivateNeverLeaksWhenQueryingPublic()
{
    if (!ArangoEnabled) return;
    var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
    try
    {
        using var http = new HttpClient();
        var emb = new MemoryStoreNotesTests.StubEmbeddingClient(); // returns identical embedding for any text
        var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
            "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);
        await store.UpsertPostAsync(MemoryStorePostsTests.MakePost(slug: "publicPost"), force: false);
        await store.UpsertNoteAsync(MemoryStoreNotesTests.MakeNote(key: "obsidian://privateNote.md"));

        var req = new SearchRequest(
            Query: "anything",
            Kinds: new[] { "post", "observation" },
            K: 5,
            Tenant: null,
            Tenants: new[] { "public" });
        var resp = await ContentRagEndpoints.HandleSearchAsync(req, store, emb);

        Assert.NotEmpty(resp.Results);
        Assert.All(resp.Results, r => Assert.Equal("public", r.Tenant));
        Assert.DoesNotContain(resp.Results, r => r.Kind == "observation");
    }
    finally
    {
        await MemoryStoreSchemaTests.DropDb(dbName);
    }
}

[Fact]
public async Task HandleSearchAsync_UnionAcrossKinds_RanksByScore()
{
    if (!ArangoEnabled) return;
    var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
    try
    {
        using var http = new HttpClient();
        var emb = new MemoryStoreNotesTests.StubEmbeddingClient();
        var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
            "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);
        await store.UpsertPostAsync(MemoryStorePostsTests.MakePost(slug: "p1"), force: false);
        await store.UpsertNoteAsync(MemoryStoreNotesTests.MakeNote(key: "obsidian://n1.md"));

        var req = new SearchRequest(
            Query: "x",
            Kinds: new[] { "post", "observation" },
            K: 10,
            Tenant: null,
            Tenants: new[] { "public", "private" });
        var resp = await ContentRagEndpoints.HandleSearchAsync(req, store, emb);

        // Score ordering: each result's score >= the next.
        for (int i = 1; i < resp.Results.Count; i++)
            Assert.True(resp.Results[i - 1].Score >= resp.Results[i].Score);
        // Both kinds represented.
        Assert.Contains(resp.Results, r => r.Kind == "post");
        Assert.Contains(resp.Results, r => r.Kind == "observation");
    }
    finally
    {
        await MemoryStoreSchemaTests.DropDb(dbName);
    }
}
```

The test references `MemoryStoreNotesTests.StubEmbeddingClient` and `MemoryStoreNotesTests.MakeNote`. Make both `internal static` (or move them to a shared test-helper class) in Task 3's test file so they're accessible here.

- [ ] **Step 2: Run tests; expect compilation/runtime failures**

```bash
ARANGO_TEST_RUN=1 dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter FullyQualifiedName~HandleSearchAsync_
```

Expected: tests fail because `HandleSearchAsync` doesn't yet recognize `"observation"` as a kind, doesn't read `request.Tenants` (plural), and `SearchResult.Kind`/`Tenant` aren't populated.

- [ ] **Step 3: Update `HandleSearchAsync` in `ContentRagEndpoints.cs`**

Replace the body of `HandleSearchAsync` (currently around line 100) with:

```csharp
public static async Task<SearchResponse> HandleSearchAsync(
    SearchRequest request,
    MemoryStore store,
    IEmbeddingClient embeddings,
    CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(request.Query))
        throw new ArgumentException("query is required", nameof(request));

    var k = request.K <= 0 ? 5 : Math.Min(request.K, 50);

    // Tenants: prefer plural; fall back to singular for back-compat; default ["public"].
    IReadOnlyList<string> tenants;
    if (request.Tenants is { Count: > 0 })
        tenants = request.Tenants;
    else if (!string.IsNullOrWhiteSpace(request.Tenant))
        tenants = new[] { request.Tenant! };
    else
        tenants = new[] { "public" };

    var kindStrings = request.Kinds is { Count: > 0 } ? request.Kinds : new[] { "post" };
    var kinds = kindStrings.Select(s => s.ToLowerInvariant() switch
    {
        "post" => MemoryKind.Post,
        "observation" => MemoryKind.Observation,
        "fact" => MemoryKind.Fact,
        "decision" => MemoryKind.Decision,
        _ => throw new ArgumentException($"unknown kind: {s}", nameof(request))
    }).ToList();

    var embedSw = Stopwatch.StartNew();
    var queryVec = await embeddings.EmbedAsync(request.Query, ct);
    embedSw.Stop();

    var searchSw = Stopwatch.StartNew();
    var rows = await store.SearchAsync(queryVec, kinds, tenants, rawK: k * 2, ct);
    searchSw.Stop();

    // Per-row projection that handles both posts and notes.
    var topK = rows.OrderByDescending(r => r.Sim).Take(k).ToList();

    var results = topK.Select(r =>
    {
        var kindLower = r.Kind.ToLowerInvariant();
        if (kindLower == "post")
        {
            return new SearchResult(
                Slug: r.Slug,
                Collection: r.Collection,
                Title: r.Title,
                MatchedKind: r.VectorKind,
                Score: r.Sim,
                Snippet: BuildSnippet(r),
                Url: $"/{r.Collection}/{r.Slug}/",
                Kind: "post",
                Tenant: r.TenantId);
        }
        // Notes: Slug = note_key, Collection = "", Url = note_key (obsidian://...).
        return new SearchResult(
            Slug: r.Slug,
            Collection: string.Empty,
            Title: r.Title,
            MatchedKind: kindLower,
            Score: r.Sim,
            Snippet: BuildSnippet(r),
            Url: r.Slug,
            Kind: kindLower,
            Tenant: r.TenantId);
    }).ToList();

    return new SearchResponse(
        QueryEmbedMs: embedSw.ElapsedMilliseconds,
        SearchMs: searchSw.ElapsedMilliseconds,
        Results: results);
}
```

This assumes `PostSearchHit` (the row type returned by `MemoryStore.SearchAsync`) has at minimum: `Slug`, `Collection`, `Title`, `VectorKind`, `Sim`, `Text`, `AiSummary`, `TenantId`, `Kind`. If `Kind` and `TenantId` aren't already there, extend the record in `dais-bridge/Memory/Models/PostSearchHit.cs` to include them, and update the AQL `RETURN` projection in `MemoryStore.SearchAsync` to populate them (`kind: d.kind` for notes — which the new ingest already writes — and `tenant_id: d.tenant_id` from the existing field). For posts, `kind` should be populated as `"post"` (either at write-time in `UpsertOnePostVectorAsync` or as a literal in the AQL projection).

- [ ] **Step 4: Run tests; expect all 4 to pass**

```bash
ARANGO_TEST_RUN=1 dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter FullyQualifiedName~HandleSearchAsync_ 2>&1 | tail -10
```

Expected: 4/4 pass (5 if pre-existing `HandleSearchAsync_*` tests also pass after the projection update).

- [ ] **Step 5: Run the full integration suite to confirm no regressions**

```bash
ARANGO_TEST_RUN=1 dotnet test dais-bridge.tests/dais-bridge.tests.csproj 2>&1 | tail -5
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add dais-bridge/Endpoints/ContentRagEndpoints.cs dais-bridge/Memory/Models/PostSearchHit.cs dais-bridge/Memory/MemoryStore.cs dais-bridge.tests/Endpoints/ContentRagEndpointsTests.cs
git commit -m "feat(bridge): search supports notes kinds + tenant list"
```

(Only stage `PostSearchHit.cs` / `MemoryStore.cs` if you actually modified them in this task.)

---

## Task 6: `HandleIngestNotesAsync` + route (TDD)

**Files:**
- Modify: `/home/deovolente/repos/DarbeesChasingRainbows/dais-bridge.tests/Endpoints/ContentRagEndpointsTests.cs`
- Modify: `/home/deovolente/repos/DarbeesChasingRainbows/dais-bridge/Endpoints/ContentRagEndpoints.cs`
- Modify: `/home/deovolente/repos/DarbeesChasingRainbows/dais-bridge/Program.cs`

- [ ] **Step 1: Append the failing test**

Add this test to `ContentRagEndpointsTests.cs`:

```csharp
[Fact]
public async Task HandleIngestNotesAsync_RoundTrip_ReturnsCountsAndStaleDeletes()
{
    if (!ArangoEnabled) return;
    var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
    try
    {
        using var http = new HttpClient();
        var emb = new MemoryStoreNotesTests.StubEmbeddingClient();
        var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
            "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

        // Pre-seed an obsidian note that will become stale.
        await store.UpsertNoteAsync(MemoryStoreNotesTests.MakeNote(key: "obsidian://old.md"));

        var req = new IngestNotesRequest(
            Tenant: "private",
            Notes: new[]
            {
                new NoteRecord(
                    Key: "obsidian://new.md",
                    Kind: "observation",
                    Text: "fresh note",
                    Title: "new",
                    Metadata: null),
            },
            CurrentKeys: new[] { "obsidian://new.md" });
        var resp = await ContentRagEndpoints.HandleIngestNotesAsync(req, store);

        Assert.Equal(1, resp.EmbeddedCount);
        Assert.Equal(0, resp.CachedCount);
        Assert.Equal(0, resp.FailedCount);
        Assert.Equal(1, resp.StaleDeletedCount); // obsidian://old.md removed
        Assert.Equal("embedded", resp.PerNote.Single(p => p.Key == "obsidian://new.md").Outcome);
    }
    finally
    {
        await MemoryStoreSchemaTests.DropDb(dbName);
    }
}
```

- [ ] **Step 2: Run; expect compile error (no `HandleIngestNotesAsync`)**

```bash
ARANGO_TEST_RUN=1 dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter FullyQualifiedName~HandleIngestNotesAsync
```

Expected: compilation error.

- [ ] **Step 3: Add `HandleIngestNotesAsync` to `ContentRagEndpoints.cs`**

After `HandleSearchAsync`, add:

```csharp
public static async Task<IngestNotesResponse> HandleIngestNotesAsync(
    IngestNotesRequest request,
    MemoryStore store,
    CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(request.Tenant))
        throw new ArgumentException("tenant is required", nameof(request));

    var sw = Stopwatch.StartNew();

    int embedded = 0, cached = 0, failed = 0;
    var perNote = new List<IngestNoteResult>();

    foreach (var n in request.Notes)
    {
        MemoryKind kind;
        try
        {
            kind = n.Kind.ToLowerInvariant() switch
            {
                "observation" => MemoryKind.Observation,
                "fact" => MemoryKind.Fact,
                "decision" => MemoryKind.Decision,
                _ => throw new ArgumentException($"unsupported kind for note: {n.Kind}")
            };
        }
        catch (Exception ex)
        {
            failed++;
            perNote.Add(new IngestNoteResult(n.Key, "failed", ex.Message));
            continue;
        }

        var doc = new NoteDocument(
            Key: n.Key,
            Title: n.Title,
            Text: n.Text,
            Kind: kind,
            TenantId: request.Tenant,
            Metadata: n.Metadata);

        UpsertNoteResult r;
        try
        {
            r = await store.UpsertNoteAsync(doc, ct);
        }
        catch (Exception ex)
        {
            failed++;
            perNote.Add(new IngestNoteResult(n.Key, "failed", ex.Message));
            continue;
        }

        if (r.Outcome == VectorWriteOutcome.Embedded)
        {
            embedded++;
            perNote.Add(new IngestNoteResult(n.Key, "embedded", null));
        }
        else if (r.Outcome == VectorWriteOutcome.Cached)
        {
            cached++;
            perNote.Add(new IngestNoteResult(n.Key, "cached", null));
        }
        else
        {
            failed++;
            perNote.Add(new IngestNoteResult(n.Key, "failed", r.Reason));
        }
    }

    var staleDeleted = await store.DeleteStaleNotesAsync(request.CurrentKeys, request.Tenant, ct);

    sw.Stop();
    return new IngestNotesResponse(
        EmbeddedCount: embedded,
        CachedCount: cached,
        FailedCount: failed,
        StaleDeletedCount: staleDeleted,
        DurationMs: sw.ElapsedMilliseconds,
        PerNote: perNote);
}
```

- [ ] **Step 4: Wire the route in `Program.cs`**

Find the existing `app.MapPost("/api/memory/search", ...)` registration (around line 170 in `Program.cs`). Add a new registration immediately after it:

```csharp
app.MapPost("/api/memory/ingest-notes", async (
    IngestNotesRequest request,
    MemoryStore store,
    CancellationToken ct) =>
{
    try
    {
        var resp = await ContentRagEndpoints.HandleIngestNotesAsync(request, store, ct);
        return Results.Ok(resp);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = "invalid_request", message = ex.Message });
    }
});
```

- [ ] **Step 5: Build + run the new test**

```bash
dotnet build dais-bridge/dais-bridge.csproj
ARANGO_TEST_RUN=1 dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter FullyQualifiedName~HandleIngestNotesAsync 2>&1 | tail -5
```

Expected: 1/1 pass.

- [ ] **Step 6: Run the full integration suite once more**

```bash
ARANGO_TEST_RUN=1 dotnet test dais-bridge.tests/dais-bridge.tests.csproj 2>&1 | tail -5
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

```bash
git add dais-bridge/Endpoints/ContentRagEndpoints.cs dais-bridge/Program.cs dais-bridge.tests/Endpoints/ContentRagEndpointsTests.cs
git commit -m "feat(bridge): POST /api/memory/ingest-notes with stale-delete"
```

---

## Task 7: Plugin scaffold

**Files:**
- Create: `/home/deovolente/repos/DarbeesChasingRainbows/obsidian-plugin/package.json`
- Create: `/home/deovolente/repos/DarbeesChasingRainbows/obsidian-plugin/tsconfig.json`
- Create: `/home/deovolente/repos/DarbeesChasingRainbows/obsidian-plugin/esbuild.config.mjs`
- Create: `/home/deovolente/repos/DarbeesChasingRainbows/obsidian-plugin/manifest.json`
- Create: `/home/deovolente/repos/DarbeesChasingRainbows/obsidian-plugin/versions.json`
- Create: `/home/deovolente/repos/DarbeesChasingRainbows/obsidian-plugin/README.md`
- Modify: `/home/deovolente/repos/DarbeesChasingRainbows/.gitignore`

- [ ] **Step 1: Create `obsidian-plugin/package.json`**

```json
{
	"name": "darbee-memory",
	"version": "0.1.0",
	"description": "Darbee memory ingest + sidebar search for Obsidian.",
	"main": "main.js",
	"type": "module",
	"scripts": {
		"build": "node esbuild.config.mjs",
		"dev": "node esbuild.config.mjs --watch",
		"test": "vitest run",
		"test:watch": "vitest"
	},
	"devDependencies": {
		"@types/node": "^22.0.0",
		"esbuild": "^0.23.0",
		"obsidian": "^1.7.2",
		"typescript": "^5.5.0",
		"vitest": "^3.0.0"
	},
	"keywords": ["obsidian"],
	"license": "MIT"
}
```

- [ ] **Step 2: Create `obsidian-plugin/tsconfig.json`**

```json
{
	"compilerOptions": {
		"target": "ES2022",
		"module": "ESNext",
		"moduleResolution": "Bundler",
		"strict": true,
		"esModuleInterop": true,
		"skipLibCheck": true,
		"resolveJsonModule": true,
		"isolatedModules": true,
		"noEmit": true,
		"lib": ["ES2022", "DOM"]
	},
	"include": ["src/**/*.ts", "test/**/*.ts"]
}
```

- [ ] **Step 3: Create `obsidian-plugin/esbuild.config.mjs`**

```js
import esbuild from 'esbuild';
import { mkdir, copyFile, readFile, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const root = __dirname;
const dist = join(root, 'dist');

const watch = process.argv.includes('--watch');

await mkdir(dist, { recursive: true });

const context = await esbuild.context({
	entryPoints: [join(root, 'src/main.ts')],
	bundle: true,
	format: 'cjs',
	platform: 'browser',
	external: ['obsidian', 'electron'],
	outfile: join(dist, 'main.js'),
	target: 'es2022',
	sourcemap: 'inline',
	logLevel: 'info',
});

// Copy manifest and versions into dist.
await copyFile(join(root, 'manifest.json'), join(dist, 'manifest.json'));
await copyFile(join(root, 'versions.json'), join(dist, 'versions.json'));

if (watch) {
	await context.watch();
} else {
	await context.rebuild();
	await context.dispose();
}
```

- [ ] **Step 4: Create `obsidian-plugin/manifest.json`**

```json
{
	"id": "darbee-memory",
	"name": "Darbee Memory",
	"version": "0.1.0",
	"minAppVersion": "1.5.0",
	"description": "Ingest flagged Obsidian notes into the Darbee memory bridge and query memory from the sidebar.",
	"author": "Darbees",
	"isDesktopOnly": true
}
```

- [ ] **Step 5: Create `obsidian-plugin/versions.json`**

```json
{
	"0.1.0": "1.5.0"
}
```

- [ ] **Step 6: Create `obsidian-plugin/README.md`**

```markdown
# Darbee Memory (Obsidian plugin)

Live-sync Obsidian notes flagged `memory: true` into the Darbee memory bridge,
and search across published posts + private notes from the sidebar.

## Install (one-time)

```bash
cd obsidian-plugin
npm install
npm run build
cd ..
npm run obsidian:link
```

Then enable "Darbee Memory" in **Obsidian → Community Plugins**.

## Develop

```bash
npm run obsidian:dev   # watch-mode esbuild; pair with Obsidian "Hot Reload" plugin
```

## Frontmatter contract

```yaml
---
memory: true
memory_kind: observation       # observation | fact | decision (default: observation)
memory_tenant: private         # default: from plugin settings
---
```

## Uninstall

Disable in Obsidian, optionally `npm run obsidian:unlink`. Memory rows linger
in Arango until the plugin's next save sends an empty `currentKeys`.
```

- [ ] **Step 7: Append to `.gitignore`**

```
# Obsidian plugin build artifacts
obsidian-plugin/node_modules/
obsidian-plugin/dist/
```

Add at the bottom of the existing `.gitignore`.

- [ ] **Step 8: Install + verify the scaffold builds**

```bash
cd /home/deovolente/repos/DarbeesChasingRainbows/obsidian-plugin
npm install
mkdir -p src && echo "console.log('darbee-memory placeholder')" > src/main.ts
npm run build
ls dist/
```

Expected: `dist/main.js`, `dist/manifest.json`, `dist/versions.json` present. The placeholder source will be replaced in Task 8.

- [ ] **Step 9: Commit**

```bash
cd /home/deovolente/repos/DarbeesChasingRainbows
git add obsidian-plugin/package.json obsidian-plugin/package-lock.json obsidian-plugin/tsconfig.json obsidian-plugin/esbuild.config.mjs obsidian-plugin/manifest.json obsidian-plugin/versions.json obsidian-plugin/README.md obsidian-plugin/src/main.ts .gitignore
git commit -m "feat(obsidian): plugin scaffold (package, esbuild, manifest, README)"
```

---

## Task 8: Plugin `types.ts` + `ingester.ts` with Vitest (TDD)

**Files:**
- Create: `/home/deovolente/repos/DarbeesChasingRainbows/obsidian-plugin/src/types.ts`
- Create: `/home/deovolente/repos/DarbeesChasingRainbows/obsidian-plugin/src/ingester.ts`
- Create: `/home/deovolente/repos/DarbeesChasingRainbows/obsidian-plugin/test/ingester.test.ts`

- [ ] **Step 1: Write the failing test file `test/ingester.test.ts`**

```ts
import { describe, it, expect, vi } from 'vitest';
import {
	parseNoteFrontmatter,
	deriveNoteKey,
	stripMdx,
	buildIngestPayload,
	scopeToFilters,
} from '../src/ingester';

const SETTINGS = {
	bridgeUrl: 'http://localhost:5000',
	defaultTenant: 'private',
	defaultKind: 'observation' as const,
	debounceMs: 2000,
	defaultScope: 'both' as const,
};

describe('parseNoteFrontmatter', () => {
	it('returns shouldIngest=true with fact kind when frontmatter requests fact', () => {
		const res = parseNoteFrontmatter(
			'---\nmemory: true\nmemory_kind: fact\n---\nbody',
			SETTINGS,
		);
		expect(res.shouldIngest).toBe(true);
		expect(res.kind).toBe('fact');
		expect(res.tenant).toBe('private');
	});

	it('returns shouldIngest=false when memory flag is missing', () => {
		const res = parseNoteFrontmatter('---\ntitle: foo\n---\nbody', SETTINGS);
		expect(res.shouldIngest).toBe(false);
	});

	it('falls back to defaultKind on unknown memory_kind and warns', () => {
		const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
		const res = parseNoteFrontmatter(
			'---\nmemory: true\nmemory_kind: foobar\n---\nbody',
			SETTINGS,
		);
		expect(res.kind).toBe('observation');
		expect(warn).toHaveBeenCalled();
		warn.mockRestore();
	});
});

describe('deriveNoteKey', () => {
	it('produces obsidian:// prefix and preserves the path', () => {
		expect(deriveNoteKey('daily/2026-05-18.md')).toBe('obsidian://daily/2026-05-18.md');
	});
});

describe('stripMdx', () => {
	it('removes import lines, jsx, markdown punctuation, and collapses whitespace', () => {
		const input = `import Callout from '../Callout.astro';
# Heading
Some **bold** text with a [link](https://example.com).
<Callout>Hi</Callout>`;
		const out = stripMdx(input);
		expect(out).not.toContain('import');
		expect(out).not.toContain('<Callout');
		expect(out).not.toContain('**');
		expect(out).toContain('Some bold text with a link');
	});
});

describe('buildIngestPayload', () => {
	it('groups queued notes and includes currentKeys; drops empty bodies', () => {
		const payload = buildIngestPayload({
			tenant: 'private',
			queued: [
				{ key: 'obsidian://a.md', title: 'A', kind: 'observation', body: 'hello' },
				{ key: 'obsidian://b.md', title: 'B', kind: 'fact', body: '   ' },
			],
			currentKeys: ['obsidian://a.md', 'obsidian://b.md'],
		});
		expect(payload.notes).toHaveLength(1);
		expect(payload.notes[0].key).toBe('obsidian://a.md');
		expect(payload.currentKeys).toContain('obsidian://b.md');
	});
});

describe('scopeToFilters', () => {
	it('maps private to observation/fact/decision kinds with private tenant', () => {
		expect(scopeToFilters('private')).toEqual({
			kinds: ['observation', 'fact', 'decision'],
			tenants: ['private'],
		});
	});

	it('maps both to 4 kinds and 2 tenants', () => {
		expect(scopeToFilters('both')).toEqual({
			kinds: ['post', 'observation', 'fact', 'decision'],
			tenants: ['public', 'private'],
		});
	});

	it('maps posts to post kind with public tenant', () => {
		expect(scopeToFilters('posts')).toEqual({
			kinds: ['post'],
			tenants: ['public'],
		});
	});
});
```

- [ ] **Step 2: Run; expect compile/import failures**

```bash
cd obsidian-plugin
npm test 2>&1 | tail -15
```

Expected: `Failed to resolve import "../src/ingester"`.

- [ ] **Step 3: Create `src/types.ts`**

```ts
export type MemoryKind = 'observation' | 'fact' | 'decision';
export type SearchKind = 'post' | MemoryKind;
export type Scope = 'posts' | 'private' | 'both';

export interface Settings {
	bridgeUrl: string;
	defaultTenant: string;
	defaultKind: MemoryKind;
	debounceMs: number;
	defaultScope: Scope;
}

export interface NoteRecord {
	key: string;
	kind: MemoryKind;
	text: string;
	title: string;
	metadata?: Record<string, unknown>;
}

export interface IngestPayload {
	tenant: string;
	notes: NoteRecord[];
	currentKeys: string[];
}

export interface SearchHit {
	slug: string;
	collection: string;
	title: string;
	matchedKind: string;
	score: number;
	snippet: string;
	url: string;
	kind: string;
	tenant: string;
}

export interface SearchResponse {
	queryEmbedMs: number;
	searchMs: number;
	results: SearchHit[];
}

export interface QueuedNote {
	key: string;
	title: string;
	kind: MemoryKind;
	body: string;
}
```

- [ ] **Step 4: Create `src/ingester.ts`**

```ts
import type { MemoryKind, Scope, Settings, IngestPayload, QueuedNote } from './types';

const KIND_VALUES: ReadonlyArray<MemoryKind> = ['observation', 'fact', 'decision'];

export interface ParseResult {
	shouldIngest: boolean;
	kind: MemoryKind;
	tenant: string;
}

export function parseNoteFrontmatter(raw: string, settings: Settings): ParseResult {
	const match = raw.match(/^---\n([\s\S]*?)\n---/);
	if (!match) return { shouldIngest: false, kind: settings.defaultKind, tenant: settings.defaultTenant };
	const frontmatter = match[1];
	const flag = /^memory:\s*true\s*$/m.test(frontmatter);
	if (!flag) return { shouldIngest: false, kind: settings.defaultKind, tenant: settings.defaultTenant };

	const kindMatch = frontmatter.match(/^memory_kind:\s*(\S+)\s*$/m);
	let kind = settings.defaultKind;
	if (kindMatch) {
		const candidate = kindMatch[1].toLowerCase() as MemoryKind;
		if (KIND_VALUES.includes(candidate)) {
			kind = candidate;
		} else {
			console.warn(`[darbee-memory] unknown memory_kind="${kindMatch[1]}" — falling back to ${settings.defaultKind}`);
		}
	}

	const tenantMatch = frontmatter.match(/^memory_tenant:\s*(\S+)\s*$/m);
	const tenant = tenantMatch ? tenantMatch[1] : settings.defaultTenant;

	return { shouldIngest: true, kind, tenant };
}

export function deriveNoteKey(vaultRelativePath: string): string {
	return `obsidian://${vaultRelativePath}`;
}

export function stripMdx(body: string): string {
	return body
		.replace(/^import\s.+$/gm, '')
		.replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
		.replace(/<[^>]+>/g, '')
		.replace(/[#*_`>|~-]/g, ' ')
		.replace(/\s+/g, ' ')
		.trim();
}

export function bodyFromRaw(raw: string): string {
	const match = raw.match(/^---\n[\s\S]*?\n---\n?/);
	const body = match ? raw.slice(match[0].length) : raw;
	return stripMdx(body);
}

export interface BuildPayloadInput {
	tenant: string;
	queued: QueuedNote[];
	currentKeys: string[];
}

export function buildIngestPayload(input: BuildPayloadInput): IngestPayload {
	const notes = input.queued
		.filter((n) => n.body.trim().length > 0)
		.map((n) => ({
			key: n.key,
			kind: n.kind,
			text: n.body,
			title: n.title,
			metadata: { source: 'obsidian' },
		}));
	return { tenant: input.tenant, notes, currentKeys: input.currentKeys };
}

export interface ScopeFilters {
	kinds: string[];
	tenants: string[];
}

export function scopeToFilters(scope: Scope): ScopeFilters {
	switch (scope) {
		case 'posts':
			return { kinds: ['post'], tenants: ['public'] };
		case 'private':
			return { kinds: ['observation', 'fact', 'decision'], tenants: ['private'] };
		case 'both':
			return { kinds: ['post', 'observation', 'fact', 'decision'], tenants: ['public', 'private'] };
	}
}
```

- [ ] **Step 5: Run tests; expect all to pass**

```bash
cd obsidian-plugin
npm test 2>&1 | tail -10
```

Expected: 8/8 pass.

- [ ] **Step 6: Commit**

```bash
cd /home/deovolente/repos/DarbeesChasingRainbows
git add obsidian-plugin/src/types.ts obsidian-plugin/src/ingester.ts obsidian-plugin/test/ingester.test.ts
git commit -m "feat(obsidian): pure ingester (frontmatter parse, stripMdx, scope filters) + tests"
```

---

## Task 9: Plugin `bridge-client.ts` (TDD)

**Files:**
- Create: `/home/deovolente/repos/DarbeesChasingRainbows/obsidian-plugin/src/bridge-client.ts`
- Create: `/home/deovolente/repos/DarbeesChasingRainbows/obsidian-plugin/test/bridge-client.test.ts`

- [ ] **Step 1: Write failing test `test/bridge-client.test.ts`**

```ts
import { describe, it, expect, vi, afterEach } from 'vitest';
import { ingestNotes, searchMemory, BridgeError } from '../src/bridge-client';

const obsidianMock = vi.hoisted(() => ({ requestUrl: vi.fn() }));
vi.mock('obsidian', () => obsidianMock);

afterEach(() => {
	obsidianMock.requestUrl.mockReset();
});

describe('ingestNotes', () => {
	it('returns parsed response on 2xx', async () => {
		obsidianMock.requestUrl.mockResolvedValue({
			status: 200,
			json: { embeddedCount: 1, cachedCount: 0, failedCount: 0, staleDeletedCount: 0, durationMs: 12, perNote: [] },
			text: '',
		});
		const out = await ingestNotes('http://localhost:5000', {
			tenant: 'private',
			notes: [],
			currentKeys: [],
		});
		expect(out.embeddedCount).toBe(1);
	});

	it('throws BridgeError with parsed body on non-2xx', async () => {
		obsidianMock.requestUrl.mockResolvedValue({
			status: 400,
			json: { error: 'invalid_request', message: 'bad' },
			text: '',
		});
		await expect(
			ingestNotes('http://localhost:5000', { tenant: 'private', notes: [], currentKeys: [] }),
		).rejects.toBeInstanceOf(BridgeError);
	});
});

describe('searchMemory', () => {
	it('passes kinds and tenants in the request body', async () => {
		obsidianMock.requestUrl.mockResolvedValue({
			status: 200,
			json: { queryEmbedMs: 50, searchMs: 10, results: [] },
			text: '',
		});
		await searchMemory('http://localhost:5000', {
			query: 'hello',
			k: 5,
			kinds: ['post', 'observation'],
			tenants: ['public', 'private'],
		});
		const call = obsidianMock.requestUrl.mock.calls[0][0];
		const body = JSON.parse(call.body);
		expect(body.kinds).toEqual(['post', 'observation']);
		expect(body.tenants).toEqual(['public', 'private']);
	});
});
```

- [ ] **Step 2: Run; expect import failure**

```bash
cd obsidian-plugin
npm test 2>&1 | tail -10
```

Expected: `Failed to resolve import "../src/bridge-client"`.

- [ ] **Step 3: Create `src/bridge-client.ts`**

```ts
import { requestUrl } from 'obsidian';
import type { IngestPayload, SearchResponse } from './types';

const DEFAULT_TIMEOUT_MS = 30_000;

export class BridgeError extends Error {
	status?: number;
	body?: unknown;

	constructor(message: string, opts: { status?: number; body?: unknown } = {}) {
		super(message);
		this.name = 'BridgeError';
		this.status = opts.status;
		this.body = opts.body;
	}
}

interface IngestNotesResponse {
	embeddedCount: number;
	cachedCount: number;
	failedCount: number;
	staleDeletedCount: number;
	durationMs: number;
	perNote: Array<{ key: string; outcome: string; reason?: string }>;
}

async function postJson<T>(url: string, body: unknown, timeoutMs = DEFAULT_TIMEOUT_MS): Promise<T> {
	let response;
	try {
		response = await Promise.race([
			requestUrl({
				url,
				method: 'POST',
				headers: { 'content-type': 'application/json' },
				body: JSON.stringify(body),
				throw: false,
			}),
			new Promise<never>((_, reject) =>
				setTimeout(() => reject(new BridgeError(`bridge timeout after ${timeoutMs}ms: ${url}`)), timeoutMs),
			),
		]);
	} catch (err) {
		if (err instanceof BridgeError) throw err;
		throw new BridgeError(`bridge unreachable at ${url}: ${(err as Error).message}`);
	}

	if (response.status < 200 || response.status >= 300) {
		throw new BridgeError(`bridge ${response.status}`, {
			status: response.status,
			body: (response as { json?: unknown }).json ?? response.text,
		});
	}

	return ((response as { json?: unknown }).json ?? JSON.parse(response.text)) as T;
}

export async function ingestNotes(
	baseUrl: string,
	payload: IngestPayload,
	timeoutMs?: number,
): Promise<IngestNotesResponse> {
	return postJson<IngestNotesResponse>(
		`${baseUrl.replace(/\/$/, '')}/api/memory/ingest-notes`,
		payload,
		timeoutMs,
	);
}

export interface SearchPayload {
	query: string;
	k: number;
	kinds: string[];
	tenants: string[];
}

export async function searchMemory(
	baseUrl: string,
	payload: SearchPayload,
	timeoutMs?: number,
): Promise<SearchResponse> {
	return postJson<SearchResponse>(
		`${baseUrl.replace(/\/$/, '')}/api/memory/search`,
		payload,
		timeoutMs,
	);
}
```

- [ ] **Step 4: Run tests; expect all to pass**

```bash
cd obsidian-plugin
npm test 2>&1 | tail -10
```

Expected: 3/3 new tests pass (alongside the 8 from Task 8 → 11 total).

- [ ] **Step 5: Commit**

```bash
cd /home/deovolente/repos/DarbeesChasingRainbows
git add obsidian-plugin/src/bridge-client.ts obsidian-plugin/test/bridge-client.test.ts
git commit -m "feat(obsidian): bridge-client wrappers with timeout + BridgeError"
```

---

## Task 10: Plugin `settings.ts`

**Files:**
- Create: `/home/deovolente/repos/DarbeesChasingRainbows/obsidian-plugin/src/settings.ts`

- [ ] **Step 1: Create `src/settings.ts`**

```ts
import { App, PluginSettingTab, Setting } from 'obsidian';
import type DarbeeMemoryPlugin from './main';
import type { MemoryKind, Scope, Settings } from './types';

export const DEFAULT_SETTINGS: Settings = {
	bridgeUrl: 'http://localhost:5000',
	defaultTenant: 'private',
	defaultKind: 'observation',
	debounceMs: 2000,
	defaultScope: 'both',
};

const KINDS: ReadonlyArray<MemoryKind> = ['observation', 'fact', 'decision'];
const SCOPES: ReadonlyArray<Scope> = ['posts', 'private', 'both'];

export class DarbeeMemorySettingTab extends PluginSettingTab {
	plugin: DarbeeMemoryPlugin;

	constructor(app: App, plugin: DarbeeMemoryPlugin) {
		super(app, plugin);
		this.plugin = plugin;
	}

	display(): void {
		const { containerEl } = this;
		containerEl.empty();

		new Setting(containerEl)
			.setName('Bridge URL')
			.setDesc('Base URL of the DAIS bridge (localhost expected).')
			.addText((text) =>
				text
					.setValue(this.plugin.settings.bridgeUrl)
					.onChange(async (value) => {
						try {
							new URL(value); // validation
							this.plugin.settings.bridgeUrl = value;
							await this.plugin.saveSettings();
						} catch {
							/* keep previous value on invalid URL */
						}
					}),
			);

		new Setting(containerEl)
			.setName('Default tenant')
			.setDesc('Tenant id applied when a note has no `memory_tenant` frontmatter.')
			.addText((text) =>
				text
					.setValue(this.plugin.settings.defaultTenant)
					.onChange(async (value) => {
						this.plugin.settings.defaultTenant = value || 'private';
						await this.plugin.saveSettings();
					}),
			);

		new Setting(containerEl)
			.setName('Default kind')
			.setDesc('Used when a note has no `memory_kind` frontmatter.')
			.addDropdown((dd) => {
				for (const k of KINDS) dd.addOption(k, k);
				dd.setValue(this.plugin.settings.defaultKind).onChange(async (value) => {
					this.plugin.settings.defaultKind = value as MemoryKind;
					await this.plugin.saveSettings();
				});
			});

		new Setting(containerEl)
			.setName('Debounce (ms)')
			.setDesc('How long to wait after a save before flushing the batch.')
			.addText((text) =>
				text
					.setValue(String(this.plugin.settings.debounceMs))
					.onChange(async (value) => {
						const n = Number(value);
						if (Number.isFinite(n) && n >= 0) {
							this.plugin.settings.debounceMs = n;
							await this.plugin.saveSettings();
						}
					}),
			);

		new Setting(containerEl)
			.setName('Sidebar default scope')
			.addDropdown((dd) => {
				for (const s of SCOPES) dd.addOption(s, s);
				dd.setValue(this.plugin.settings.defaultScope).onChange(async (value) => {
					this.plugin.settings.defaultScope = value as Scope;
					await this.plugin.saveSettings();
				});
			});
	}
}
```

- [ ] **Step 2: Build to confirm types**

```bash
cd obsidian-plugin
npx tsc --noEmit
```

Expected: zero errors (the unused `DarbeeMemoryPlugin` import will resolve once Task 11 creates main.ts; if the strict-mode check trips on the import-not-yet-used, leave it — the next task fixes it).

If `npx tsc` fails because `main.ts` doesn't exist, that's expected. Proceed to commit anyway.

- [ ] **Step 3: Commit**

```bash
cd /home/deovolente/repos/DarbeesChasingRainbows
git add obsidian-plugin/src/settings.ts
git commit -m "feat(obsidian): settings tab with bridge URL, tenant, kind, debounce, scope"
```

---

## Task 11: Plugin `sidebar-view.ts`

**Files:**
- Create: `/home/deovolente/repos/DarbeesChasingRainbows/obsidian-plugin/src/sidebar-view.ts`

- [ ] **Step 1: Create `src/sidebar-view.ts`**

```ts
import { App, ItemView, Notice, WorkspaceLeaf } from 'obsidian';
import type DarbeeMemoryPlugin from './main';
import { scopeToFilters } from './ingester';
import { searchMemory } from './bridge-client';
import type { Scope, SearchHit } from './types';

export const VIEW_TYPE_SIDEBAR = 'darbee-memory-sidebar';

export class DarbeeMemorySidebar extends ItemView {
	plugin: DarbeeMemoryPlugin;
	scope: Scope;
	controller: AbortController | null = null;
	queryInput!: HTMLInputElement;
	statusEl!: HTMLElement;
	resultsEl!: HTMLElement;

	constructor(leaf: WorkspaceLeaf, plugin: DarbeeMemoryPlugin) {
		super(leaf);
		this.plugin = plugin;
		this.scope = plugin.settings.defaultScope;
	}

	getViewType() {
		return VIEW_TYPE_SIDEBAR;
	}

	getDisplayText() {
		return 'Darbee Memory';
	}

	async onOpen() {
		const container = this.containerEl.children[1];
		container.empty();
		container.createEl('h3', { text: 'Darbee Memory' });

		const form = container.createEl('form');
		form.style.display = 'flex';
		form.style.flexDirection = 'column';
		form.style.gap = '6px';
		form.style.marginBottom = '8px';

		this.queryInput = form.createEl('input', {
			type: 'text',
			placeholder: 'Search posts and notes…',
		}) as HTMLInputElement;

		const toggleEl = form.createEl('div');
		toggleEl.style.display = 'flex';
		toggleEl.style.gap = '4px';
		const scopes: Scope[] = ['posts', 'private', 'both'];
		const buttons: Record<Scope, HTMLButtonElement> = {} as Record<Scope, HTMLButtonElement>;
		for (const s of scopes) {
			const btn = toggleEl.createEl('button', { text: s, type: 'button' }) as HTMLButtonElement;
			btn.onclick = () => {
				this.scope = s;
				for (const other of scopes) buttons[other].removeClass('mod-cta');
				btn.addClass('mod-cta');
			};
			buttons[s] = btn;
		}
		buttons[this.scope].addClass('mod-cta');

		const submit = form.createEl('button', { text: 'Search', type: 'submit' });
		this.statusEl = container.createEl('p', { text: '' });
		this.statusEl.style.minHeight = '1.4em';
		this.statusEl.setAttr('role', 'status');
		this.resultsEl = container.createEl('div');

		form.onsubmit = async (e) => {
			e.preventDefault();
			submit.setAttr('disabled', 'true');
			try {
				await this.runQuery();
			} finally {
				submit.removeAttribute('disabled');
			}
		};
	}

	async onClose() {
		this.controller?.abort();
	}

	private async runQuery() {
		const query = this.queryInput.value.trim();
		if (!query) return;

		this.controller?.abort();
		this.controller = new AbortController();
		const { kinds, tenants } = scopeToFilters(this.scope);

		this.statusEl.setText('Searching…');
		this.resultsEl.empty();

		try {
			const resp = await searchMemory(this.plugin.settings.bridgeUrl, {
				query,
				k: 10,
				kinds,
				tenants,
			});
			if (resp.results.length === 0) {
				this.statusEl.setText('No results.');
				return;
			}
			this.statusEl.setText(
				`${resp.results.length} result${resp.results.length === 1 ? '' : 's'} (embed ${resp.queryEmbedMs}ms · search ${resp.searchMs}ms)`,
			);
			this.renderResults(resp.results);
		} catch (err) {
			this.statusEl.setText(`Error: ${(err as Error).message}`);
		}
	}

	private renderResults(results: SearchHit[]) {
		for (const r of results) {
			const card = this.resultsEl.createEl('div');
			card.style.border = '1px solid var(--background-modifier-border)';
			card.style.borderRadius = '4px';
			card.style.padding = '6px';
			card.style.marginBottom = '6px';
			card.style.cursor = 'pointer';

			const header = card.createEl('div');
			header.style.display = 'flex';
			header.style.justifyContent = 'space-between';
			header.style.gap = '6px';

			header.createEl('strong', { text: r.title || r.slug });
			const badge = header.createEl('span', { text: `${r.score.toFixed(3)} · ${r.kind}/${r.tenant}` });
			badge.style.fontSize = '11px';
			badge.style.opacity = '0.7';

			const snippet = card.createEl('p', { text: r.snippet });
			snippet.style.fontSize = '12px';
			snippet.style.margin = '4px 0';
			snippet.style.opacity = '0.85';

			card.onclick = () => {
				if (r.kind === 'post') {
					window.open(`${this.plugin.settings.bridgeUrl.replace(/:\d+$/, '')}${r.url}`, '_blank');
				} else {
					this.app.workspace.openLinkText(r.slug.replace(/^obsidian:\/\//, ''), '');
				}
			};
		}
	}
}
```

- [ ] **Step 2: Commit**

```bash
cd /home/deovolente/repos/DarbeesChasingRainbows
git add obsidian-plugin/src/sidebar-view.ts
git commit -m "feat(obsidian): sidebar view with scope toggle and result cards"
```

---

## Task 12: Plugin `main.ts` wire-up + on-save debounce (with integration tests)

**Files:**
- Create: `/home/deovolente/repos/DarbeesChasingRainbows/obsidian-plugin/src/main.ts`
- Create: `/home/deovolente/repos/DarbeesChasingRainbows/obsidian-plugin/test/obsidian-stub.ts`
- Create: `/home/deovolente/repos/DarbeesChasingRainbows/obsidian-plugin/test/main.test.ts`

- [ ] **Step 1: Create the Obsidian test stub `test/obsidian-stub.ts`**

```ts
import { vi } from 'vitest';

// A minimal in-memory Vault that the plugin's debounce logic can drive.
export interface StubFile {
	path: string;
	content: string;
}

export function makeVault(initial: StubFile[]) {
	const files = new Map<string, StubFile>(initial.map((f) => [f.path, f]));
	const listeners: Record<string, ((file: StubFile) => void)[]> = { modify: [] };
	return {
		getMarkdownFiles: vi.fn(() => Array.from(files.values()).map((f) => ({ path: f.path }))),
		read: vi.fn(async (file: { path: string }) => files.get(file.path)?.content ?? ''),
		on: vi.fn((event: string, cb: (file: StubFile) => void) => {
			(listeners[event] ??= []).push(cb);
		}),
		simulateModify(file: StubFile) {
			files.set(file.path, file);
			for (const cb of listeners.modify ?? []) cb(file);
		},
		files,
	};
}
```

- [ ] **Step 2: Write failing `test/main.test.ts`**

```ts
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { makeVault } from './obsidian-stub';
import { runDebounceFlush, DarbeeMemoryRuntime } from '../src/main';

const obsidianMock = vi.hoisted(() => ({
	Plugin: class {},
	ItemView: class {},
	PluginSettingTab: class {},
	Setting: class {
		setName() { return this; }
		setDesc() { return this; }
		addText() { return this; }
		addDropdown() { return this; }
	},
	Notice: vi.fn(),
	requestUrl: vi.fn(),
}));
vi.mock('obsidian', () => obsidianMock);

beforeEach(() => {
	vi.useFakeTimers();
	obsidianMock.requestUrl.mockResolvedValue({
		status: 200,
		json: { embeddedCount: 1, cachedCount: 0, failedCount: 0, staleDeletedCount: 0, durationMs: 1, perNote: [] },
		text: '',
	});
});

afterEach(() => {
	vi.useRealTimers();
	obsidianMock.requestUrl.mockReset();
});

describe('runtime debounce', () => {
	it('coalesces 3 saves within window into one ingest call', async () => {
		const vault = makeVault([
			{ path: 'a.md', content: '---\nmemory: true\n---\nhello a' },
			{ path: 'b.md', content: '---\nmemory: true\n---\nhello b' },
			{ path: 'c.md', content: '---\nmemory: true\n---\nhello c' },
		]);
		const runtime = new DarbeeMemoryRuntime(
			{ bridgeUrl: 'http://localhost:5000', defaultTenant: 'private', defaultKind: 'observation', debounceMs: 2000, defaultScope: 'both' },
			vault as any,
		);

		runtime.handleModify({ path: 'a.md' });
		runtime.handleModify({ path: 'b.md' });
		runtime.handleModify({ path: 'c.md' });

		await vi.advanceTimersByTimeAsync(2100);
		await runDebounceFlush(runtime); // resolve any pending flush awaitables
		expect(obsidianMock.requestUrl).toHaveBeenCalledTimes(1);
		const body = JSON.parse(obsidianMock.requestUrl.mock.calls[0][0].body);
		expect(body.notes.length).toBe(3);
	});

	it('does not call bridge when only un-flagged notes are saved', async () => {
		const vault = makeVault([{ path: 'plain.md', content: 'no frontmatter' }]);
		const runtime = new DarbeeMemoryRuntime(
			{ bridgeUrl: 'http://localhost:5000', defaultTenant: 'private', defaultKind: 'observation', debounceMs: 2000, defaultScope: 'both' },
			vault as any,
		);
		runtime.handleModify({ path: 'plain.md' });
		await vi.advanceTimersByTimeAsync(2100);
		await runDebounceFlush(runtime);
		expect(obsidianMock.requestUrl).not.toHaveBeenCalled();
	});

	it('drops notes with empty bodies before sending', async () => {
		const vault = makeVault([{ path: 'empty.md', content: '---\nmemory: true\n---\n\n' }]);
		const runtime = new DarbeeMemoryRuntime(
			{ bridgeUrl: 'http://localhost:5000', defaultTenant: 'private', defaultKind: 'observation', debounceMs: 2000, defaultScope: 'both' },
			vault as any,
		);
		runtime.handleModify({ path: 'empty.md' });
		await vi.advanceTimersByTimeAsync(2100);
		await runDebounceFlush(runtime);
		expect(obsidianMock.requestUrl).not.toHaveBeenCalled();
	});

	it('buffers a save while a previous ingest is in flight', async () => {
		const vault = makeVault([
			{ path: 'a.md', content: '---\nmemory: true\n---\nhello a' },
			{ path: 'b.md', content: '---\nmemory: true\n---\nhello b' },
		]);
		let resolveFirst: (v: unknown) => void = () => {};
		obsidianMock.requestUrl.mockImplementationOnce(
			() => new Promise((r) => { resolveFirst = r; }),
		);
		obsidianMock.requestUrl.mockResolvedValueOnce({
			status: 200,
			json: { embeddedCount: 1, cachedCount: 0, failedCount: 0, staleDeletedCount: 0, durationMs: 1, perNote: [] },
			text: '',
		});

		const runtime = new DarbeeMemoryRuntime(
			{ bridgeUrl: 'http://localhost:5000', defaultTenant: 'private', defaultKind: 'observation', debounceMs: 2000, defaultScope: 'both' },
			vault as any,
		);

		runtime.handleModify({ path: 'a.md' });
		await vi.advanceTimersByTimeAsync(2100);
		// First call is in-flight. Trigger another save.
		runtime.handleModify({ path: 'b.md' });
		await vi.advanceTimersByTimeAsync(2100);
		// Resolve the first call now.
		resolveFirst({
			status: 200,
			json: { embeddedCount: 1, cachedCount: 0, failedCount: 0, staleDeletedCount: 0, durationMs: 1, perNote: [] },
			text: '',
		});
		await runDebounceFlush(runtime);

		expect(obsidianMock.requestUrl).toHaveBeenCalledTimes(2);
	});
});
```

- [ ] **Step 3: Run; expect import error**

```bash
cd obsidian-plugin
npm test 2>&1 | tail -10
```

Expected: `runDebounceFlush` and `DarbeeMemoryRuntime` not found.

- [ ] **Step 4: Create `src/main.ts`**

```ts
import { Plugin, TFile, WorkspaceLeaf } from 'obsidian';
import { DEFAULT_SETTINGS, DarbeeMemorySettingTab } from './settings';
import { DarbeeMemorySidebar, VIEW_TYPE_SIDEBAR } from './sidebar-view';
import { ingestNotes } from './bridge-client';
import { parseNoteFrontmatter, deriveNoteKey, bodyFromRaw, buildIngestPayload } from './ingester';
import type { Settings, MemoryKind, QueuedNote } from './types';

// Vault subset that the runtime needs; matches Obsidian's Vault for the fields used.
interface VaultLike {
	getMarkdownFiles(): Array<{ path: string }>;
	read(file: { path: string }): Promise<string>;
	on(event: 'modify', cb: (file: { path: string }) => void): unknown;
}

export class DarbeeMemoryRuntime {
	settings: Settings;
	vault: VaultLike;
	queue: Map<string, QueuedNote> = new Map();
	timer: ReturnType<typeof setTimeout> | null = null;
	inFlight: Promise<unknown> | null = null;
	pendingFlushChain: Promise<unknown> = Promise.resolve();

	constructor(settings: Settings, vault: VaultLike) {
		this.settings = settings;
		this.vault = vault;
	}

	handleModify(file: { path: string }): void {
		this.pendingFlushChain = this.pendingFlushChain.then(async () => {
			const raw = await this.vault.read(file);
			const parsed = parseNoteFrontmatter(raw, this.settings);
			if (!parsed.shouldIngest) return;
			const body = bodyFromRaw(raw);
			const key = deriveNoteKey(file.path);
			const title = file.path.replace(/\.md$/, '').split('/').pop() ?? file.path;
			this.queue.set(key, { key, title, kind: parsed.kind, body });
			this.scheduleFlush();
		});
	}

	private scheduleFlush(): void {
		if (this.timer) clearTimeout(this.timer);
		this.timer = setTimeout(() => {
			this.timer = null;
			this.pendingFlushChain = this.pendingFlushChain.then(() => this.flush());
		}, this.settings.debounceMs);
	}

	private async flush(): Promise<void> {
		if (this.inFlight) {
			await this.inFlight;
		}
		if (this.queue.size === 0) return;

		const drained = Array.from(this.queue.values());
		this.queue.clear();

		// currentKeys: every note in the vault that still has memory:true.
		const all = this.vault.getMarkdownFiles();
		const currentKeys: string[] = [];
		for (const f of all) {
			const raw = await this.vault.read(f);
			const parsed = parseNoteFrontmatter(raw, this.settings);
			if (parsed.shouldIngest) currentKeys.push(deriveNoteKey(f.path));
		}

		const payload = buildIngestPayload({
			tenant: this.settings.defaultTenant,
			queued: drained,
			currentKeys,
		});
		if (payload.notes.length === 0 && currentKeys.length === 0) return;

		this.inFlight = ingestNotes(this.settings.bridgeUrl, payload);
		try {
			await this.inFlight;
		} finally {
			this.inFlight = null;
		}
	}
}

// Test helper: awaits any pending flush chain so tests can assert post-debounce state.
export async function runDebounceFlush(runtime: DarbeeMemoryRuntime): Promise<void> {
	await runtime.pendingFlushChain;
}

export default class DarbeeMemoryPlugin extends Plugin {
	settings: Settings = DEFAULT_SETTINGS;
	runtime!: DarbeeMemoryRuntime;

	async onload() {
		await this.loadSettings();
		this.runtime = new DarbeeMemoryRuntime(this.settings, this.app.vault as any);

		this.registerEvent(
			this.app.vault.on('modify', (file) => {
				if (file instanceof TFile && file.extension === 'md') {
					this.runtime.handleModify({ path: file.path });
				}
			}),
		);

		this.registerView(VIEW_TYPE_SIDEBAR, (leaf: WorkspaceLeaf) => new DarbeeMemorySidebar(leaf, this));

		this.addCommand({
			id: 'open-sidebar',
			name: 'Open sidebar',
			callback: () => this.activateSidebar(),
		});

		this.addCommand({
			id: 'ingest-now',
			name: 'Ingest flagged notes now',
			callback: async () => {
				// Force-flush: enqueue every flagged note's current contents.
				const files = this.app.vault.getMarkdownFiles();
				for (const f of files) this.runtime.handleModify({ path: f.path });
			},
		});

		this.addSettingTab(new DarbeeMemorySettingTab(this.app, this));
	}

	async loadSettings() {
		this.settings = Object.assign({}, DEFAULT_SETTINGS, await this.loadData());
	}

	async saveSettings() {
		await this.saveData(this.settings);
	}

	async activateSidebar() {
		const { workspace } = this.app;
		let leaf = workspace.getLeavesOfType(VIEW_TYPE_SIDEBAR)[0];
		if (!leaf) {
			leaf = workspace.getRightLeaf(false) ?? workspace.getLeaf(true);
			await leaf.setViewState({ type: VIEW_TYPE_SIDEBAR });
		}
		workspace.revealLeaf(leaf);
	}
}
```

- [ ] **Step 5: Run tests; expect all to pass**

```bash
cd obsidian-plugin
npm test 2>&1 | tail -15
```

Expected: 4/4 main-runtime tests pass (in addition to ingester + bridge-client tests → 15 total).

- [ ] **Step 6: Verify the plugin builds cleanly**

```bash
cd obsidian-plugin
npm run build
ls dist/
```

Expected: `dist/main.js` rebuilt without errors.

- [ ] **Step 7: Commit**

```bash
cd /home/deovolente/repos/DarbeesChasingRainbows
git add obsidian-plugin/src/main.ts obsidian-plugin/test/main.test.ts obsidian-plugin/test/obsidian-stub.ts
git commit -m "feat(obsidian): plugin entry with debounce, on-save ingest, and commands"
```

---

## Task 13: Root npm scripts + CLAUDE.md + PR

**Files:**
- Modify: `/home/deovolente/repos/DarbeesChasingRainbows/package.json`
- Create: `/home/deovolente/repos/DarbeesChasingRainbows/scripts/obsidian-link.sh`
- Create: `/home/deovolente/repos/DarbeesChasingRainbows/scripts/obsidian-unlink.sh`
- Modify: `/home/deovolente/repos/DarbeesChasingRainbows/CLAUDE.md`

- [ ] **Step 1: Add npm scripts to root `package.json`**

In the root `package.json` `"scripts"` block, add four entries adjacent to the other npm-script entries:

```json
"obsidian:build": "npm --prefix obsidian-plugin run build",
"obsidian:dev":   "npm --prefix obsidian-plugin run dev",
"obsidian:link":  "bash scripts/obsidian-link.sh",
"obsidian:unlink":"bash scripts/obsidian-unlink.sh",
"test:plugin":    "npm --prefix obsidian-plugin test"
```

- [ ] **Step 2: Create `scripts/obsidian-link.sh`**

```bash
#!/usr/bin/env bash
# Symlink obsidian-plugin/dist/ into .obsidian/plugins/darbee-memory/.
# Idempotent: replaces an existing symlink, refuses to clobber a real directory.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="$REPO_ROOT/obsidian-plugin/dist"
TARGET_DIR="$REPO_ROOT/.obsidian/plugins"
LINK="$TARGET_DIR/darbee-memory"

[[ -d "$SRC" ]] || { echo "build output missing at $SRC — run 'npm run obsidian:build' first" >&2; exit 1; }
mkdir -p "$TARGET_DIR"

if [[ -L "$LINK" ]]; then
	rm "$LINK"
elif [[ -e "$LINK" ]]; then
	echo "$LINK exists and is not a symlink — refusing to replace" >&2
	exit 1
fi

ln -s "$SRC" "$LINK"
echo "linked $LINK -> $SRC"
echo "Enable 'Darbee Memory' in Obsidian -> Community Plugins."
```

Make it executable: `chmod +x scripts/obsidian-link.sh`.

- [ ] **Step 3: Create `scripts/obsidian-unlink.sh`**

```bash
#!/usr/bin/env bash
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LINK="$REPO_ROOT/.obsidian/plugins/darbee-memory"

if [[ -L "$LINK" ]]; then
	rm "$LINK"
	echo "removed symlink $LINK"
elif [[ ! -e "$LINK" ]]; then
	echo "no symlink at $LINK"
else
	echo "$LINK is not a symlink — leaving it alone" >&2
	exit 1
fi
```

Make it executable: `chmod +x scripts/obsidian-unlink.sh`.

- [ ] **Step 4: Verify scripts run**

```bash
cd /home/deovolente/repos/DarbeesChasingRainbows
npm run obsidian:build
npm run obsidian:link
ls -la .obsidian/plugins/darbee-memory
npm run obsidian:unlink
```

Expected: link succeeds → ls shows symlink → unlink removes it. (Re-run `obsidian:link` before opening Obsidian for the smoke test.)

- [ ] **Step 5: Update `CLAUDE.md`**

In the "Commands (Authoring scripts — Phase 13)" table, add two rows after the existing `rag:check-fresh` row:

```markdown
| Build Obsidian plugin | `npm run obsidian:build` | After editing `obsidian-plugin/` source |
| Link plugin into vault | `npm run obsidian:link` | First-time install; idempotent |
```

In the "Things to be careful about" section, append:

```markdown
- **Memory ingest**: The Obsidian plugin runs on save. The `memory: true` frontmatter is the only opt-in. Removing it un-flags the note; on next save the bridge stale-deletes the corresponding memory row (tenant=private only — public posts unaffected).
```

- [ ] **Step 6: Run the full JS suite to confirm nothing regressed**

```bash
npm run test:scripts
npm run test:plugin
```

Expected: prior counts plus 15 new plugin tests; no regressions.

- [ ] **Step 7: Commit**

```bash
git add package.json scripts/obsidian-link.sh scripts/obsidian-unlink.sh CLAUDE.md
git commit -m "feat(build): obsidian plugin npm scripts + CLAUDE.md authoring table"
```

- [ ] **Step 8: Manual end-to-end smoke (requires stack up)**

```bash
make up
npm run rag:reindex
npm run obsidian:build
npm run obsidian:link
```

In Obsidian:
1. Enable "Darbee Memory" in Community Plugins.
2. Create note `daily/test.md` with frontmatter:
   ```yaml
   ---
   memory: true
   memory_kind: observation
   ---
   ```
   and body `test note about cast iron pans`.
3. Save. After ~2s, status bar shows `✓ synced 1` (if you wired one — otherwise the request succeeded silently; verify via Arango).
4. Run command palette: "Darbee Memory: Open sidebar".
5. Query `cast iron`, scope `private` → expect 1 result with kind=observation, tenant=private badge.
6. Query `rv life`, scope `both` → posts + notes interleaved with badges.

Verify Arango:

```bash
curl -s -u root:password -X POST \
  http://localhost:8529/_db/darbees_knowledge/_api/cursor \
  -H 'content-type: application/json' \
  -d '{"query":"FOR d IN memory_observations FILTER d.tenant_id==\"private\" RETURN d.note_key"}'
```

Expected: response includes `obsidian://daily/test.md`.

- [ ] **Step 9: Open PR**

```bash
git push -u main feature/obsidian-memory
gh pr create --base master --head feature/obsidian-memory --title "feat: Obsidian ↔ memory (live-sync plugin + ingest-notes + tenant-filtered search)" --body "$(cat <<'EOF'
## Summary
- New Obsidian plugin (`obsidian-plugin/`) ingests notes flagged `memory: true` into private-tenant collections (observations/facts/decisions) on save with a 2s debounce.
- Sidebar search with posts / private / both scope toggle, backed by an extended `/api/memory/search`.
- Bridge gains `POST /api/memory/ingest-notes` and accepts `kinds` + `tenants` arrays in search requests. Same Arango DB; tenant filter is the isolation guard.
- MemoryStore gains `UpsertNoteAsync` (hash-cache) and `DeleteStaleNotesAsync` (scoped by tenant + `source=="obsidian"`).
- Critical regression guard: integration test `HandleSearchAsync_TenantIsolation_PrivateNeverLeaksWhenQueryingPublic`.

Spec: `docs/superpowers/specs/2026-05-18-obsidian-memory-design.md`.
Plan: `docs/superpowers/plans/2026-05-18-obsidian-memory.md`.

## Test plan
- [ ] `ARANGO_TEST_RUN=1 dotnet test dais-bridge.tests/dais-bridge.tests.csproj` — bridge integration tests green (existing + 11 new).
- [ ] `npm run test:plugin` — Vitest passes (15 tests).
- [ ] `npm run test:scripts` — JS suite unchanged.
- [ ] Manual smoke: install plugin, save flagged note, sidebar query returns it.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Expected: PR URL printed. (The remote is named `main` locally — see `git remote -v` if push fails with "origin not found".)

---

## Decision references (from spec §10)

These choices are inherited from the spec — don't re-litigate.

- Frontmatter opt-in (`memory: true`); private notes default to `tenant=private`.
- Same Arango DB, tenant filter for isolation. **Test 10 is the regression guard.**
- `source == "obsidian"` filter on stale-delete; never touches Arango-direct or `memory_posts` rows.
- Full-sync delete via `currentKeys` envelope on every ingest call (matches `rag-reindex`).
- `_key = sha1(human_key)` with human key in `note_key`. Arango key constraints can't accept slashes.
- 2s debounce default. 30s timeout per call. Obsidian `requestUrl` bypasses CORS.
- Search response carries `Kind` + `Tenant` per row; plugin switches on `Kind` for click handling.
