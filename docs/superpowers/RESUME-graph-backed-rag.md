# Resume Guide — Phase 11: Graph-Backed RAG

> **Purpose:** Everything you (or a future agent session) need to pick this work back up cold without re-deriving context.

**Last updated:** 2026-05-10
**Branch:** `feature/graph-backed-rag` (off `master`)
**Status:** Phases A1, A2, A3 complete; Phases A4 → G2 remaining; A4 blocked on local v4 ArangoDB.

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
| `0117708` | master | Spec + plan retargeted to ArangoDB v4 (per user direction; v4 has first-class vector index) |
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

### Verification attempts for ArangoDB v4 vector index — all blocked

Attempted to resolve Open Questions #1 (vector index body shape) and #2 (AQL similarity function name) before A4. All three external channels failed:

| Channel | Result |
|---|---|
| Context7 `/arangodb/arangodb` (4 queries) | Corpus has the general `/_api/index` API but **no vector-specific examples**. Confirms vector indexes exist as a type; no body shape, no AQL function example. |
| `docs.arangodb.com/stable/...` | 301 redirects to `docs.arango.ai` (the docs were moved). |
| `docs.arango.ai/...` | 403 Forbidden via WebFetch — likely blocking automated user agents. |

**Conclusion:** the empirical path is now the cheapest verification. When v4 ArangoDB is running, the first `POST /_api/index` call from `EnsureVectorIndexAsync` is itself the verification — ArangoDB returns descriptive 400s naming any invalid field. Adjust the plan's 3.x body shape based on the live error response.

### Local infrastructure blocker uncovered

`docker ps` on the host showed an **already-running** container:

```
mustang-arangodb    arangodb:3.12    Up
```

Two consequences:
- **Port 8529 is held** by another project's container. The resume guide's prior instruction to `docker run -d --name arango-test ... -p 8529:8529 ...` would fail with a port collision.
- **3.12 is below the viability floor** stated in the original Environment section ("3.12 or earlier: vector RAG is not viable. Upgrade required."). We cannot reuse this container for our work even if the port were free.

**Fix for next session:** start a separate v4 container on port 8530:

```powershell
docker run -d --name arango-test `
  -e ARANGO_ROOT_PASSWORD=password `
  -p 8530:8529 `
  arangodb:latest

$env:ARANGO_TEST_URL = "http://localhost:8530"
```

Capture the major version returned by `curl http://localhost:8530/_api/version` (with auth) — that's the input to A4's body-shape decision.

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

**Status as of 2026-05-10:** Docker is healthy, but **port 8529 is occupied** by another project's `mustang-arangodb` container running `arangodb:3.12`. Do not reuse that container — it belongs to the mustangcoffee stack and is below the vector-RAG viability floor.

**Required action before resuming:**

1. Pull a v4 image:

```powershell
docker pull arangodb:latest
```

2. Start on a **non-conflicting port** (8530 instead of 8529):

```powershell
docker run -d --name arango-test `
  -e ARANGO_ROOT_PASSWORD=password `
  -p 8530:8529 `
  arangodb:latest
