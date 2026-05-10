# Graph-Backed RAG for DAIS Bridge — Design

**Date:** 2026-05-09
**Status:** Draft, awaiting user review
**Owner:** Patrick Darbee
**Phase:** 10 (Sovereign Gateway / DAIS Bridge), follow-on to commit `71964eb`
**Prerequisite reading:** [HANDOFF.md § Phase 10](../../../HANDOFF.md), [docs/plans/2026-05-08-sovereign-gateway-design.md](../../plans/2026-05-08-sovereign-gateway-design.md)

---

## 1. Goal

Wire **ArangoDB as a Semantic Kernel memory store** so the DAIS Bridge agent recalls architectural decisions, plugin observations, and conversation summaries across sessions and across kernels (kid-safe and admin), with strict tenant isolation and zero cloud egress.

This is "graph-backed" RAG: memories are connected to extracted entities (people, files, tags, concepts), and recall combines graph expansion from query entities with vector similarity reranking.

## 2. Non-goals

- Multi-modal memory (images, audio). Text only in v1.
- Cloud-hosted embedding services. LM Studio local only.
- Memory-aware planning (`Plan` returns inform-by-recall). Out of scope; reasoning loop unchanged.
- Memory garbage collection / TTL. Manual delete only in v1.
- Cross-device sync. Single-machine deployment.
- Replacing the existing `WhiteboardProvider` short-term layer; we use the built-in implementation.

## 3. Architecture

### 3.1 Three memory layers (Microsoft SK 1.75 layered model)

| Layer | Lifetime | Trigger | SK abstraction | Implementation |
|---|---|---|---|---|
| Short-term | Per-thread | Auto, in-thread | `WhiteboardProvider` (built-in) | No code change — registered in hub session setup |
| Long-term | Cross-session | Auto, extracted post-turn | `AIContextProvider` | New `DarbeesContextProvider` (Mem0Provider analog), backed by `MemoryStore` |
| Explicit RAG | Cross-session | Explicit upsert | `ContentStorageService` pattern | New `MemoryPlugin` kernel functions, backed by `MemoryStore` |

Both backed-by-Arango layers share one `MemoryStore` substrate. Built-in `WhiteboardProvider` is independent.

### 3.2 Module layout

```
dais-bridge/
├── Memory/                              ← NEW namespace
│   ├── IEmbeddingClient.cs
│   ├── LmStudioEmbeddingClient.cs       ← POSTs to /v1/embeddings
│   ├── MemoryStore.cs                   ← all Arango I/O; query helpers; bind-vars only
│   ├── MemoryRecallEngine.cs            ← hybrid recall: entity extract → graph expand → vector rerank
│   └── Models/
│       ├── MemoryItem.cs                ← shared base record
│       ├── MemoryEdge.cs
│       ├── RecallResult.cs              ← items + per-item cosine, proximity, path
│       └── WriteResult.cs               ← { Id, Completed, Queued }
├── Plugins/
│   ├── MemoryPlugin.cs                  ← REPLACES ArangoPlugin
│   └── ArangoPlugin.cs                  ← DELETED
└── Providers/
    └── DarbeesContextProvider.cs        ← AIContextProvider impl, auto-extracts long-term facts
```

### 3.3 Service registration (Program.cs)

```csharp
builder.Services.AddSingleton<IEmbeddingClient>(_ =>
    new LmStudioEmbeddingClient(lmStudioUrl, embeddingModelId));

builder.Services.AddSingleton<MemoryStore>(sp =>
    new MemoryStore(arangoUrl, arangoDb, arangoUser, arangoPass,
                    sp.GetRequiredService<IEmbeddingClient>()));

builder.Services.AddSingleton<MemoryRecallEngine>();

builder.Services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
```

Both `kernel-kidsafe` and `kernel-admin` register `MemoryPlugin` referencing the same singleton `MemoryStore`. ArangoPlugin is removed from both.

### 3.4 MemoryPlugin kernel function surface

