# Resume Guide — Phase 11: Graph-Backed RAG

> **2026-05-17 — Stack drift update:** This work was paused at task A6. Since then:
> - `LmStudioEmbeddingClient` has been renamed to `OpenAiCompatibleEmbeddingClient` (same behavior, accurate name).
> - The embedding stack switched from LM Studio + `nomic-embed-text-v1.5` (768-dim) to llama.cpp + `qwen3-embedding-8b` (4096-dim).
> - The bridge now reads `LLM_CHAT_URL` and `LLM_EMBEDDING_URL` (split). `LMSTUDIO_URL` is back-compat with a deprecation warning.
> - `EnsureSchemaAsync` is now invoked lazily (first-use) rather than at startup, so the new `POST /api/admin/migrate-embeddings` endpoint stays reachable during config-mismatch states.
> - Posts are now stored as `MemoryKind.Post` in a `memory_posts` collection. See [`docs/superpowers/specs/2026-05-16-content-rag-design.md`](specs/2026-05-16-content-rag-design.md).
> - `MemoryPlugin` (kernel functions `RememberDecision`/`RememberObservation`/`LinkMemory`) was added in parallel via commit `131f509`, advancing Phase 11 B2.
>
> When resuming Phase 11 task B3 and beyond, the above is the current state. `MemoryStore` and `IEmbeddingClient` references in the original spec/plan below are correct in spirit; just substitute the new class name and config keys.

> **Purpose:** Everything you (or a future agent session) need to pick this work back up cold without re-deriving context.

**Last updated:** 2026-05-12 (later)
**Branch:** `feature/graph-backed-rag` (off `master`)
**Status:** Phases A1, A2, A3, B1 complete; Phases A4 → G2 remaining (except B1); A4 has been redesigned (lazy vector index) based on the 2026-05-12 smoke test. Local ArangoDB 3.12.x must be started with `--vector-index`.

---

## Quick orientation

| Artifact | Location |
|---|---|
| Design spec | [`docs/superpowers/specs/2026-05-09-graph-backed-rag-design.md`](specs/2026-05-09-graph-backed-rag-design.md) |
| Implementation plan | [`docs/superpowers/plans/2026-05-09-graph-backed-rag.md`](plans/2026-05-09-graph-backed-rag.md) |
| This guide | [`docs/superpowers/RESUME-graph-backed-rag.md`](RESUME-graph-backed-rag.md) |
| Memory layer code | `dais-bridge/Memory/Models/` (created in A1) |

The spec answers WHY and WHAT. The plan answers HOW. This guide adds: WHAT WAS DONE, WHAT'S BLOCKING, and WHAT THE NEXT SESSION NEEDS TO KNOW.

---

## What was done in the 2026-05-09 session

### Brainstorming → spec → plan

| Commit | Branch | Description |
|---|---|---|
| `621bfee` | master | Spec committed — layered SK 1.75 memory model, single-DB normalized schema, hybrid recall |
| `611fd38` | master | Initial plan committed — 7 phases (A–G), ~25 tasks, TDD step-by-step |
| `0117708` | master | Spec + plan retargeted to ArangoDB v4 — **later reverted** (see 2026-05-12 session notes): v4 is in development and not yet released; 3.12.x is the current stable and has experimental vector index support |
| `b58e89f` | `feature/graph-backed-rag` | Plan fix: LM Studio now requires `Authorization: Bearer <token>`; switched to `arangodb:latest` tag |

### Phase A1 — Memory model records (DONE)

| Commit | Description |
|---|---|
| `39d1d5e` | Created 5 record/enum files in `dais-bridge/Memory/Models/` |
| `8281b8e` | Removed inline comments per CLAUDE.md ("default to no comments") |
| `2b737f0` | Extracted `ScoredMemoryItem` to its own file per code review |

**Files in tree (after A1):**

```
dais-bridge/Memory/Models/
├── MemoryKind.cs         — enum + MemoryCollections static (constants + ForKind switch)
├── MemoryItem.cs         — record carrying any kind of memory content
├── MemoryEdge.cs         — edge record (from, to, kind, weight, tenant)
├── ScoredMemoryItem.cs   — recall result item with cosine/proximity/score/path
├── RecallResult.cs       — items + extracted entity ids
└── WriteResult.cs        — { Id, Completed, Queued } with Ready/Pending factories
```

**Build:** clean, 0 warnings, 0 errors on .NET 9.

### Subagent workflow gotchas surfaced

