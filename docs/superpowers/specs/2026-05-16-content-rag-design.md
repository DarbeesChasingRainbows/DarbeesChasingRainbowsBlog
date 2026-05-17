# Content RAG — Design Spec

**Date:** 2026-05-16
**Status:** Draft, ready for review
**Branch (proposed):** `feature/content-rag` (off `master`)
**Related:** Layers on top of [Phase 11 — Graph-Backed RAG](2026-05-09-graph-backed-rag-design.md). Reuses the existing `MemoryStore`, `IEmbeddingClient`, and Arango connection infrastructure. Does **not** require Phase 11 B2–G2 to land first.

---

## 1. Problem

The published blog content under `src/content/**/*.mdx` (blog, projects, field-notes, books) is invisible to the DAIS Bridge's existing memory layer. The bridge's chat interfaces have no way to surface "what does the blog say about X?" without the blog content being embedded and retrievable from Arango.

The companion script `scripts/related-rebuild.mjs` already embeds posts for static related-post linking, but writes a flat JSON file consumed at build time — it isn't a retrieval surface, and it duplicates work the bridge could own.

Phase 11 built the storage substrate (`MemoryStore`, vector index lifecycle, two-phase write, pending-embedding queue) for chat-driven memory (decisions, observations, facts, summaries). This spec extends that same substrate to cover static blog content, then exposes a retrieval HTTP endpoint suitable for direct testing today and for wrapping in a kernel function (`MemoryPlugin.SearchPosts`) once Phase 11 B2 lands.

In parallel, the spec resolves stack drift since Phase 11 was written: LM Studio is no longer used, llama.cpp runs directly on the host (chat at `:8080`, embedding at `:8081`), and the embedding model is `qwen3-embedding-8b` (4096-dim) instead of `nomic-embed-text-v1.5` (768-dim).

## 2. Goals

1. **Ingest** all published posts under `src/content/{blog,projects,field-notes,books}/**/*.mdx` into Arango as embedded vectors, idempotently, with stale-slug deletion.
2. **Retrieve** posts via a `POST /api/memory/search` HTTP endpoint that performs query embedding + AQL cosine similarity, returning ranked results with metadata and snippets.
3. **Layer on the existing `MemoryStore`** rather than introduce a parallel `ContentStore`. Posts become a new `MemoryKind`.
4. **Adapt to the current stack** — split the chat and embedding endpoint URLs, switch embedding model + dimension, drop LM Studio naming.
5. **Solve schema-version safety properly.** Embedding model and dimension are recorded in a `memory_meta` collection. The bridge refuses to start on mismatch and exposes a migration endpoint that preserves canonical text while clearing derived embeddings.
6. **Stay testable end-to-end** under existing `ARANGO_TEST_RUN=1` plus a new `LLM_TEST_RUN=1` gate.

## 3. Non-goals

- **No kernel plugin in this spec.** Wrapping retrieval in a `MemoryPlugin.SearchPosts` kernel function belongs to Phase 11 B2. The HTTP surface is what we ship; the kernel function is a thin wrapper over the same store method.
- **No SignalR hub integration.** Hubs setting `TenantContext` and routing retrieval is Phase 11 B4.
- **No paragraph-level chunking.** Each post produces exactly two embeddings (a summary vector and a body vector). Chunked retrieval is a follow-up if scale ever demands it.
- **No background re-embed worker.** The `memory_pending_embeddings` queue exists in the schema and `MemoryStore` knows how to enqueue, but the worker that drains it is out of scope here. For posts, re-embedding happens on demand via the reindex endpoint.
- **No auth on the new HTTP endpoints.** Both endpoints are unauthenticated for MVP. The bridge is bound to local Podman networking + host loopback. See §10 for the follow-up gap.
- **No vector index for `memory_posts` yet.** At 26 vectors, brute-force AQL `COSINE_SIMILARITY` is sub-millisecond. The lazy vector-index path (Phase 11 A4) remains available for collections that grow past the IVF training threshold.

## 4. Architecture

```
┌────────────────────────────────────────────────────────────────────────┐
│  HOST (Framework Desktop, AMD Ryzen AI Max+ 395, unified memory)       │
│                                                                        │
│  scripts/rag-reindex.mjs ──reads──► src/content/**/*.mdx               │
│         │                                                              │
│         └─curl──► dais-bridge :5000 ──► [POST /api/admin/              │
│                                            reindex-posts]              │
│                                                                        │
│  ┌──────────────────────────────────────────────────────────┐          │
│  │  dais-bridge (Podman, dev profile)                       │          │
│  │  ┌──────────────────────────────────────────────────┐    │          │
│  │  │  Program.cs — Minimal API                        │    │          │
│  │  │    POST /api/admin/reindex-posts                 │    │          │
│  │  │    POST /api/memory/search                       │    │          │
│  │  │    POST /api/admin/migrate-embeddings            │    │          │
│  │  └────────────────────┬─────────────────────────────┘    │          │
│  │                       │                                   │          │
│  │  ┌────────────────────▼─────────────────────────────┐    │          │
│  │  │  MemoryStore (extended)                          │    │          │
│  │  │    UpsertPostAsync                               │    │          │
│  │  │    SearchAsync (kinds, tenants, k)               │    │          │
│  │  │    DeleteStalePostsAsync                         │    │          │
│  │  │    MigrateEmbeddingsAsync                        │    │          │
│  │  │    EnsureSchemaAsync (extended: meta + posts)    │    │          │
│  │  └─────┬────────────────────────────────┬───────────┘    │          │
│  │        │                                │                │          │
│  │  ┌─────▼─────────────────────┐    ┌─────▼────────────┐   │          │
│  │  │ OpenAiCompatibleEmbedding │    │ ArangoDBClient + │   │          │
│  │  │ Client (qwen3-emb-8b)     │    │ raw HTTP for AQL │   │          │
│  │  └─────┬─────────────────────┘    └─────┬────────────┘   │          │
│  └────────┼───────────────────────────────┼────────────────┘          │
│           │                                │                          │
│           ▼                                ▼                          │
│   llama.cpp :8081                  ArangoDB :8529 (Podman)            │
│   /v1/embeddings                   /_db/darbees_knowledge             │
│   qwen3-embedding-8b, 4096-dim     memory_posts, memory_decisions,    │
│                                    memory_observations, memory_facts, │
│                                    memory_summaries, memory_entities, │
│                                    memory_edges,                      │
│                                    memory_pending_embeddings,         │
│                                    memory_meta                        │
└────────────────────────────────────────────────────────────────────────┘

(llama.cpp chat at :8080 / llama-4-maverick is NOT in the ingestion or
 retrieval data path. Chat is the consumer of retrieval results in the
 eventual end-to-end flow, not a participant in the indexing pipeline.)
```