| Function | Args | Returns | Kernels |
|---|---|---|---|
| `RememberDecision` | `subject`, `chose`, `because`, `alternatives` | `WriteResult` | both |
| `RememberObservation` | `kind`, `payload` | `WriteResult` | both |
| `LinkMemory` | `fromId`, `toId`, `edgeKind`, `weight?` | edge `_key` | both |
| `Recall` | `query`, `topK?` (8), `expandHops?` (1) | `RecallResult` | both |
| `ListByTenant` | `kindFilter?`, `since?` | `MemoryItem[]` | admin-only registration |

Tenant identifier is **never an LLM-bound parameter**. `MemoryPlugin` reads from `ITenantContextAccessor` (DI-injected), which is set by the SignalR hub at connection time.

## 4. Schema (ArangoDB)

Single database (`darbees_knowledge`), single set of collections, `tenant_id` field on every document and edge.

```
memory_decisions     (document)
  _key, text, embedding[768], tenant_id, chose, because, alternatives[],
  status ('ready' | 'pending_embedding'), created_at, updated_at

memory_observations  (document)
  _key, text, embedding[768], tenant_id, source, payload,
  status, created_at, updated_at

memory_facts         (document)
  _key, text, embedding[768], tenant_id, source_thread,
  status, created_at, updated_at

memory_summaries     (document)
  _key, text, embedding[768], tenant_id, thread_id,
  status, created_at, updated_at

memory_entities      (document, NO embedding)
  _key, canonical_name, aliases[], type ('person'|'file'|'tag'|'concept'),
  tenant_id, created_at

memory_edges         (edge)
  _from, _to, kind ('mentions'|'depends-on'|'supersedes'|'tagged'|'about-file'),
  weight (float, default 1.0), tenant_id, created_at

memory_pending_embeddings  (document, queue)
  _key, target_collection, target_key, attempts, last_error, queued_at
```

### 4.1 Indexes

On each content collection (`decisions`, `observations`, `facts`, `summaries`):
- Vector index: `{ type: "vector", fields: ["embedding"], params: { dimension: 768, metric: "cosine", nLists: 100 } }`
- Persistent: `{ type: "persistent", fields: ["tenant_id", "status", "created_at"] }`

On `memory_entities`:
- Persistent: `{ type: "persistent", fields: ["tenant_id", "canonical_name"] }`
- Persistent: `{ type: "persistent", fields: ["tenant_id", "aliases[*]"] }`

On `memory_edges`:
- Persistent: `{ type: "persistent", fields: ["tenant_id", "kind"] }`

### 4.2 Embedding model

Default: `nomic-embed-text-v1.5` (768 dimensions). Configured via `appsettings.json`:

```json
"AI": {
  "LMStudioUrl": "http://localhost:1234/v1",
  "ModelId": "local-model",
  "EmbeddingModelId": "nomic-embed-text-v1.5",
  "EmbeddingDimension": 768
}
```

Switching the embedding model requires re-embedding existing items. `MemoryStore.ReembedAllAsync(tenantId)` is provided as a deliberate operation; not invoked automatically.

## 5. Hybrid recall algorithm