- **`SendMessage` tool is not available** in this environment. The skill assumes it for "fix loop with same subagent", but in practice we dispatched fresh subagents per fix. Stateless re-dispatch works because all state is on disk.
- **Spec reviewer prompts must match the plan's code blocks exactly.** Initial spec reviewer flagged inline comments that were specified verbatim in the plan. Lesson: when writing the plan, remove any comments from code blocks that wouldn't survive CLAUDE.md review.
- **Code reviewer flagged colocation of `ScoredMemoryItem` with `RecallResult`.** Going forward, prefer one-type-per-file even when the plan groups multiple types in a single code block.

---

## What was done in the 2026-05-10 session

### Phases A2 + A3 — Embedding client (DONE)

| Commit | Description |
|---|---|
| `78c8b52` | A2: `IEmbeddingClient` interface + failing `LmStudioEmbeddingClientTests` (red via missing-type compile error) |
| `e3c45bf` | A3: `LmStudioEmbeddingClient` impl + 2 more tests (dim mismatch, batch). 3/3 PASS. |

**Files added:**

```
dais-bridge/Memory/IEmbeddingClient.cs
dais-bridge/Memory/LmStudioEmbeddingClient.cs
dais-bridge.tests/Memory/LmStudioEmbeddingClientTests.cs
```

### Plan deviation logged in commit `e3c45bf`

Dropped `using var request = new HttpRequestMessage(...)` → `var request = ...`. Reason: `HttpRequestMessage.Dispose` also disposes its `Content`. Two of the three tests inspect the request body via the captured StubHandler reference *after* `EmbedBatchAsync` returns, which threw `ObjectDisposedException` on `JsonContent`. Removing the `using` lets the request body remain readable for assertions; in production the request goes out of scope and is GC'd cleanly. **Future plan rewrites should not re-introduce `using` here.**

### Verification attempts for ArangoDB vector index docs — Context7 + WebFetch both empty

Attempted to resolve Open Questions #1 (vector index body shape) and #2 (AQL similarity function name) before A4. All three external channels failed:

| Channel | Result |
|---|---|
| Context7 `/arangodb/arangodb` (4 queries) | Corpus has the general `/_api/index` API but **no vector-specific examples**. Confirms vector indexes exist as a type; no body shape, no AQL function example. |
| `docs.arangodb.com/stable/...` | 301 redirects to `docs.arango.ai` (the docs were moved). |
| `docs.arango.ai/...` | 403 Forbidden via WebFetch — likely blocking automated user agents. |

**Conclusion:** the empirical path is the cheapest verification. When ArangoDB 3.12 is running, the first `POST /_api/index` call from `EnsureVectorIndexAsync` is itself the verification — ArangoDB returns descriptive 400s naming any invalid field. If the response says vector indexes are disabled, restart the container with `--experimental-vector-index`.

### Local infrastructure note (Windows session, 2026-05-10)

On the Windows host where the prior session ran, `docker ps` showed an already-running container holding port 8529:

```
mustang-arangodb    arangodb:3.12    Up
```

That container belongs to another project (mustangcoffee stack) and shouldn't be reused. On that machine, the workaround was to run a second container on port 8530. **This is machine-specific** — on a host where port 8529 is free, use the canonical 8529:8529 mapping per the plan's Pre-flight.

---

## What was done in the 2026-05-12 session

### Phase B1 — TenantContext + ITenantContextAccessor (DONE)

| Commit | Description |
|---|---|
| `4f600e3` | B1: `TenantContext` moved to `Darbee.Gateway.Models` (sealed, init-only props, `Admin`/`ForKid` factories). `ITenantContextAccessor` interface with `Required` default member. `TenantContextAccessor` AsyncLocal-backed. 4/4 unit tests pass. |

**Plan deviation logged in commit `4f600e3`:** the plan's tests use `var acc = new TenantContextAccessor()`, which doesn't compile — `Required` is a default interface member and is only callable via the interface type. Tests now use `ITenantContextAccessor acc = new TenantContextAccessor()`, which also mirrors production usage (DI injects the interface). **Future plan rewrites should specify the interface-typed variable explicitly.**

### Docs correction — ArangoDB v4 reversal

The 2026-05-09 commit `0117708` retargeted the spec and plan to ArangoDB v4 based on the assumption that v4 was available with first-class vector index. **This was wrong**: v4 is in development and not yet generally available. Docker Hub confirms — `arangodb:latest` aliases to the 3.12.x line, and no `3.13`, no `4.x` tag exists.