**Ingest path:** `scripts/rag-reindex.mjs` walks the filesystem (reusing `scripts/lib/posts.mjs`'s `listPosts()`), enumerates posts, and POSTs the structured payload to the bridge. The bridge composes summary + body texts, hashes them, batch-embeds via `:8081`, upserts to Arango. The bridge does no filesystem I/O — that obligation stays in the JS authoring tooling where the canonical "what is a publishable post" rules already live.

**Retrieval path:** Caller POSTs a query to `/api/memory/search`. The bridge embeds the query via `:8081`, runs an AQL `COSINE_SIMILARITY` scan over the `memory_posts` collection filtered by tenant and kind, returns top-`2k` rows, dedups application-side to top-`k` distinct posts. No vector index involvement at this scale.

**Migration path:** `EnsureSchemaAsync` is invoked lazily (first-use, not at `app.Build`) — a deliberate change from Phase 11 A6's startup-eager bootstrap. The bridge process always starts; the first endpoint that needs schema triggers the check. On embedding-config mismatch the check throws `EmbeddingConfigMismatchException`. The endpoint that triggered it returns 503 with a remediation pointer. `POST /api/admin/migrate-embeddings` is the one endpoint exempt from the schema check (it exists to *resolve* mismatches), so it remains reachable. The operator runs it; it drops vector indexes, clears `embedding` fields, enqueues affected docs into `memory_pending_embeddings`, and updates `memory_meta/embedding_config`. Subsequent endpoint calls retry the schema check and succeed.

## 5. Components

### 5.1 Stack-drift configuration

`dais-bridge/Program.cs` reads three new environment-variable groupings:

```csharp
var lmChatUrl        = Env("LLM_CHAT_URL")
                       ?? Env("LMSTUDIO_URL")              // back-compat warning
                       ?? Config["AI:ChatUrl"]
                       ?? "http://localhost:8080/v1";
var lmEmbeddingUrl   = Env("LLM_EMBEDDING_URL")
                       ?? Config["AI:EmbeddingUrl"]
                       ?? lmChatUrl;                        // fall back to chat URL
var embeddingModelId = Env("AI_EMBEDDING_MODEL_ID")
                       ?? Config["AI:EmbeddingModelId"]
                       ?? "qwen3-embedding-8b";
var embeddingDimension = int.Parse(
    Env("AI_EMBEDDING_DIMENSION")
    ?? Config["AI:EmbeddingDimension"]
    ?? "4096");
```

The chat completion service uses `lmChatUrl`. The embedding client uses `lmEmbeddingUrl`. If `LMSTUDIO_URL` is read because `LLM_CHAT_URL` is unset, the bridge logs a one-time warning at startup recommending the new var.

`appsettings.json` defaults updated:
- `AI:ChatUrl` → `http://localhost:8080/v1`
- `AI:EmbeddingUrl` → `http://localhost:8081/v1`
- `AI:EmbeddingModelId` → `qwen3-embedding-8b`
- `AI:EmbeddingDimension` → `4096`
- (deletes) `AI:LMStudioUrl`, `AI:LMStudioApiKey` — superseded
- Retains `AI:ApiKey` (single key works for both endpoints when both servers require auth; llama.cpp itself doesn't require auth by default but the bridge still sends a Bearer header if `AI_API_KEY` is set).

`compose.yaml` updates for `dais-bridge-dev` and `dais-bridge-prod` environment blocks:
- Replace `LMSTUDIO_URL: http://host.containers.internal:1234/v1` with `LLM_CHAT_URL: http://host.containers.internal:8080/v1`
- Add `LLM_EMBEDDING_URL: http://host.containers.internal:8081/v1`
- Add `AI_EMBEDDING_MODEL_ID: ${AI_EMBEDDING_MODEL_ID:-qwen3-embedding-8b}`
- Add `AI_EMBEDDING_DIMENSION: ${AI_EMBEDDING_DIMENSION:-4096}`
- Replace `AI_MODEL_ID: ${AI_MODEL_ID:-local-model}` with `AI_MODEL_ID: ${AI_MODEL_ID:-llama-4-maverick}`

The `lm-probe` sidecar in `compose.yaml` is deleted. Its previous polling of `:1234` (LM Studio's default) is no longer meaningful. The bridge fails loudly the first time an embed request hits a dead `:8081`, which is sufficient signal.

`.env` already contains the relevant keys (`LMSTUDIO_URL`, `LLM_EMBEDDING_URL`, `AI_MODEL_ID`, `AI_EMBEDDING_MODEL_ID`). Update `LMSTUDIO_URL` to `LLM_CHAT_URL` and the `make init` template that copies `.env.example` reflects the new names.

### 5.2 `OpenAiCompatibleEmbeddingClient` (renamed from `LmStudioEmbeddingClient`)

Class file rename + namespace-internal rename. No behavior change. The class talks OpenAI-compatible `/v1/embeddings`, which works against LM Studio, llama.cpp's `llama-server`, vLLM, Ollama, OpenAI itself, and any other compliant server. The previous name was misleading; the new name describes what it actually does.

Touch points:
- `dais-bridge/Memory/LmStudioEmbeddingClient.cs` → `dais-bridge/Memory/OpenAiCompatibleEmbeddingClient.cs` (class renamed inside).
- `dais-bridge/Program.cs` — DI registration updated.
- `dais-bridge.tests/Memory/LmStudioEmbeddingClientTests.cs` → `dais-bridge.tests/Memory/OpenAiCompatibleEmbeddingClientTests.cs`.
- Phase 11 docs (`RESUME-graph-backed-rag.md`, `2026-05-09-graph-backed-rag-design.md`, `2026-05-09-graph-backed-rag.md`, `TODO-phase11.md`) — `LmStudio*` references updated to the new names, with a note at the top of each that the embedding stack switched to llama.cpp + qwen3-embedding-8b on 2026-05-16.

### 5.3 `MemoryKind` and `MemoryCollections` additions

`dais-bridge/Memory/Models/MemoryKind.cs` — current state (post-Phase-11-A1):

```csharp
public enum MemoryKind { Decision, Observation, Fact, Summary, Entity }

public static class MemoryCollections
{
    public const string Decisions          = "memory_decisions";
    public const string Observations       = "memory_observations";
    public const string Facts              = "memory_facts";
    public const string Summaries          = "memory_summaries";
    public const string Entities           = "memory_entities";
    public const string Edges              = "memory_edges";
    public const string PendingEmbeddings  = "memory_pending_embeddings";

    public static string ForKind(MemoryKind kind) => kind switch
    {
        MemoryKind.Decision    => Decisions,
        MemoryKind.Observation => Observations,
        MemoryKind.Fact        => Facts,
        MemoryKind.Summary     => Summaries,
        MemoryKind.Entity      => Entities,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
```

Changes in this spec — add `Post` to the enum and two new collection constants, both following the existing `memory_` prefix convention:

```csharp
public enum MemoryKind
{
    Decision,
    Observation,
    Fact,
    Summary,
    Entity,
    Post,    // NEW
}

public static class MemoryCollections
{
    // ...existing constants unchanged...
    public const string Posts = "memory_posts";   // NEW
    public const string Meta  = "memory_meta";    // NEW

    public static string ForKind(MemoryKind kind) => kind switch
    {
        // ...existing arms unchanged...
        MemoryKind.Post        => Posts,          // NEW
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
```

`Edges` and `PendingEmbeddings` deliberately have no enum value — edges are relationships (not embeddable content) and `memory_pending_embeddings` is queue infrastructure. `Meta` is config-singleton infrastructure and also has no enum value. Only embeddable content kinds get enum entries.

### 5.4 `memory_meta` collection and `embedding_config`

The `memory_meta` collection holds singleton bridge configuration documents. Created by `EnsureSchemaAsync`. Not tenant-scoped (single global config). Keys are namespace-prefixed for future expansion.

Document shape (`memory_meta/embedding_config`):

```json
{
  "_key": "embedding_config",
  "model": "qwen3-embedding-8b",
  "dimension": 4096,
  "first_set_at": "2026-05-16T12:30:00Z",
  "last_set_at": "2026-05-16T12:30:00Z"
}
```

`EnsureSchemaAsync` flow:

1. Create `memory_meta` collection (idempotent).
2. Try `GET memory_meta/embedding_config`:
   - **Absent** → write current `{ model, dimension, first_set_at, last_set_at }`. Continue.
   - **Present and equal** to current configured `(model, dimension)` → continue.
   - **Present and unequal** → throw `EmbeddingConfigMismatchException` with message:

     ```
     Embedding config mismatch.
       In Arango: { model: nomic-embed-text-v1.5, dimension: 768 }
       Bridge:    { model: qwen3-embedding-8b,    dimension: 4096 }
     Existing vector indexes and embeddings are incompatible with the configured model.
     Remediation:
       curl -X POST http://localhost:5000/api/admin/migrate-embeddings \
            -H 'content-type: application/json' \
            -d '{ "confirm": "preserve-and-reembed" }'
     ```

3. Continue with existing collection + persistent index creation per Phase 11 A4.
4. Adds `memory_posts` to the collections-created loop.
5. Adds one persistent index on `memory_posts`: `["slug", "vector_kind"]`. Used by `UpsertPostAsync` for direct lookup.
6. Adds one persistent index on `memory_posts`: `["collection", "slug"]`. Used by stale-deletion enumeration.

**Future expansion note:** When the bridge gains a budgeting or inventory feature, each gets its own database (`darbees_budget`, `darbees_inventory`) with its own `memory_meta` collection. Multi-tenant isolation continues to use `tenant_id` within each DB. The `memory_meta`/`embedding_config` pattern is per-database, not per-tenant — embeddings are a single-model decision across all tenants in a given database.

### 5.5 `Post` document shape

Posts live in the `memory_posts` collection. Each post produces exactly two documents: one for the summary vector, one for the body vector.

```json
{
  "_key": "blog__welcome-to-darbees-chasing-rainbows__summary",
  "slug": "welcome-to-darbees-chasing-rainbows",
  "collection": "blog",
  "vector_kind": "summary",
  "tenant_id": "public",
  "text": "<exact text that was embedded>",
  "embedding": [4096 floats],
  "hash": "sha256:<embed-text-hash>",
  "title": "Welcome to Darbees Chasing Rainbows",
  "description": "An introduction to our family blog ...",
  "pub_date": "2026-04-29",
  "category": "Faith & Reflections",
  "tags": ["family", "faith"],
  "entity_mentions": ["Kingdom Farm", "Florida"],
  "ai_summary": "The Darbees family ...",
  "status": "ready",
  "created_at": "2026-05-16T12:30:00Z",
  "updated_at": "2026-05-16T12:30:00Z"
}
```

**`_key` shape:** `{collection}__{slug}__{vector_kind}`. Deterministic — upserts go straight to a PATCH/INSERT by `_key` without a query. The double-underscore separator is safe (slugs in this project are lowercase kebab-case, no underscores).

**Tenant:** Hard-coded `"public"`. Not a user input. The retrieval endpoint accepts a `tenant` parameter for future blending with user-private memory, but for post writes there is never a per-tenant variation.

**Status semantics:**
- `"ready"` — embedding is current and matches `memory_meta/embedding_config`.
- `"pending_embedding"` — set by `MigrateEmbeddingsAsync`; doc is preserved but `embedding` field has been cleared. Excluded from `SearchAsync` results.

**Hash computation:**

```
hash = sha256( embeddingModelId + ":" + text )
```

Folding the model id in invalidates the cache on model swap.

### 5.6 `MemoryStore` new methods

Signatures (in `dais-bridge/Memory/MemoryStore.cs`):

```csharp
public Task<UpsertPostResult> UpsertPostAsync(
    PostDocument post,
    bool force,
    CancellationToken ct = default);

public Task<int> DeleteStalePostsAsync(
    IReadOnlyCollection<(string Collection, string Slug)> currentPosts,
    CancellationToken ct = default);

public Task<List<ScoredMemoryItem>> SearchAsync(
    float[] queryVec,
    IReadOnlyList<MemoryKind> kinds,
    IReadOnlyList<string> tenants,
    int rawK,
    CancellationToken ct = default);

public Task<MigrationResult> MigrateEmbeddingsAsync(
    string confirmToken,
    CancellationToken ct = default);
```

Where:

```csharp
public sealed record PostDocument(
    string Collection,
    string Slug,
    string Title,
    string Description,
    string Body,                            // already stripped of MDX components on JS side
    string? AiSummary,
    IReadOnlyList<string> KeyTakeaways,
    IReadOnlyList<FaqEntry> Faq,
    IReadOnlyList<string> EntityMentions,
    IReadOnlyList<string> Tags,
    string? Category,
    string? PubDate);

public sealed record UpsertPostResult(
    string Slug,
    VectorWriteOutcome Summary,
    VectorWriteOutcome Body);

public enum VectorWriteOutcome { Embedded, Cached, Failed }

public sealed record MigrationResult(
    EmbeddingConfig Previous,
    EmbeddingConfig Current,
    IReadOnlyList<string> IndexesDropped,
    IReadOnlyDictionary<string, int> DocsMarkedForReembed,
    int QueueSizeAfter);
```

**`UpsertPostAsync` behavior:**

For each of `(summary, body)`:
1. Compose text (see §6.1).
2. Hash the composed text + embedding model id.
3. Compute deterministic `_key`.
4. Try to read existing doc by `_key`.
5. If `!force` and existing doc's `hash` matches → return `VectorWriteOutcome.Cached` for that kind. Skip embedding.
6. Otherwise: queue the text for batch embedding (the caller — the HTTP handler — accumulates queues across all posts and flushes via `EmbedBatchAsync`).
7. After embedding returns, write the doc via PATCH-or-INSERT (the existing `Document.PostDocumentAsync` won't do PATCH-or-INSERT; we use the underlying ArangoDB upsert pattern via `_key` and a `merge` policy).

**`DeleteStalePostsAsync` behavior:**

```aql
FOR doc IN memory_posts
  FILTER doc.tenant_id == "public"
  FILTER NOT (doc.collection IN @collectionSet AND doc.slug IN @slugSet)
  REMOVE doc IN memory_posts
```

(Bind vars built from the caller's `currentPosts` set.)

Returns the number of removed documents.

**`SearchAsync` behavior:**

See §6.2 for the AQL. Returns raw rows; the HTTP handler does dedup-by-slug and snippet generation.

**`MigrateEmbeddingsAsync` behavior:**

Confirm token must be one of `"preserve-and-reembed"` (default safe mode) or `"wipe-and-reset"` (destructive — only for dev rebuilds, documented as such).

For `"preserve-and-reembed"`:
1. Read `memory_meta/embedding_config` (or `null` if absent — first-time setup).
2. For each content collection (`memory_decisions`, `memory_observations`, `memory_facts`, `memory_summaries`, `memory_posts`):
   - List existing vector indexes via `GET /_api/index?collection=<name>` → drop each via `DELETE /_api/index/<id>`. Collect into `IndexesDropped`.
   - AQL: `FOR doc IN <coll> FILTER doc.embedding != null UPDATE doc WITH { embedding: null, status: "pending_embedding", updated_at: DATE_ISO8601(DATE_NOW()) } IN <coll> COLLECT WITH COUNT INTO n RETURN n`.
   - Track count per collection in `DocsMarkedForReembed`.
3. For each doc just marked, enqueue a `memory_pending_embeddings` row (existing `EnqueuePendingEmbeddingAsync`).
4. Upsert `memory_meta/embedding_config` with current `(model, dimension)`, `last_set_at = now`. Set `first_set_at` only if the doc was absent.
5. Clear the in-memory `_vectorIndexReady` cache.
6. Return `MigrationResult`.

For `"wipe-and-reset"`: same as above except step 2's UPDATE becomes `REMOVE doc IN <coll>`, and step 3 is skipped (nothing to enqueue). Documented as **DEV ONLY — destroys canonical text**.

### 5.7 New HTTP endpoints in `Program.cs`

Three new Minimal API endpoints, registered before `app.Run()`.

**`POST /api/admin/reindex-posts`:**

Request body:
```json
{
  "force": false,
  "posts": [
    {
      "collection": "blog",
      "slug": "welcome-to-darbees-chasing-rainbows",
      "frontmatter": {
        "title": "Welcome to Darbees Chasing Rainbows",
        "description": "...",
        "pubDate": "2026-04-29",
        "category": "Faith & Reflections",
        "tags": ["family", "faith"],
        "aiSummary": "...",
        "keyTakeaways": ["...", "..."],
        "faq": [{ "question": "...", "answer": "..." }],
        "entityMentions": ["...", "..."]
      },
      "body": "<stripped markdown body>"
    }
  ]
}
```

Handler flow:
1. Parse + validate. Reject if any `(collection, slug)` is duplicated within `posts[]`.
2. For each post: compose summary + body texts, build `PostDocument`, accumulate into an upsert plan that distinguishes cache hits from embed-required entries.
3. For embed-required entries: batch via `EmbedBatchAsync` (batch size 16 by default, configurable via `Memory:EmbedBatchSize`).
4. Persist all docs.
5. Call `DeleteStalePostsAsync` with the set of `(collection, slug)` pairs from the payload.
6. Return summary.

Response body (200 OK):
```json
{
  "scanned": 13,
  "embedded": 24,
  "from_cache": 2,
  "deleted_stale": 0,
  "duration_ms": 4271,
  "posts": [
    {
      "slug": "welcome-to-darbees-chasing-rainbows",
      "collection": "blog",
      "summary": "ready",
      "body": "ready"
    },
    {
      "slug": "what-we-pack-first-in-the-rv",
      "collection": "blog",
      "summary": "cached",
      "body": "embedded"
    }
  ]
}
```

Field semantics:
- `scanned` — number of posts in the request payload.
- `embedded` — number of vectors actually written via the embedding server (`from_cache` excluded).
- `from_cache` — number of vectors skipped because their hash matched the existing doc.
- `deleted_stale` — number of docs removed by `DeleteStalePostsAsync` (one slug can contribute two: summary + body).
- `posts[*].summary` and `posts[*].body` — one of `embedded` | `cached` | `failed`. On `failed`, an additional `failure_reason` string is present.
- HTTP 200 means the run completed end-to-end. Per-post failures appear in the body, not as a 5xx.

**`POST /api/memory/search`:**

Request body:
```json
{
  "query": "what do they say about cast iron pans?",
  "kinds": ["post"],
  "k": 5,
  "tenant": "public"
}
```

Handler flow:
1. Validate. `kinds` defaults to `["post"]`. `k` defaults to 5, clamped to `[1, 50]`. `tenant` defaults to `"public"`.
2. Embed `query` via `_embeddingClient.EmbedAsync`. Measure `query_embed_ms`.
3. Translate `kinds` to collection set. For posts: query both `vector_kind=summary` and `vector_kind=body`. For other kinds: future Phase 11 work.
4. Call `MemoryStore.SearchAsync(queryVec, kinds, [tenant], rawK = k * 2)`. Measure `search_ms`.
5. Dedup application-side: group by `(collection, slug)`, keep the row with highest `sim`, sort, take top `k`.
6. Generate snippet:
   - `matched_kind=summary` → return `ai_summary` (truncated to ~280 chars if longer).
   - `matched_kind=body` → return first ~280 chars of `text`.
7. Return.

**`POST /api/admin/migrate-embeddings`:**

Request body:
```json
{ "confirm": "preserve-and-reembed" }
```

Handler flow:
1. Validate confirm token.
2. Call `MemoryStore.MigrateEmbeddingsAsync`.
3. Return `MigrationResult` as JSON.

This endpoint is exempt from `EnsureSchemaAsync` — it skips the lazy schema check so it remains callable when the bridge is in a config-mismatch state. The exemption is implemented as a separate non-schema-checking code path in `MemoryStore` (`MigrateEmbeddingsAsync` does its own minimal schema bootstrap — ensures `memory_meta` collection exists, reads/writes `embedding_config` directly, manages indexes — without invoking the full `EnsureSchemaAsync`). All other endpoints invoke `EnsureSchemaAsync` lazily on first use; if it throws `EmbeddingConfigMismatchException` the handler returns 503 with the remediation curl command in the body.

### 5.8 `scripts/rag-reindex.mjs`

New file under `scripts/`. Pattern mirrors `scripts/related-rebuild.mjs`. Reuses `listPosts()` from `scripts/lib/posts.mjs`.

CLI flags:
- `--force` → POST `{ force: true }`.
- `--collections blog,projects` → restrict to a subset (default: all four).
- `--bridge-url http://localhost:5000` → override default endpoint (env: `BRIDGE_URL`).

Output:
```
✓ blog/welcome-to-darbees-chasing-rainbows
  summary: cached, body: embedded
✓ projects/rv-water-tank-alert-system
  summary: embedded, body: embedded
...
13 posts: 2 cached, 22 embedded, 0 failed
Deleted 0 stale posts.
Duration: 4.3s
```

Exit code is `0` on full success, `1` if any post failed.

Also adds `npm run rag:reindex` to `package.json` invoking the script with `node --env-file-if-exists=.env scripts/rag-reindex.mjs`.

### 5.9 (Optional) `scripts/rag-search.mjs`

Thin helper for ad-hoc retrieval testing without typing curl. Reads query from args, hits `/api/memory/search`, pretty-prints results. Not on the critical path for shipping; included if it falls out cheaply.

```
$ npm run rag:search -- "cast iron pans"
0.847  blog/what-we-pack-first-in-the-rv     [body]
       A good cast iron pan ... earns its space ...
0.812  blog/read-aloud-rhythms-on-the-road   [summary]
       The Darbees built a daily read-aloud habit ...
...
```

## 6. Algorithms

### 6.1 Embedding text composition

For each post, two distinct strings are embedded:

**Summary text:**
```
{title}

{description}

AI Summary: {aiSummary}

Key Takeaways:
- {keyTakeaways[0]}
- {keyTakeaways[1]}
...

FAQ:
Q: {faq[0].question}
A: {faq[0].answer}

Q: {faq[1].question}
A: {faq[1].answer}
...

Mentions: {entityMentions.join(", ")}
```

**Body text:**
```
{title}

{description}

Tags: {tags.join(", ")}
Category: {category}
Mentions: {entityMentions.join(", ")}

{strippedBody}
```

Composition rules:
- Optional fields (any of `aiSummary`, `keyTakeaways`, `faq`, `entityMentions`, `category`, `tags`) are omitted entirely (including their header/label) when absent or empty. The composer never emits `"AI Summary: "` followed by nothing.
- `strippedBody` is computed on the JS side by `scripts/lib/posts.mjs:stripMdx()`. The bridge does not re-strip.
- All trailing whitespace is collapsed; the final string is `text.trim() + "\n"`.

### 6.2 Retrieval AQL

```aql
LET q = @query_vec
FOR doc IN memory_posts
  FILTER doc.tenant_id IN @tenants
  FILTER doc.status == "ready"
  FILTER doc.vector_kind IN @vector_kinds
  LET sim = COSINE_SIMILARITY(doc.embedding, q)
  SORT sim DESC
  LIMIT @raw_k
  RETURN {
    key:           doc._key,
    slug:          doc.slug,
    collection:    doc.collection,
    vector_kind:   doc.vector_kind,
    title:         doc.title,
    text:          doc.text,
    description:   doc.description,
    ai_summary:    doc.ai_summary,
    pub_date:      doc.pub_date,
    category:      doc.category,
    tags:          doc.tags,
    sim:           sim
  }
```

Bind vars:
- `@query_vec` — float array, length 4096.
- `@tenants` — string array. Default `["public"]`. Future chat flows pass `[user_tenant, "public"]`.
- `@vector_kinds` — string array. Default `["summary", "body"]`.
- `@raw_k` — int. Set to `k * 2` to give the dedup pass headroom.

Brute-force `COSINE_SIMILARITY` scans every row in `memory_posts`. At 26 rows × 4096-dim, measured cost is sub-millisecond. The seam to switch to `APPROX_NEAR_COSINE` (Phase 11 A4-style lazy vector index) is one AQL string and a `Memory:VectorNLists` tuning. We do not pre-engineer that switch.

### 6.3 Application-side dedup

Pseudo-code applied to the raw AQL rows:

```
hits_by_slug = {}
for row in rows:
    key = (row.collection, row.slug)
    if key not in hits_by_slug or row.sim > hits_by_slug[key].sim:
        hits_by_slug[key] = row
sorted_unique = sorted(hits_by_slug.values(), by=lambda r: -r.sim)
top_k = sorted_unique[:k]
return [build_search_result(r) for r in top_k]
```

`build_search_result` renders the response shape including `snippet` (per §5.7) and `url` (derived as `/{collection}/{slug}/` matching the Astro route structure).

## 7. Data flow

### 7.1 Full reindex (cold start, no cache)

```
$ npm run rag:reindex
  │
  ▼ scripts/rag-reindex.mjs walks src/content/**/*.mdx
  │   13 posts found (skipping _templates, *.wip.*, *.draft.*, _drafts)
  │
  ▼ POST /api/admin/reindex-posts { force: false, posts: [...13...] }
  │
  ▼ bridge: compose 26 texts (13 × {summary, body})
  │         hash each; lookup existing by _key → all absent
  │
  ▼ bridge: EmbedBatchAsync × 2 round trips (16+10 texts)
  │   → llama.cpp :8081, qwen3-embedding-8b, ~2-4s total
  │
  ▼ bridge: upsert 26 docs into Arango posts collection
  │   set status="ready", hash=<computed>
  │
  ▼ bridge: DeleteStalePostsAsync → no stale (cold start)
  │
  ▼ bridge: response { scanned: 13, embedded: 26, from_cache: 0, ... }
  │
  ▼ script prints summary, exits 0
```

### 7.2 Incremental reindex (most posts unchanged)

```
$ npm run rag:reindex   (after editing one post body)
  │
  ▼ POST /api/admin/reindex-posts { force: false, posts: [...13...] }
  │
  ▼ bridge: compose 26 texts; hash each
  │   24 hashes match existing docs → cache hit
  │   2 hashes differ (summary + body of the edited post)
  │
  ▼ bridge: EmbedBatchAsync × 1 round trip (2 texts)
  │
  ▼ bridge: upsert 2 docs
  │
  ▼ bridge: response { scanned: 13, embedded: 2, from_cache: 24, ... }
```

### 7.3 Single search

```
$ curl -sX POST :5000/api/memory/search \
       -H 'content-type: application/json' \
       -d '{ "query": "cast iron pans", "k": 5 }'
  │
  ▼ bridge: EmbedAsync("cast iron pans")
  │   → llama.cpp :8081, ~140ms
  │
  ▼ bridge: AQL COSINE_SIMILARITY scan over posts (tenants=[public], kinds=[summary,body])
  │   LIMIT 10  (raw_k = k * 2 = 10)
  │   → ~3ms
  │
  ▼ bridge: application-side dedup → top 5 distinct posts by best matching vector_kind
  │
  ▼ bridge: snippet per row from text or ai_summary
  │
  ▼ response: { results: [...5...], query_embed_ms: 142, search_ms: 3 }
```

### 7.4 Embedding model migration

```
operator decides to swap embedding model
  │
  ▼ stop bridge, change AI_EMBEDDING_MODEL_ID / AI_EMBEDDING_DIMENSION in .env
  │  + restart llama-server with new model on :8081
  │
  ▼ start bridge — process comes up, no schema check yet (lazy bootstrap)
  │
  ▼ first request (e.g. POST /api/memory/search) hits a non-migration endpoint
  │   ▼ MemoryStore.EnsureSchemaAsync (lazy) reads memory_meta/embedding_config
  │     mismatch detected → throw EmbeddingConfigMismatchException
  │   ▼ HTTP handler returns 503 with remediation curl command in body
  │
  ▼ operator runs the suggested:
  │   curl -X POST :5000/api/admin/migrate-embeddings \
  │     -d '{ "confirm": "preserve-and-reembed" }'
  │   (this endpoint is exempt from EnsureSchemaAsync — it exists to fix the
  │    mismatch, not to be blocked by it)
  │
  ▼ bridge: MigrateEmbeddingsAsync
  │   drops vector indexes on memory_decisions / memory_observations /
  │     memory_facts / memory_summaries / memory_posts
  │   AQL UPDATE: clear embedding + set status=pending_embedding
  │   enqueue each affected doc into memory_pending_embeddings
  │   upsert memory_meta/embedding_config = new (model, dim)
  │   clear in-memory _schemaReady flag so next request re-runs EnsureSchema
  │
  ▼ bridge logs migration result
  │
  ▼ operator runs npm run rag:reindex → posts re-embedded with new model
  │   (re-runs EnsureSchemaAsync, now succeeds; ingest proceeds normally)
  │   chat memory backfill: handled by future memory_pending_embeddings worker
  │   (out of scope for this spec)
```

## 8. Error handling

| Failure mode | Detection | Response |
|---|---|---|
| `LLM_CHAT_URL` unset, falls back to `LMSTUDIO_URL` | DI registration | Log one-time warning, continue |
| `LLM_EMBEDDING_URL` unset | DI registration | Fall back to `LLM_CHAT_URL`, log warning |
| Embedding dimension mismatch (e.g. server returned 768 floats but config says 4096) | `OpenAiCompatibleEmbeddingClient.EmbedBatchAsync` | Throw `InvalidOperationException`; HTTP handler translates to 503 with `{ error: "embedding_dim_mismatch", expected: 4096, got: 768 }` |
| Embedding server unreachable | `HttpRequestException` from `_http.SendAsync` | 503 with `{ error: "embedding_server_unreachable", url: "...", inner: "..." }` |
| Embedding server returns 5xx mid-batch | `EnsureSuccessStatusCode` throws | Caller marks batch posts as `Failed`; run continues to next batch |
| Arango unreachable | `HttpRequestException` from `_rawHttp.SendAsync` | 503 with `{ error: "arango_unreachable" }` |
| Arango write conflict on `posts/{_key}` (concurrent reindex) | `ApiErrorException` with `_rev` conflict | Retry once with fresh GET, then mark `Failed` |
| `EmbeddingConfigMismatchException` on first schema check (lazy bootstrap) | `EnsureSchemaAsync` | Bridge process is up; the triggering endpoint returns 503 with remediation hint. `POST /api/admin/migrate-embeddings` is exempt and remains reachable |
| `/api/admin/migrate-embeddings` called without `confirm` token | Handler validation | 400 with `{ error: "missing_or_invalid_confirm", accepted: ["preserve-and-reembed", "wipe-and-reset"] }` |
| `/api/admin/migrate-embeddings` called with no mismatch | Handler | 200 with no-op `MigrationResult`, `previous == current` |
| `/api/memory/search` with no matches | Empty AQL result | 200 with `{ results: [] }` |
| `/api/memory/search` with malformed JSON | Body binder | 400 with `{ error: "invalid_request_body", details: "..." }` |
| `/api/memory/search` with `k > 50` or `k < 1` | Validation | 400 with `{ error: "k_out_of_range", min: 1, max: 50 }` |
| `/api/admin/reindex-posts` with duplicate `(collection, slug)` in payload | Validation | 400 with `{ error: "duplicate_slug", slug: "...", collection: "..." }` |
| Post body too large (> 1MB stripped) | Handler validation | 400 with `{ error: "body_too_large", slug: "...", bytes: ... }` — caps to prevent accidental embedding of binary content slipped into MDX |
| Per-post failure during a multi-post reindex | Handler catches per-post | Run continues. Failed posts appear in response `posts[]` with `summary: "failed"` or `body: "failed"` and a `failure_reason` string |

## 9. Testing

Two gates:
- `ARANGO_TEST_RUN=1` — gates Arango-dependent tests (existing).
- `LLM_TEST_RUN=1` — gates tests that hit either llama.cpp server (chat or embed). New.

CI runs neither (unit-only, fast). Local dev or self-hosted runners set one or both depending on available services.

| ID | Layer | Test | Gates | Notes |
|---|---|---|---|---|
| T1 | Unit | `OpenAiCompatibleEmbeddingClientTests` rename — existing 3 tests pass under new name | none | Pure rename, no behavior change |
| T2 | Unit | `MemoryStoreTests.UpsertPostAsync_writesTwoDocs_oneSummaryOneBody` | ARANGO | Verifies two docs created with correct `_key` shape |
| T3 | Unit | `MemoryStoreTests.UpsertPostAsync_hashMatch_skipsEmbed` | ARANGO | Stub embedding client asserts not called when hash matches |
| T4 | Unit | `MemoryStoreTests.UpsertPostAsync_force_reembedsEvenOnHashMatch` | ARANGO | Same setup, `force=true`, embedding client called |
| T5 | Unit | `MemoryStoreTests.DeleteStalePostsAsync_removesPostsNotInCurrentSet` | ARANGO | Seed 4 posts, pass currentSet of 2 → 2 removed |
| T6 | Unit | `MemoryStoreTests.SearchAsync_dedupsBySlug_returnsBestKindPerPost` | ARANGO | Hand-crafted embeddings: same slug summary scores 0.8, body scores 0.6 → returns summary only |
| T7 | Unit | `MemoryStoreTests.SearchAsync_emptyCollection_returnsEmpty` | ARANGO | Smoke |
| T8 | Unit | `MemoryStoreTests.SearchAsync_filtersOutPendingStatus` | ARANGO | Doc with `status=pending_embedding` excluded |
| T9 | Unit | `MemoryStoreTests.EnsureSchemaAsync_writesEmbeddingConfigOnFirstRun` | ARANGO | Empty `memory_meta` collection → config doc written |
| T10 | Unit | `MemoryStoreTests.EnsureSchemaAsync_throwsOnEmbeddingConfigMismatch` | ARANGO | Pre-seed mismatched config → throws |
| T11 | Unit | `MemoryStoreTests.EnsureSchemaAsync_noopOnMatch` | ARANGO | Pre-seed matching config → no throw, no write |
| T12 | Unit | `MemoryStoreTests.MigrateEmbeddingsAsync_preservesText_clearsEmbedding` | ARANGO | Seed doc with text + embedding → after migration, text intact, embedding null, status pending |
| T13 | Unit | `MemoryStoreTests.MigrateEmbeddingsAsync_enqueuesPendingEmbeddings` | ARANGO | Verify queue contents after migration |
| T14 | Unit | `MemoryStoreTests.MigrateEmbeddingsAsync_dropsVectorIndexes` | ARANGO | Pre-create vector index, migrate, verify dropped |
| T15 | Unit | `MemoryStoreTests.MigrateEmbeddingsAsync_rejectsInvalidConfirm` | ARANGO | `confirm: "nope"` → throws |
| T16 | Unit | `MemoryStoreTests.MigrateEmbeddingsAsync_wipeAndReset_destroysDocs` | ARANGO | `confirm: "wipe-and-reset"` → docs gone |
| T17 | Integration | `IngestionEndpointTests.PostReindexPosts_emptyDb_writes26Docs` | ARANGO, LLM | Real qwen3 server, real Arango, 13-post fixture |
| T18 | Integration | `IngestionEndpointTests.PostReindexPosts_secondCall_allCacheHits` | ARANGO, LLM | Verify hash-based caching round-trips |
| T19 | Integration | `IngestionEndpointTests.PostReindexPosts_force_rewritesEvenOnHashMatch` | ARANGO, LLM | `force=true` ignores cache |
| T20 | Integration | `IngestionEndpointTests.PostReindexPosts_removedPost_deletesStale` | ARANGO, LLM | Ingest 13, then ingest 12, verify 2 docs deleted (summary + body of removed slug) |
| T21 | Integration | `SearchEndpointTests.PostMemorySearch_returnsCastIronPanPost_forCastIronQuery` | ARANGO, LLM | Content-fixture, end-to-end retrieval quality smoke |
| T22 | Integration | `SearchEndpointTests.PostMemorySearch_kEqualsOne_returnsSingleHit` | ARANGO, LLM | Sanity |
| T23 | Integration | `SearchEndpointTests.PostMemorySearch_emptyPostsCollection_returnsEmpty` | ARANGO, LLM | Empty DB → empty results |
| T24 | Integration | `MigrationEndpointTests.PostMigrateEmbeddings_noMismatch_isNoop` | ARANGO | Idempotency check |
| T25 | Integration | `MigrationEndpointTests.PostMigrateEmbeddings_actualMismatch_updatesConfig` | ARANGO | Pre-seed old config, run, verify new config persisted |

The test fixture for T21 uses a small representative content corpus committed to `dais-bridge.tests/fixtures/content/` so the retrieval-quality test isn't dependent on whatever's currently in `src/content/`.

## 10. Open gaps and follow-ups

These are flagged here so they don't get lost. They are **not** in scope for this spec but should be picked up before this work is considered production-ready.

1. **No auth on the new HTTP endpoints.** Both `/api/admin/*` and `/api/memory/search` are unauthenticated. The bridge is bound to local network only, but the threat model when SignalR hubs start enforcing tenant boundaries (Phase 11 B4) requires the HTTP surface either: (a) be gated behind admin auth, (b) require the SignalR hub's tenant token, or (c) be removed in favor of the kernel function. Decision deferred to whoever lands B4.
2. **Pending-embeddings worker.** The migration endpoint enqueues docs for re-embedding but nothing drains the queue for chat memory collections. For posts, `rag:reindex` is the drain. For decisions/observations/facts/summaries, a background `BackgroundService` will be needed (Phase 11 C or D scope).
3. **Multi-DB pattern for future apps.** When a budgeting or inventory feature lands, each gets its own database (`darbees_budget`, `darbees_inventory`). The bridge's current single-DB connection (`ARANGO_DATABASE`) needs to grow to a multi-DB resolver. Not in scope here.
4. **Snippet quality.** The current snippet rule (first 280 chars of stored text, or full `aiSummary`) is utilitarian. A future improvement is highlight-aware snippet generation — find the most semantically relevant chunk of the matched text relative to the query. Requires either re-embedding chunks at query time (expensive) or a TF-IDF fallback (cheap, decent).
5. **Vector index threshold.** As `memory_posts` grows past a few hundred docs, brute-force AQL gets noticeably slower. Phase 11's lazy IVF vector index path (`EnsureVectorIndexAsync`) is available for `memory_posts` — just needs `docCount >= vectorNLists` (default 100). Trigger condition documented for future-us.
6. **`scripts/rag-search.mjs` polish.** Listed as optional in §5.9. If it ships, deciding the UX of "search with filters" (e.g., collection, tag, date range) is a follow-up.
7. **Renaming touches Phase 11 docs.** `RESUME-graph-backed-rag.md`, `2026-05-09-graph-backed-rag-design.md`, `2026-05-09-graph-backed-rag.md`, and `TODO-phase11.md` all reference `LmStudioEmbeddingClient`, `nomic-embed-text-v1.5`, and 768-dim. Patching every reference is in scope; archival vs. inline-edit is a judgment call for whoever lands this. Default: inline-edit with a 2026-05-16 update note at the top of each affected doc.

## 11. Decisions log (for future-us)

| Decision | Rejected alternative | Reason |
|---|---|---|
| Posts as a new `MemoryKind` inside existing `MemoryStore` | Separate `ContentStore` class | User explicitly chose "layered on Phase 11" framing. Single write path, single schema bootstrap, single vector-index lifecycle. |
| Two embeddings per post (summary + body) | One embedding (whole-post); paragraph chunks | Whole-post loses recall on detail queries; chunks add storage and dedup complexity disproportionate to a 13-post corpus. Two-vector compromise leverages the `llmFields` content (aiSummary, keyTakeaways, faq) already populated by `geo-fill`. |
| HTTP endpoint first, kernel plugin later | Plugin-only; HTTP + plugin simultaneously | Plugin requires Phase 11 B2-B5 (MemoryPlugin, tenant accessor, hub wiring). HTTP is independently testable and is the data primitive the plugin will eventually wrap. |
| Bridge admin endpoint + npm runner | Pure JS script bypassing bridge; C# CLI mode | Single write path through `MemoryStore`. Dogfoods the production code. NPM trigger keeps it consistent with `geo:fill` / `related:rebuild` authoring tooling. |
| `qwen3-embedding-8b` (4096-dim) | `nomic-embed-text-v1.5` (768-dim) | Already running, already validated. Sovereign-stack consistency (Qwen3 family for sub-LLM tasks). Embedding quality matters more on small corpora. Hardware (Ryzen AI Max+ 395, unified memory) has headroom to run Maverick + Qwen3-Embedding-8B simultaneously. |
| Dedicated `memory_meta` collection | Magic `_key` in `entities` | System metadata never belongs in user-data collections. Pattern scales cleanly when future apps land their own DBs. |
| Preserve data, clear embeddings, enqueue for re-embed | Truncate all docs on migration | Canonical text on chat memory docs is user-generated. Destroying on model swap is indefensible once real users have populated memory. Posts can be losslessly re-ingested either way, so the safer pattern wins. |
| Brute-force `COSINE_SIMILARITY` AQL, no vector index for `memory_posts` yet | Lower `Memory:VectorNLists` to 4 to force index training | IVF quality at very low nLists is degraded; would touch this twice as corpus grows. Brute force at 26 vectors is sub-ms. |
| Two test gates (`ARANGO_TEST_RUN`, `LLM_TEST_RUN`) | Single gate; per-purpose gates (embed/chat) | Extends existing single-gate convention. One LLM gate is sufficient at this scale; splitting adds friction without recall. |
| Rename `LmStudio*` → `OpenAiCompatible*` and `LMSTUDIO_URL` → `LLM_CHAT_URL` in this pass | Defer rename to Phase 11 B-track | Rename is a small diff with high readability gain. New code shouldn't be written with misleading names. Back-compat env-var fallback covers existing `.env` files. |
| Migration endpoint registered before schema check | Schema check at app build time | Without this, the bridge is unrecoverable on mismatch without manual Arango surgery. The endpoint must be reachable to perform the remediation it documents. |

---

**End of spec.**