```
Recall(query, tenantId, topK=8, expandHops=1):

  1. ExtractEntities(query, tenantId):
       a. Substring-match against memory_entities (canonical_name + aliases)
          AQL: FOR e IN memory_entities
                 FILTER e.tenant_id == @tenantId
                 FILTER CONTAINS(LOWER(@query), LOWER(e.canonical_name))
                     OR LENGTH(FOR a IN e.aliases FILTER CONTAINS(LOWER(@query), LOWER(a)) RETURN 1) > 0
                 RETURN e._id
       b. If empty, fall back to NER via LM Studio chat completion (small prompt)
       Returns: entityIds[]

  2. GraphExpand(entityIds, tenantId, expandHops):
       AQL: FOR e IN @entityIds
              FOR v, edge, p IN 1..@hops ANY e memory_edges
                FILTER v.tenant_id == @tenantId
                FILTER PARSE_IDENTIFIER(v._id).collection != 'memory_entities'
                RETURN DISTINCT { item: v, hops: LENGTH(p.edges) }
       Returns: graphCandidates[]

  3. VectorTopK(query, tenantId, 2*topK):
       queryEmbedding = LmStudioEmbeddingClient.Embed(query)
       UNION across content collections, ordered by APPROX_NEAR_COSINE:
       AQL: FOR i IN UNION(
              (FOR d IN memory_decisions
                 FILTER d.tenant_id == @tenantId AND d.status == 'ready'
                 RETURN MERGE(d, { _kind: 'decision' })),
              (FOR o IN memory_observations
                 FILTER o.tenant_id == @tenantId AND o.status == 'ready'
                 RETURN MERGE(o, { _kind: 'observation' })),
              (FOR f IN memory_facts ...),
              (FOR s IN memory_summaries ...))
            SORT APPROX_NEAR_COSINE(i.embedding, @queryEmb) DESC
            LIMIT @limit
            RETURN { item: i, similarity: APPROX_NEAR_COSINE(i.embedding, @queryEmb) }
       Returns: vectorCandidates[]

  4. Score & merge:
       combined = graphCandidates ∪ vectorCandidates  (by item._key)
       for c in combined:
         c.cosine = c.similarity ?? cosine(queryEmbedding, c.item.embedding)
         c.proximity = 1.0 / (1 + (c.hops ?? Infinity))
         c.score = α * c.cosine + β * c.proximity   (α=0.7, β=0.3, configurable)
       return top-K by score
```

`RecallResult` includes per-item `cosine`, `proximity`, and the `path` (entity → ... → item) so the LLM can cite *why* a memory was retrieved.

## 6. Write paths

### 6.1 Explicit (RememberDecision / RememberObservation)

```
UpsertContent(collection, document, tenantId):
  1. Validate tenantId from TenantContext
  2. Insert into <collection> with status='pending_embedding', no embedding field
  3. Try IEmbeddingClient.Embed(text):
       Success: UPDATE doc with embedding[], status='ready'
       Failure: insert {target_collection, target_key, attempts:0} into memory_pending_embeddings
  4. Return WriteResult { Id, Completed: status=='ready', Queued: status=='pending_embedding' }
```

### 6.2 Auto (DarbeesContextProvider)

`AIContextProvider.OnNewMessageAsync` runs after each agent invocation:
1. Pull last N messages from `AgentThread` history
2. Prompt LM Studio (small chat call) with: "Extract any new persistent facts from this exchange. Return JSON: `{ facts: [{ text, type, mentioned_entities[] }] }`"
3. For each extracted fact: `MemoryStore.UpsertFact(text, tenantId, mentioned_entities)`
4. For each mentioned entity not already in `memory_entities`: create entity node + `mentions` edge from fact to entity

Tenant inherits from `TenantContext.Current` which the hub set at connection.

### 6.3 Pending-embedding retry

`PendingEmbeddingsService : BackgroundService`:
- Every 30s, query `memory_pending_embeddings` ordered by `queued_at ASC`, take first 10
- For each: try `IEmbeddingClient.Embed`, on success UPDATE target doc + remove queue entry, on failure increment `attempts`, after 5 attempts move to a dead-letter collection with `last_error`

## 7. Tenant isolation (defense in depth)

| Layer | Mechanism |
|---|---|
| Kernel split | `kernel-kidsafe` / `kernel-admin` already separate; preserved unchanged |
| Plugin context | `MemoryPlugin` reads `tenantId` from `ITenantContextAccessor` (DI), not from kernel function parameters |
| Hub injection | `KidSafeHub.OnConnectedAsync` resolves authenticated kid identity → `TenantContext.Current = "kid:<id>"`. `ParentHub` resolves to `"admin"`. |
| Query construction | All AQL composed inside `MemoryStore` with bind-vars; zero string interpolation; no raw-AQL kernel function exposed |
| Field replication | `tenant_id` on every document and every edge; persistent indexes include `tenant_id` as the leading column |
| Admin cross-tenant | Parent admin views call separate `AdminListMemories(tenantId)` registered only on `kernel-admin`. Kid-safe kernel never gets that surface |