Corrections applied in this session:

- **Spec** (section 10.2 + References): re-pinned to ArangoDB 3.12.x (experimental vector index); body shape and AQL function `APPROX_NEAR_COSINE` flagged for empirical verification at A4.
- **Plan** (Tech Stack, Pre-flight, A4, Step 4 of A4 run-test fallback, G1 dependency line, README text, CI service container `arangodb:3.13` → `arangodb:3.12`, CI commit message): all v4 references retargeted to 3.12.x. `--experimental-vector-index` flag flagged as a possibility surfaced empirically.
- **Resume guide** (this file): status banner, session-history entries, A4/C3 rows, open-questions table 1+2+8, resume sequence shell commands, full task list.

The 3.x experimental form (which the plan had originally used before commit `0117708`) is now restored as the authoritative starting point: `type: "vector"`, `fields: ["embedding"]`, `params: { dimension, metric, nLists }`, AQL function `APPROX_NEAR_COSINE`.

### Linux machine state

This session ran on Linux (Fedora). `docker ps` was empty — no port collision. The Windows-specific 8530 workaround from 2026-05-10 doesn't apply here.

### Smoke test — live ArangoDB 3.12 vector index verification

Pulled `arangodb:3.12` (resolved to `3.12.9-1` enterprise), started on `0.0.0.0:8529`, and ran a manual curl smoke against `POST /_api/index` and `APPROX_NEAR_COSINE` AQL. Findings (now baked into spec §10.2 and plan A4):

- **Vector index feature is gated behind a startup flag.** `POST /_api/index` with `type: "vector"` returns `400 / errorNum 10 "vector index feature is not enabled. Run ArangoDB with --vector-index flag turned on."` without it.
- **Flag is now `--vector-index`.** `--experimental-vector-index` still works but logs: `WARNING please note that the specified option '--experimental-vector-index has been renamed to '--vector-index'`.
- **Body shape confirmed:** the plan's existing 3.x form is accepted: `type: "vector"`, `fields: ["embedding"]`, `params: { dimension: 768, metric: "cosine", nLists }`. Successful creation returns `trainingState: "ready"`, plus auto-defaulted `trainingIterations: 25` and `defaultNProbe: 1`.
- **Cold-start gotcha (big one):** vector index POST on an empty / under-trained collection returns `500 / errorNum 1555 "vector index not ready"` — **but the index entry IS persisted in `trainingState: "unusable"` state.** ArangoDB's AQL optimizer then prefers the unusable index over any subsequently created good one, causing queries to fail with the same 1555 even after data is inserted. Cleanup is required.
- **`nLists` must be ≤ document count** in the collection (with some training margin). `nLists=1` works with 1 doc; `nLists=100` fails on 1 doc with errorNum 1555.
- **AQL syntax is strict.** Calling `APPROX_NEAR_COSINE` twice in the same query (once in `SORT`, once in `RETURN`) returns `errorNum 1554` "Vector search could not be applied. Please ensure ... your query uses the correct syntax for vector search." Required pattern: `LET sim = APPROX_NEAR_COSINE(doc.embedding, @q) ... SORT sim DESC ... RETURN { sim }`. The plan's C3 AQL already uses this pattern; the **spec's section 6.3 example had the bug** (now fixed in commit corresponding to this session).
- **No `OPTIONS { useExperimentalVectorIndex: true }` clause is needed.** That was a pre-rename form; current AQL parses it as a syntax error if mis-placed and ignores it elsewhere.

### A4 redesign — lazy vector index

The smoke test showed the original A4 design (vector index creation inside `EnsureSchemaAsync`) cannot work: at app startup all collections are empty, so every vector index would be created in unusable state and pollute the index space.

A4 was rewritten:

- `EnsureSchemaAsync` now creates collections + non-vector indexes only.
- New public `EnsureVectorIndexAsync(collection)` is called from write paths (Task A5, `UpsertContentAsync` after the embedding patch succeeds).
- Idempotent: caches "usable index exists" per collection in a `ConcurrentDictionary<string, bool>` after first observation; steady-state cost is one cache check per write.
- Cleans up `trainingState != "ready"` vector indexes before retrying.
- Skips creation when `documentCount < nLists`; tries again on next write.
- `nLists` is now a constructor parameter. Configured via `Memory:VectorNLists` (default 100 in production `appsettings.json`; default 1 in integration tests).
- Three new integration tests in `MemoryStoreVectorIndexTests.cs` cover: no-op below threshold, creates usable index when threshold met, cleans up unusable indexes before retrying.