```

3. Wait ~10s, then verify:

```bash
curl -u root:password http://localhost:8530/_api/version
```

4. **Note the major version returned.** This determines which AQL vector function and index body shape Task A4 will use:
   - **4.x:** vector index is first-class. Verify the exact `POST /_api/index` body shape via Context7 (`/arangodb/arangodb`, query "vector index POST /_api/index v4 type vector body shape dimension metric example") before writing `EnsureVectorIndexAsync`.
   - **3.13–3.x:** vector index is experimental. The plan's existing body shape (`type: "vector"`, `params: { dimension, metric, nLists }`) is the experimental form and should work; the AQL function `APPROX_NEAR_COSINE` is the experimental name.
   - **3.12 or earlier:** vector RAG is not viable. Upgrade required.

5. For integration tests, set the env var that gates them (note port `8530`, not 8529):

```powershell
$env:ARANGO_TEST_RUN = "1"
$env:ARANGO_TEST_URL = "http://localhost:8530"
$env:ARANGO_TEST_USER = "root"
$env:ARANGO_TEST_PASS = "password"
```

---

## Remaining work — annotated by phase

The plan's per-task TDD steps are authoritative. This table adds **dependencies, verification, and per-task gotchas not visible inside the plan**.

### Phase A — Substrate (4 tasks remaining of 6)

| Task | Title | Service deps | Plan-deferred verification | Notes |
|---|---|---|---|---|
| A2 | `IEmbeddingClient` interface + failing test | None (stub `HttpClient` handler) | — | Unit tests use `StubHandler : HttpMessageHandler`. Real LM Studio not required for this task. |
| A3 | `LmStudioEmbeddingClient` impl | None for unit tests; LM Studio + token for manual smoke | — | The plan now includes Bearer auth in the constructor (`apiKey` param). Unit tests cover dimension mismatch and batch input shape. |
| A4 | `MemoryStore` schema migration (creates collections + indexes) | **ArangoDB required** | **Verify v4 vector index body shape via Context7 BEFORE writing `EnsureVectorIndexAsync`**. The plan's body shape is the 3.x experimental form — may need adjustment for v4. | Vector index is created via raw `POST /_api/index?collection=X` because `ArangoDBNetStandard` 2.0.0 has no typed support. Schema migration is idempotent — safe to call repeatedly. |
| A5 | Write paths (UpsertDecision/Observation/Fact/Summary/Entity/Edge + ListPending) | **ArangoDB + LM Studio + token** | — | Two-phase write: insert with `status='pending_embedding'`, try embed, on success patch `status='ready'`, on failure enqueue to `memory_pending_embeddings`. The `ConstantEmbeddingClient` and `FailingEmbeddingClient` test helpers are reused by later tests — keep them `internal` not `private`. |
| A6 | Wire IEmbeddingClient + MemoryStore into Program.cs DI | None | — | `EnsureSchemaAsync` runs at startup. Adds `IHttpClientFactory` "memory" client. Reads `LMSTUDIO_API_KEY` env var first, then `AI:LMStudioApiKey` config. |

### Phase B — Explicit memory layer (5 tasks)

| Task | Title | Service deps | Plan-deferred verification | Notes |
|---|---|---|---|---|
| B1 | `TenantContext` + `ITenantContextAccessor` (AsyncLocal) | None | — | Existing `TenantContext.cs` is extended (added static factories `Admin`, `ForKid`). New `ITenantContextAccessor` + `TenantContextAccessor` (AsyncLocal). Unit tests cover async flow + parallel task isolation. |
| B2 | `MemoryPlugin` kernel functions (RememberDecision/Observation/LinkMemory) | **ArangoDB** | — | Tenant ID is **not** an LLM-bound parameter — `MemoryPlugin` reads from `ITenantContextAccessor`. The plan currently shows a 2-arg constructor in B2 then expands to 3-arg in C4 (adds `MemoryRecallEngine`); test fixtures need updating in C4. |
| B3 | Replace `ArangoPlugin` with `MemoryPlugin` in Program.cs and delete the stub | None | — | Deletes `dais-bridge/Plugins/ArangoPlugin.cs` and `dais-bridge.tests/ArangoPluginTests.cs`. After this task, the codebase will not compile if `ArangoPlugin` is referenced anywhere; grep first. |
| B4 | Hubs set `TenantContext` on connect + each method invocation | None | **Verify SignalR scope behavior with AsyncLocal**. The plan calls `SetTenant()` on every method to defend against scope loss. If this turns out to be unnecessary in practice, simplify; if AsyncLocal is genuinely lost between hub-method invocations, the plan's pattern is correct. | `KidSafeHub` uses `Context.UserIdentifier ?? Context.ConnectionId` for tenant id; in production this should resolve to the authenticated kid identity. |
| B5 | Cross-tenant isolation integration test | **ArangoDB** | — | The single most important test in the entire plan. Test data deliberately constructed so kid-tenant docs and admin-tenant docs have identical text/cosine — confirms isolation is enforced by `tenant_id` filter, not by content. |

### Phase C — Hybrid recall (4 tasks)

| Task | Title | Service deps | Plan-deferred verification | Notes |
|---|---|---|---|---|
| C1 | `MemoryRecallEngine.ExtractEntitiesAsync` (substring + alias match) | **ArangoDB** | — | NER fallback hook (`Func<string, Task<IReadOnlyList<string>>>?`) is defined but unused; Phase D wires the LM Studio NER backstop in. |
| C2 | `MemoryRecallEngine.GraphExpandAsync` | **ArangoDB** | — | Walks `1..@hops ANY entity edges` filtered by tenant. Excludes entity vertices from results. `MaterializeItem` reconstructs `MemoryItem` from collection-id prefix discrimination. |
| C3 | Vector top-K + `RecallAsync` composition | **ArangoDB + LM Studio + token** | **Verify v4 AQL similarity function name** before writing `VectorTopKAsync`. The plan uses `APPROX_NEAR_COSINE` (3.x experimental name). Update if v4 stabilized to a different name. | Embedding-failure path: returns graph-only candidates with `cosine=0`, so recall degrades but doesn't fail. Scoring weights `α=0.7, β=0.3` are configurable via `Memory:RecallAlpha`/`Memory:RecallBeta`. |
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
| G2 | CI ArangoDB service container + integration test gating | None (CI only) | — | Service container uses `arangodb:latest` (matches plan's pre-flight). Health check curls `/_api/version`. Env vars `ARANGO_TEST_URL`, `ARANGO_TEST_USER`, `ARANGO_TEST_PASS` set on the test job. |

---

## Open verifications and decisions deferred to implementation

These came up during brainstorming/planning and are deliberately not pinned in the spec. Each is owned by a specific task; resolve when you reach it.

| # | Question | Owning task | How to resolve |
|---|---|---|---|
| 1 | ArangoDB v4 vector index body shape | A4 | Query Context7 `/arangodb/arangodb` with "vector index v4 POST /_api/index body shape dimension metric"; compare returned syntax against the plan's 3.x form |
| 2 | ArangoDB v4 AQL similarity function name | C3 | Query Context7 `/arangodb/arangodb` with "AQL APPROX_NEAR_COSINE vector similarity function v4"; update `VectorTopKAsync` if renamed |
| 3 | SK 1.75 `AIContextProvider` exact override methods | D1 | Query Context7 `/websites/learn_microsoft_en-us_semantic-kernel_frameworks_agent`; pin the override method name and any required Agents NuGet packages |
| 4 | DI lifetime for `ITenantContextAccessor` under SignalR scopes | B4 | The plan defaults to `AddScoped` with AsyncLocal. If hub-method invocations lose context, switch to `IHubCallerContext`-keyed dictionary or per-connection `Context.Items`. |
| 5 | Entity-extraction prompt wording (LM Studio NER) | D3 | Iterate empirically. Plan ships a strict-JSON `response_format` request; tune temperature and prompt as needed once running. |
| 6 | Truncated vs full text in `RecallResult` (kid-safe vs admin) | C3 | Default to full text; revisit if response sizes become unwieldy or kid UX requires summary previews. |
| 7 | LM Studio request batching for many small embeddings | A3 | Currently 1-per-request. Revisit if Phase E retry queue gets long or auto-extraction generates many facts per turn. |
| 8 | Docker tag for ArangoDB v4 vs latest | Pre-flight | If `arangodb:4` is published when you resume, prefer it for major-version pinning. Otherwise `arangodb:latest` and capture the actual major version in Pre-flight Step 2 output. |

---

## Resume sequence

When you return to this work:

### 1. Repo state

```powershell
cd C:\Work\DarbeesChasingRainbows
git fetch
git checkout feature/graph-backed-rag
git pull --ff-only
git log --oneline -5
```

Confirm HEAD is `2b737f0` (or your subsequent commits).

### 2. Verify environment

```powershell
# LM Studio with token
$env:LMSTUDIO_API_KEY = "<your-token>"
curl http://localhost:1234/v1/embeddings `
  -H "Content-Type: application/json" `
  -H "Authorization: Bearer $env:LMSTUDIO_API_KEY" `
  -d '{"model":"nomic-embed-text-v1.5","input":"hello"}'