The existing `dais-bridge/Models/TenantContext.cs` (commit `457efd5`) is extended; `ITenantContextAccessor` is the new DI surface.

## 8. Error handling

| Failure | Write path | Recall path |
|---|---|---|
| LM Studio down / embedding model unloaded | Queue to `memory_pending_embeddings`; `BackgroundService` retries every 30s | Skip vector step; return graph-only candidates with `cosine=null`; log degradation |
| ArangoDB unreachable | Fail loudly (`InvalidOperationException`) — operator-fixable | Fail loudly. Do not queue recall — stale recall is worse than no recall |
| Vector dim mismatch (model swap without re-embed) | Fail fast at startup with explicit message; require `ReembedAllAsync` | Caught at startup |
| Empty/unknown tenant in `TenantContext` | Reject with `InvalidOperationException("Tenant context not set")` | Same |
| AQL injection surface | None — all AQL composed inside `MemoryStore` with bind-vars; no raw-AQL kernel function | Same |
| Recall query embedding cost | In-memory cache (query-hash → embedding, 5-min TTL) behind `Memory:CacheQueryEmbeddings` flag | n/a |

## 9. Testing

### 9.1 Unit tests (`dais-bridge.tests/Memory/`)

- `MemoryStoreTests` — mocked `IEmbeddingClient` and Arango client. Cover: tenant_id replication on edges, embedding-batching, status='pending' insert/update flow, AQL bind-var assertions
- `MemoryRecallEngineTests` — mocked `MemoryStore`. Cover: scoring formula, top-K selection, graph-disconnected items getting `proximity=0`, hop-distance correctness, `α`/`β` config wiring
- `LmStudioEmbeddingClientTests` — `HttpClient` test handler. Cover: request body shape (model + input array), response parse, timeout behavior, error mapping
- `MemoryPluginTests` — kernel function arg validation, tenant rejection when `TenantContext` unset, edge auto-creation on entity mentions
- `DarbeesContextProviderTests` — synthetic conversation → fact extraction → tenant inheritance verified

### 9.2 Integration tests (`dais-bridge.tests/Integration/Memory/`)

Skipped if `ARANGO_TEST_URL` env not set (matches existing pattern).

- Schema migration end-to-end (idempotent)
- Write decision → graph expand → recall round trip
- **Cross-tenant isolation**: kid-tenant query with admin-tenant cosine-similar facts returns zero admin items. Test data deliberately constructed so cosine similarity is high
- Vector index ranking on synthetic data
- Pending-embedding retry: stub embedding client to fail then succeed, verify queue drains
- Two-phase write: write succeeds when embedding client unavailable; status transitions to 'ready' after retry

### 9.3 CI

`.github/workflows/ci.yml` `dotnet test` job extended with an ArangoDB service container. Integration tests gated behind a `[Trait("Category", "Integration")]` xUnit attribute and run only when `ARANGO_TEST_URL` is set (CI sets it, local devs may opt out).

### 9.4 Smoke

No change to existing Playwright suite — Memory layer is backend-only.

## 10. Configuration changes

### 10.1 `appsettings.json` additions

```json
{
  "AI": {
    "EmbeddingModelId": "nomic-embed-text-v1.5",
    "EmbeddingDimension": 768
  },
  "Memory": {
    "RecallAlpha": 0.7,
    "RecallBeta": 0.3,
    "DefaultTopK": 8,
    "DefaultExpandHops": 1,
    "CacheQueryEmbeddings": true,
    "QueryEmbeddingTtlSeconds": 300,
    "PendingEmbeddingRetryIntervalSeconds": 30,
    "PendingEmbeddingMaxAttempts": 5
  }
}
```

### 10.2 ArangoDB version requirement

ArangoDB ≥ 4.0 (vector index is a first-class index type in 4.x, alongside persistent / geo / ttl / inverted). Document the requirement in `README.md` and `HANDOFF.md`. Local dev `docker-compose.yml` uses `arangodb:4` or later. The exact vector-index creation body shape and AQL similarity function name (`APPROX_NEAR_COSINE` was the 3.x experimental name) must be verified against a running v4 instance during Task A4 — query `/_api/version` and the v4 index docs via Context7 before pinning the body in `MemoryStore.EnsureVectorIndexAsync`.