### Docs commit history (this session)

| Commit | Description |
|---|---|
| `4f600e3` | B1 (committed earlier this session) |
| `8086a4b` | v4 reversal: spec/plan/resume retargeted to 3.12.x |
| TBD | A4 redesign: lazy vector index, AQL LET fix in spec, `--vector-index` flag required in Pre-flight, `MemoryStore` constructor gains `vectorNLists`, all 18 constructor call-sites updated, `Memory:VectorNLists` config added |

---

## Environment state — verify before resuming

### LM Studio

**Status when session ended:** running, but **requires a Bearer token** (was unauthenticated in earlier sessions).

```bash
curl http://localhost:1234/v1/models
# Returns: {"error":{"message":"An LM Studio API token is required..."}}
```

**Required action before resuming:**

1. In LM Studio's developer settings, copy or generate an API token.
2. Set as environment variable (preferred) or in `appsettings.json`:

```powershell
$env:LMSTUDIO_API_KEY = "<your-token>"
```

3. Verify embeddings are reachable with the token:

```powershell
$body = @{ model = "nomic-embed-text-v1.5"; input = "hello" } | ConvertTo-Json
curl http://localhost:1234/v1/embeddings `
  -H "Content-Type: application/json" `
  -H "Authorization: Bearer $env:LMSTUDIO_API_KEY" `
  -d $body