# ArangoDB v4 — note port 8530 (8529 is held by mustang-arangodb v3.12)
docker run -d --name arango-test -e ARANGO_ROOT_PASSWORD=password -p 8530:8529 arangodb:latest
Start-Sleep -Seconds 10
curl -u root:password http://localhost:8530/_api/version

# Test gates
$env:ARANGO_TEST_RUN = "1"
$env:ARANGO_TEST_URL = "http://localhost:8530"
$env:ARANGO_TEST_USER = "root"
$env:ARANGO_TEST_PASS = "password"
```

### 3. Pick the next task

Refer to the plan task list. Currently up: **A4 — `MemoryStore` schema migration**. This task **requires a running ArangoDB v4** on port 8530 (see Environment section above). The plan's existing 3.x body shape for the vector index is unverified for v4 — when you write `EnsureVectorIndexAsync`, push the request to the live server and adjust based on any 400 response. ArangoDB returns descriptive errors that name invalid fields.

If A4 is still blocked on ArangoDB, **B1 (`TenantContext` + `ITenantContextAccessor`)** is the easiest pivot — pure C#, no service deps, AsyncLocal-based.

### 4. Choose execution mode

- **Subagent-driven** (recommended): dispatch implementer per task → spec compliance review → code quality review → next. Fresh subagent per task. Two-stage review enforces both spec fidelity and code quality.
- **Inline (executing-plans)**: walk the plan steps yourself in this session. Slower context growth; you see every keystroke. Best when tasks are tightly coupled or when you want to make judgment calls that the subagent can't.

For the subagent-driven path, the existing prompt templates live at `C:\Users\pdarb\.claude\plugins\cache\claude-plugins-official\superpowers\5.1.0\skills\subagent-driven-development\` (`implementer-prompt.md`, `spec-reviewer-prompt.md`, `code-quality-reviewer-prompt.md`).

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
[ ] A4 — MemoryStore schema migration                          (ArangoDB v4 required, port 8530)
[ ] A5 — MemoryStore write paths                               (ArangoDB + LM Studio)
[ ] A6 — Program.cs DI wiring + EnsureSchemaAsync at startup
[ ] B1 — TenantContext + ITenantContextAccessor
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