## 11. Migration / rollout

Single-step deployment, no production data to migrate (memory is new):

1. Deploy `MemoryStore.EnsureSchemaAsync` runs at app startup; idempotent. Creates collections, indexes, vector index dimension.
2. Replace `ArangoPlugin` registrations with `MemoryPlugin` in `Program.cs`.
3. Delete `dais-bridge/Plugins/ArangoPlugin.cs`.
4. Update `dais-bridge.tests` references (any tests that referenced `ArangoPlugin` directly are reworked).

Phased implementation order (per `writing-plans` follow-up):

1. **Phase A — substrate**: `Memory/Models/`, `IEmbeddingClient`, `LmStudioEmbeddingClient`, `MemoryStore` (schema migration + write paths only, no recall yet)
2. **Phase B — explicit layer**: `MemoryPlugin` with `RememberDecision`/`RememberObservation`/`LinkMemory`. Replaces `ArangoPlugin` in `Program.cs`. Unit tests + integration tests for writes + cross-tenant isolation
3. **Phase C — recall**: `MemoryRecallEngine` with hybrid algorithm; `MemoryPlugin.Recall`. Unit + integration tests
4. **Phase D — auto layer**: `DarbeesContextProvider` AIContextProvider; SignalR hubs add it to `AgentThread.AIContextProviders`. `WhiteboardProvider` wired in same step
5. **Phase E — pending-embedding retry**: `BackgroundService` + dead-letter collection
6. **Phase F — admin surface**: `AdminListMemories` registered on `kernel-admin` only; `ParentHub` exposes via SignalR method
7. **Phase G — docs + CI**: HANDOFF.md update (Phase 11 entry), README ArangoDB version note, CI ArangoDB service container

## 12. Open questions deferred to implementation

These are intentionally not decided here; surface in the implementation plan or first review:

- Exact entity-extraction prompt wording for the auto layer
- Whether `Recall` returns truncated text (preview) or full text in `RecallResult`
- LM Studio request batching for many small embeddings (keep 1-per-request first; revisit if hot)
- Whether kid-safe `Recall` exposes `path` (entity citations) or hides it for simplicity in the kid UI
- ArangoDB experimental-vector-index AQL flag (`OPTIONS { useExperimentalVectorIndex: true }`) on the version we target
- Exact `AIContextProvider` override method name in SK 1.75 (`OnMessageAddedAsync` vs. `OnAIInvocationAsync` vs. `OnNewMessageAsync`); section 6.2 uses placeholder pending live-API check
- DI lifetime for `ITenantContextAccessor` (`AddScoped` is the current default; SignalR hub-method scopes may favor a hybrid `IHubCallerContext`-keyed dictionary — confirm during Phase B)

## 13. Anti-patterns to avoid (incorporated from HANDOFF.md)

- **No raw-AQL kernel function** (#9 in HANDOFF anti-patterns surfaces injection risk; we remove the surface entirely)
- **Tenant ID is never an LLM-bound parameter** (extends the kernel-split sovereign trust boundary established in commit `d1a1cb0`)
- **Embedding queues, not blocks**: write succeeds even when LM Studio is down, recall degrades gracefully — same philosophy as Microsoft Kernel Memory's two-phase write
- **All edits to `Program.cs` and `MemoryStore.cs` sequential**, never parallel `Edit` calls (HANDOFF anti-pattern #5)

## 14. References

- Microsoft Semantic Kernel agent-memory guidance: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/agent-memory
- Microsoft Kernel Memory `ContentStorageService` two-phase write pattern (Context7: `/microsoft/kernel-memory`)
- ArangoDB 4.x vector index documentation (verify exact body shape and AQL function name during implementation; the 3.x experimental form was `APPROX_NEAR_COSINE`)
- DAIS Bridge implementation history: [HANDOFF.md § Phase 10](../../../HANDOFF.md), commits `f0d09cc` → `71964eb`