```

Expected: JSON with a 768-element `data[0].embedding` array. If embeddings have a different dimension, update `appsettings.json` `AI:EmbeddingDimension` and re-run pre-flight.

4. Embedding model required: `nomic-embed-text-v1.5` (or compatible 768-dim model). Load it in LM Studio's server panel before starting integration work.

### ArangoDB

**Target:** ArangoDB 3.12.x (current stable; vector index is an experimental feature). v4 with first-class vector index is in development and not yet generally available — `arangodb:latest` on Docker Hub currently aliases to the 3.12.x line.

**Required action before resuming:**

ArangoDB is now part of the Phase 12 podman compose stack — bring it up with `make up`, which starts the configured `arangodb:3.12 --vector-index` container along with the LM Studio probe and the gateway. See [`docs/dev-environment.md`](../dev-environment.md).

```bash
make up
make health   # confirms ArangoDB UP at localhost:8529
```

Then set the integration-test env vars on your host shell:

```bash
export ARANGO_TEST_RUN=1
export ARANGO_TEST_URL=http://localhost:8529
export ARANGO_TEST_USER=root
export ARANGO_TEST_PASS=password
```

**ArangoDB-specific facts (still relevant when debugging):**
- The `--vector-index` startup flag is required. Without it, `POST /_api/index` with `type: "vector"` returns errorNum 10 "vector index feature is not enabled." `--experimental-vector-index` is a deprecated alias (still works, logs a rename warning). The compose file already passes this flag.
- Vector index body shape (3.12 experimental, verified 2026-05-12): `POST /_api/index` with `type: "vector"`, `fields: ["embedding"]`, `params: { dimension, metric, nLists }`.
- AQL similarity function: `APPROX_NEAR_COSINE`. Bind once via `LET sim = APPROX_NEAR_COSINE(d.embedding, @q)` and reuse — double-call is errorNum 1554.
- `nLists` must be ≤ document count or POST returns errorNum 1555 (and persists an unusable index that must be cleaned up before retrying).

If you need to run ArangoDB standalone (without compose, e.g. for debugging in isolation): `podman run -d --name arango-test -e ARANGO_ROOT_PASSWORD=password -p 8529:8529 arangodb:3.12 --vector-index`. Just make sure `make up` isn't running first or port 8529 will collide.

---

## Remaining work — annotated by phase

The plan's per-task TDD steps are authoritative. This table adds **dependencies, verification, and per-task gotchas not visible inside the plan**.

### Phase A — Substrate (DONE — all 6 tasks complete as of 2026-05-13)

| Task | Title | Service deps | Plan-deferred verification | Notes |
|---|---|---|---|---|
| A2 | `IEmbeddingClient` interface + failing test | None (stub `HttpClient` handler) | — | Unit tests use `StubHandler : HttpMessageHandler`. Real LM Studio not required for this task. |
| A3 | `LmStudioEmbeddingClient` impl | None for unit tests; LM Studio + token for manual smoke | — | The plan now includes Bearer auth in the constructor (`apiKey` param). Unit tests cover dimension mismatch and batch input shape. |
| A4 | `MemoryStore` schema (collections + persistent indexes) + lazy `EnsureVectorIndexAsync` | **ArangoDB 3.12 required, started with `--vector-index`** | — | Vector index creation is now LAZY: `EnsureSchemaAsync` does NOT create vector indexes. Write paths (A5) call `EnsureVectorIndexAsync(collection)` after embedding patch. The method is idempotent (caches per-collection), cleans up unusable indexes, skips when `documentCount < nLists`. `nLists` configurable via `Memory:VectorNLists` (default 100 prod / 1 test). All HTTP through raw `HttpClient` because `ArangoDBNetStandard` 2.0.0 has no typed vector support. |
| A5 | Write paths (UpsertDecision/Observation/Fact/Summary/Entity/Edge + ListPending) | **ArangoDB + LM Studio + token** | — | Two-phase write: insert with `status='pending_embedding'`, try embed, on success patch `status='ready'`, on failure enqueue to `memory_pending_embeddings`. The `ConstantEmbeddingClient` and `FailingEmbeddingClient` test helpers are reused by later tests — keep them `internal` not `private`. |
| A6 | Wire IEmbeddingClient + MemoryStore into Program.cs DI | None | — | `EnsureSchemaAsync` runs at startup. Adds `IHttpClientFactory` "memory" client. Reads `LMSTUDIO_API_KEY` env var first, then `AI:LMStudioApiKey` config. |

### Phase B — Explicit memory layer (5 tasks)

| Task | Title | Service deps | Plan-deferred verification | Notes |
|---|---|---|---|---|
| B1 | `TenantContext` + `ITenantContextAccessor` (AsyncLocal) | None | — | **DONE 2026-05-12 (`4f600e3`).** `TenantContext` moved to `Darbee.Gateway.Models`, made sealed with init-only props, `Admin` static + `ForKid` factory added. `ITenantContextAccessor` + AsyncLocal `TenantContextAccessor`. 4/4 unit tests pass. Plan deviation: tests type the accessor variable as `ITenantContextAccessor` (the plan's `var acc = new TenantContextAccessor()` doesn't compile because `Required` is a default interface member — accessible only via the interface type). Future plan rewrites should specify `ITenantContextAccessor acc = ...`. |
| B2 | `MemoryPlugin` kernel functions (RememberDecision/Observation/LinkMemory) | **ArangoDB** | — | Tenant ID is **not** an LLM-bound parameter — `MemoryPlugin` reads from `ITenantContextAccessor`. The plan currently shows a 2-arg constructor in B2 then expands to 3-arg in C4 (adds `MemoryRecallEngine`); test fixtures need updating in C4. |
| B3 | Replace `ArangoPlugin` with `MemoryPlugin` in Program.cs and delete the stub | None | — | Deletes `dais-bridge/Plugins/ArangoPlugin.cs` and `dais-bridge.tests/ArangoPluginTests.cs`. After this task, the codebase will not compile if `ArangoPlugin` is referenced anywhere; grep first. |
| B4 | Hubs set `TenantContext` on connect + each method invocation | None | **Verify SignalR scope behavior with AsyncLocal**. The plan calls `SetTenant()` on every method to defend against scope loss. If this turns out to be unnecessary in practice, simplify; if AsyncLocal is genuinely lost between hub-method invocations, the plan's pattern is correct. | `KidSafeHub` uses `Context.UserIdentifier ?? Context.ConnectionId` for tenant id; in production this should resolve to the authenticated kid identity. |
| B5 | Cross-tenant isolation integration test | **ArangoDB** | — | The single most important test in the entire plan. Test data deliberately constructed so kid-tenant docs and admin-tenant docs have identical text/cosine — confirms isolation is enforced by `tenant_id` filter, not by content. |

### Phase C — Hybrid recall (4 tasks)

| Task | Title | Service deps | Plan-deferred verification | Notes |
|---|---|---|---|---|
| C1 | `MemoryRecallEngine.ExtractEntitiesAsync` (substring + alias match) | **ArangoDB** | — | NER fallback hook (`Func<string, Task<IReadOnlyList<string>>>?`) is defined but unused; Phase D wires the LM Studio NER backstop in. |
| C2 | `MemoryRecallEngine.GraphExpandAsync` | **ArangoDB** | — | Walks `1..@hops ANY entity edges` filtered by tenant. Excludes entity vertices from results. `MaterializeItem` reconstructs `MemoryItem` from collection-id prefix discrimination. |
| C3 | Vector top-K + `RecallAsync` composition | **ArangoDB + LM Studio + token** | AQL function is `APPROX_NEAR_COSINE`. The AQL **must** bind similarity via `LET sim = APPROX_NEAR_COSINE(...)` and reuse `sim` — two direct calls in one query gives errorNum 1554. No `OPTIONS` clause is needed (smoke test 2026-05-12). | Embedding-failure path: returns graph-only candidates with `cosine=0`, so recall degrades but doesn't fail. Scoring weights `α=0.7, β=0.3` are configurable via `Memory:RecallAlpha`/`Memory:RecallBeta`. |
| C4 | `MemoryPlugin.Recall` + DI wiring of `MemoryRecallEngine` | **ArangoDB + LM Studio** | — | Constructor signature for `MemoryPlugin` becomes 3-arg. All B2 tests must be updated to pass `MemoryRecallEngine`. |

### Phase D — Auto layer (3 tasks)

| Task | Title | Service deps | Plan-deferred verification | Notes |
|---|---|---|---|---|
| D1 | Pin SK 1.75 `AIContextProvider` API (research only — no commit) | None | **Query Context7** with `libraryId: /websites/learn_microsoft_en-us_semantic-kernel_frameworks_agent`, `query: "AIContextProvider abstract methods override OnNewMessage OnSuspend OnResume Mem0Provider source"`. Determine: (a) base class namespace, (b) exact override method name(s) used to react to each turn, (c) required NuGet package(s). | The plan tentatively uses `OnMessageAddedAsync(ChatMessageContent, CancellationToken)`. If the actual API is different (`OnAIInvocationAsync(IList<ChatMessageContent>, ...)` or other), update D2 implementation accordingly. |
| D2 | `DarbeesContextProvider` + `IFactExtractor` interface + stub-tested provider | **ArangoDB** | — | May require adding `Microsoft.SemanticKernel.Agents.Abstractions` and `Microsoft.SemanticKernel.Agents.Core` NuGet packages — verify in D1. |
| D3 | `LmStudioFactExtractor` + hub wiring of providers | **ArangoDB + LM Studio + token** | — | The hub wiring helper (`EnsureProviders`) is documented but actual `AgentThread` creation is deferred until the agent reasoning loop lands. Memory layer ships before the loop. |

### Phase E — Pending-embedding retry (1 task)

| Task | Title | Service deps | Plan-deferred verification | Notes |
|---|---|---|---|---|
| E1 | `PendingEmbeddingsService : BackgroundService` + `MemoryStore` retry helpers + dead-letter | **ArangoDB + LM Studio (intermittent)** | — | Test uses a `FlakyEmbeddings` client that fails first call, succeeds second. Configurable via `Memory:PendingEmbeddingRetryIntervalSeconds`, `Memory:PendingEmbeddingMaxAttempts`. Dead letter is `status='dead'` on the pending doc, not deletion (preserves forensics). |

### Phase F — Admin surface (2 tasks)

| Task | Title | Service deps | Plan-deferred verification | Notes |
|---|---|---|---|---|
| F1 | `AdminMemoryPlugin` registered on `kernel-admin` only | **ArangoDB** | — | Defense in depth: even though it's only registered on admin kernel, the plugin re-checks `tenant_context.TenantId == "admin"` before honoring cross-tenant queries. |
| F2 | `ParentHub.ListMemories` SignalR method | **ArangoDB** | — | Constructor consolidation: single ctor takes 3 deps (`logger`, `tenantAccessor`, `memory`). Remove the duplicate ctor introduced earlier. |

### Phase G — Docs + CI (2 tasks)

| Task | Title | Service deps | Plan-deferred verification | Notes |
|---|---|---|---|---|
| G1 | HANDOFF.md Phase 11, README ArangoDB requirement, anti-pattern #11 | None | — | Anti-pattern #11: "Don't expose tenant ID as an LLM-bound kernel-function parameter; always read from `ITenantContextAccessor` set by the hub." |
| G2 | CI ArangoDB service container + integration test gating | None (CI only) | — | Service container uses `arangodb:3.12` (pinned to the current stable; `arangodb:latest` would be fine today but pinning is safer). Health check curls `/_api/version`. Env vars `ARANGO_TEST_URL`, `ARANGO_TEST_USER`, `ARANGO_TEST_PASS` set on the test job. If A4 needed `--experimental-vector-index`, add the same flag in the service container's `command:`. |

---

## Open verifications and decisions deferred to implementation

These came up during brainstorming/planning and are deliberately not pinned in the spec. Each is owned by a specific task; resolve when you reach it.

| # | Question | Owning task | How to resolve |
|---|---|---|---|
| 1 | SK 1.75 `AIContextProvider` exact override methods | D1 | Query Context7 `/websites/learn_microsoft_en-us_semantic-kernel_frameworks_agent`; pin the override method name and any required Agents NuGet packages |
| 2 | DI lifetime for `ITenantContextAccessor` under SignalR scopes | B4 | The plan defaults to `AddSingleton` with AsyncLocal. If hub-method invocations lose context, switch to `IHubCallerContext`-keyed dictionary or per-connection `Context.Items`. |
| 3 | Entity-extraction prompt wording (LM Studio NER) | D3 | Iterate empirically. Plan ships a strict-JSON `response_format` request; tune temperature and prompt as needed once running. |
| 4 | Truncated vs full text in `RecallResult` (kid-safe vs admin) | C3 | Default to full text; revisit if response sizes become unwieldy or kid UX requires summary previews. |
| 5 | LM Studio request batching for many small embeddings | A3 | Currently 1-per-request. Revisit if Phase E retry queue gets long or auto-extraction generates many facts per turn. |
| 6 | Production value for `Memory:VectorNLists` | A6 | Default `100` is a starting point for IVF clustering with thousands of docs. Tune once production traffic shape is known; smaller values accept smaller training sets at the cost of recall quality. |

**Resolved during the 2026-05-12 smoke test:** vector index body shape (matches plan), startup flag (`--vector-index`, with `--experimental-vector-index` as deprecated alias), AQL function (`APPROX_NEAR_COSINE`), AQL OPTIONS clause not needed, AQL must bind similarity via LET. Lazy-creation pattern added to A4 to handle the cold-start constraint surfaced by the smoke test.

---

## Resume sequence

When you return to this work:

### 1. Repo state

```bash
# Adjust path to wherever you've checked out the repo (Windows / Linux / WSL)
cd /path/to/DarbeesChasingRainbows
git fetch
git checkout feature/graph-backed-rag
git pull --ff-only
git log --oneline -5
```

Confirm HEAD is `4f600e3` (B1) or your subsequent commits.

### 2. Verify environment

```bash
# LM Studio with token
export LMSTUDIO_API_KEY="<your-token>"
# Make sure .env contains the same value
test -f .env || cp .env.example .env

# Bring up the compose-managed stack
make up
sleep 12        # wait for arango healthcheck + dotnet watch boot
make health
# Expected: ArangoDB UP, LM Studio UP (if running), DAIS Bridge UP

# Test gates
export ARANGO_TEST_RUN=1
export ARANGO_TEST_URL=http://localhost:8529
export ARANGO_TEST_USER=root
export ARANGO_TEST_PASS=password
```

On PowerShell, swap `export VAR=value` for `$env:VAR = "value"` and the line-continuation backslash for backtick.

### 3. Pick the next task

Refer to the plan task list. Currently up: **A4 — `MemoryStore` schema migration**. Requires a running ArangoDB 3.12. The plan's body shape (`type: "vector"`, `fields: ["embedding"]`, `params: { dimension, metric, nLists }`) is the 3.x experimental form — push it and read ArangoDB's 400 response if any field is wrong. If the response says vector indexes are disabled, restart the container with `--experimental-vector-index`.

If A4 is blocked on ArangoDB, **B2 onward needs ArangoDB too** — the remaining no-service-deps work is sparse (parts of B4 hub-wiring code can be written but full validation needs ArangoDB).

### 4. Choose execution mode

- **Subagent-driven**: dispatch implementer per task → spec compliance review → code quality review → next. Fresh subagent per task. Two-stage review enforces both spec fidelity and code quality.
- **Inline (executing-plans)** (recommended for tightly-specced TDD tasks like B1 was): walk the plan steps yourself in this session. Slower context growth; you see every keystroke. Best when tasks are well-bounded and you want to make judgment calls on the fly.

For the subagent-driven path, the existing prompt templates live at `~/.claude/plugins/cache/claude-plugins-official/superpowers/5.1.0/skills/subagent-driven-development/` (`implementer-prompt.md`, `spec-reviewer-prompt.md`, `code-quality-reviewer-prompt.md`).

### 5. After all phases land

When G2 is committed:

1. Run all gates locally with `ARANGO_TEST_RUN=1`: `dotnet build`, `dotnet test`.
2. Update `HANDOFF.md` Phase 11 entry per Task G1 (already in plan).
3. Open PR back to `master`. Use `superpowers:finishing-a-development-branch` skill or open manually.
4. Squash if needed; the per-task commits are detailed but verbose for a final history.

---

## Things to know that are NOT in the spec or plan

- **`SendMessage` tool absent.** When code review surfaces issues, dispatch a fresh fix subagent rather than continuing the implementer. The fix subagent reads the on-disk state, applies the diff, and commits. This worked cleanly for both A1 fixes.
- **Plan code blocks are also production code.** A spec reviewer will flag inline comments in the plan as "extras". When writing future plans, avoid inline comments in code blocks unless the WHY genuinely belongs in the production source.
- **One-type-per-file is enforced.** Even when the plan groups multiple types in a single Step's code block, prefer separate files. `MemoryKind` + `MemoryCollections` in one file was flagged Minor; `RecallResult` + `ScoredMemoryItem` was flagged Important.
- **PowerShell forbids `&&`** as a command separator (HANDOFF anti-pattern #8). Use `;` or run commands separately. Subagent prompts should not chain with `&&`.
- **`ArangoDBNetStandard` 2.0.0 has no vector index typed API.** All vector index creation goes through raw HTTP via `MemoryStore`'s injected `HttpClient`. This is intentional, not a workaround.
- **LM Studio Bearer auth is recent.** If the project documentation later contradicts this (e.g., older examples without auth header), the auth requirement still applies — confirm by hitting `/v1/models` without a token and observing the `invalid_api_key` error.

---

## Reference: full task list with status

```
[x] A1 — Memory model records                                  (8281b8e + 2b737f0)
[x] A2 — IEmbeddingClient interface + failing test             (78c8b52)
[x] A3 — LmStudioEmbeddingClient implementation                (e3c45bf, dropped `using` on request)
[ ] A4 — MemoryStore schema migration                          (ArangoDB 3.12 required, port 8529)
[ ] A5 — MemoryStore write paths                               (ArangoDB + LM Studio)
[ ] A6 — Program.cs DI wiring + EnsureSchemaAsync at startup
[x] B1 — TenantContext + ITenantContextAccessor                (4f600e3, tests use interface-typed variable)
[ ] B2 — MemoryPlugin kernel functions                         (ArangoDB)
[ ] B3 — Replace ArangoPlugin with MemoryPlugin in Program.cs
[ ] B4 — Hub OnConnectedAsync sets TenantContext
[ ] B5 — Cross-tenant isolation integration test               (ArangoDB) ← critical invariant
[ ] C1 — MemoryRecallEngine.ExtractEntitiesAsync               (ArangoDB)
[ ] C2 — MemoryRecallEngine.GraphExpandAsync                   (ArangoDB)
[ ] C3 — VectorTopKAsync + RecallAsync                         (ArangoDB + LM Studio)
[ ] C4 — MemoryPlugin.Recall + DI                              (ArangoDB)
[ ] D1 — Verify SK 1.75 AIContextProvider API                  (Context7 query, no commit)
[ ] D2 — DarbeesContextProvider scaffolding                    (ArangoDB)
[ ] D3 — LmStudioFactExtractor + hub wiring                    (ArangoDB + LM Studio)
[ ] E1 — PendingEmbeddingsService BackgroundService            (ArangoDB)
[ ] F1 — AdminMemoryPlugin (kernel-admin only)                 (ArangoDB)
[ ] F2 — ParentHub.ListMemories SignalR method                 (ArangoDB)
[ ] G1 — HANDOFF.md Phase 11 + README + anti-pattern #11
[ ] G2 — CI ArangoDB service container
```

---

## Final verification (after G2)

Per the plan's "Final verification" section:

```powershell
$env:ARANGO_TEST_RUN = "1"
dotnet test dais-bridge.tests/dais-bridge.tests.csproj
```

All unit + integration tests should pass. Then:

```powershell
dotnet run --project dais-bridge/dais-bridge.csproj
```

Expected logs:
- `🚀 Darbee Sovereign Gateway Initializing...`
- ArangoDB collections created (or already-present logged)
- No exceptions at startup

Browse to `http://localhost:5000/` (or configured port). Expected: `"Darbee Sovereign AI Gateway Active"`.
