# TODO — Phase 11: Graph-Backed RAG

> **Where am I?** This is the active punchlist for Phase 11. The full design is in [`docs/superpowers/specs/2026-05-09-graph-backed-rag-design.md`](docs/superpowers/specs/2026-05-09-graph-backed-rag-design.md), the implementation plan with TDD steps + code blocks is in [`docs/superpowers/plans/2026-05-09-graph-backed-rag.md`](docs/superpowers/plans/2026-05-09-graph-backed-rag.md), and the session-history + environment-setup + per-task gotchas live in [`docs/superpowers/RESUME-graph-backed-rag.md`](docs/superpowers/RESUME-graph-backed-rag.md).

**Branch:** `feature/graph-backed-rag`
**Last commit:** `4c3ecf0` (A5 — write paths)
**Last verified:** 29/29 tests pass with `ARANGO_TEST_RUN=1 dotnet test` (2026-05-12)

---

## Quick start (cold-start checklist)

```bash
# 1. Repo
git checkout feature/graph-backed-rag
git pull --ff-only

# 2. ArangoDB 3.12 with --vector-index flag (required for memory tests)
docker run -d --name arango-test \
  -e ARANGO_ROOT_PASSWORD=password \
  -p 8529:8529 \
  arangodb:3.12 --vector-index
sleep 10
curl -u root:password http://localhost:8529/_api/version

# 3. LM Studio (required from A6 onward — A4/A5 use stub clients)
export LMSTUDIO_API_KEY="<your-token>"
# Load nomic-embed-text-v1.5 (768 dim) in LM Studio's server panel

# 4. Run all tests
export ARANGO_TEST_RUN=1
dotnet test dais-bridge.tests/dais-bridge.tests.csproj
# Expected: 29 passing
```

If anything fails, **read the resume guide** — it documents every environment quirk we've hit.

---

## Done

| Task | Commit | Notes |
|---|---|---|
| A1 — Memory model records | `8281b8e`, `2b737f0` | `MemoryItem`, `MemoryEdge`, `MemoryKind`/`MemoryCollections`, `RecallResult`, `ScoredMemoryItem`, `WriteResult` |
| A2 — `IEmbeddingClient` interface + failing test | `78c8b52` | Stub `HttpMessageHandler`, no service deps |
| A3 — `LmStudioEmbeddingClient` impl | `e3c45bf` | Bearer auth, batch + dim validation, 3 unit tests |
| B1 — `TenantContext` + `ITenantContextAccessor` | `4f600e3` | AsyncLocal-backed, 4 unit tests. Plan deviation: tests use interface-typed variable (default interface members aren't callable via concrete class) |
| docs — v4 reversal | `8086a4b` | ArangoDB v4 isn't released yet; retargeted to 3.12.x |
| docs — A4 redesign | `666c2bb` | Lazy vector index (smoke test showed `EnsureSchemaAsync` can't create vector indexes at startup — empty collections trigger 1555) |
| A4 — `MemoryStore` schema + lazy vector index | `ad92b61` | `EnsureSchemaAsync` (collections + persistent indexes), `EnsureVectorIndexAsync` (idempotent, cached, cleans up unusable). 4 integration tests |
| A5 — Write paths | `4c3ecf0` | Two-phase write (insert → embed → patch + EnsureVectorIndexAsync, fallback to pending queue). 2 integration tests |

---

## Remaining

### Phase A — Substrate

- [ ] **A6** — Wire `IEmbeddingClient` + `MemoryStore` into `Program.cs` DI; call `EnsureSchemaAsync` at startup. Requires LM Studio + `LMSTUDIO_API_KEY`. *No new tests; verified by app boot.*
  Plan: [§ Task A6](docs/superpowers/plans/2026-05-09-graph-backed-rag.md#task-a6-wire-into-programcs-di-no-consumer-yet)

### Phase B — Explicit memory layer

- [ ] **B2** — `MemoryPlugin` kernel functions (`RememberDecision`, `RememberObservation`, `LinkMemory`). Tenant ID read from `ITenantContextAccessor`, never an LLM-bound parameter. ArangoDB required.
- [ ] **B3** — Replace `ArangoPlugin` registration with `MemoryPlugin` in `Program.cs`; delete `dais-bridge/Plugins/ArangoPlugin.cs` and `dais-bridge.tests/ArangoPluginTests.cs`. Grep first for any remaining references.
- [ ] **B4** — Hubs (`KidSafeHub`, `ParentHub`) set `TenantContext` on `OnConnectedAsync` and on each method invocation (defensive against AsyncLocal scope loss).
- [ ] **B5** — Cross-tenant isolation integration test. **The single most important test in the plan** — identical text/cosine across tenants, confirms isolation is enforced by `tenant_id` filter, not by content uniqueness.

### Phase C — Hybrid recall

- [ ] **C1** — `MemoryRecallEngine.ExtractEntitiesAsync` (substring + alias match). NER fallback hook is defined but wired in D3.
- [ ] **C2** — `MemoryRecallEngine.GraphExpandAsync`. Walks `1..@hops ANY entity edges`, filtered by tenant, excludes entity vertices from result set.
- [ ] **C3** — `MemoryRecallEngine.VectorTopKAsync` + `RecallAsync` composition. AQL uses `LET sim = APPROX_NEAR_COSINE(...)` (double-call is errorNum 1554). Embedding-failure path: returns graph-only candidates with `cosine=0`.
- [ ] **C4** — `MemoryPlugin.Recall` + DI wiring of `MemoryRecallEngine`. `MemoryPlugin` constructor becomes 3-arg; all B2 tests need updating.

### Phase D — Auto layer (long-term fact extraction)

- [ ] **D1** — *Research-only, no commit.* Pin SK 1.75 `AIContextProvider` API via Context7. Determine: base class namespace, exact override method name(s), required NuGet packages.
- [ ] **D2** — `DarbeesContextProvider` + `IFactExtractor` interface + stub-tested provider. ArangoDB required.
- [ ] **D3** — `LmStudioFactExtractor` + hub wiring of providers. LM Studio required for NER prompt iteration.

### Phase E — Background retry

- [ ] **E1** — `PendingEmbeddingsService : BackgroundService` + `MemoryStore` retry helpers + dead-letter (`status='dead'` preserves forensics, not deletion).

### Phase F — Admin surface

- [ ] **F1** — `AdminMemoryPlugin` registered on `kernel-admin` only. Defense in depth: plugin re-checks `tenant.TenantId == "admin"`.
- [ ] **F2** — `ParentHub.ListMemories` SignalR method.

### Phase G — Docs + CI

- [ ] **G1** — `HANDOFF.md` Phase 11 final entry, `README.md` ArangoDB requirement, anti-pattern #11 (tenant ID not LLM-bound).
- [ ] **G2** — CI ArangoDB service container in `.github/workflows/ci.yml` (use `arangodb:3.12 --vector-index`). Gate integration tests on `ARANGO_TEST_RUN=1`.

---

## Open verifications (resolve when you reach the owning task)

| # | Question | Owning task |
|---|---|---|
| 1 | SK 1.75 `AIContextProvider` exact override methods | D1 |
| 2 | DI lifetime for `ITenantContextAccessor` under SignalR scopes (default is `AddSingleton` with AsyncLocal) | B4 |
| 3 | Entity-extraction prompt wording (LM Studio NER) | D3 |
| 4 | Truncated vs full text in `RecallResult` (kid-safe vs admin) | C3 |
| 5 | LM Studio request batching for many small embeddings | A3 (revisit) |
| 6 | Production value for `Memory:VectorNLists` (default 100) | A6 |

All ArangoDB-specific open questions (vector index body shape, AQL function name, OPTIONS clause, startup flag) were resolved during the 2026-05-12 smoke test and are now codified in the spec + plan.

---

## Plan deviations to remember when reading the plan

Every commit message documents its own deviation. The recurring ones:

- **`MemoryStore` constructor takes `vectorNLists`** (added in A4 redesign). All ~18 call sites in the plan show the updated signature.
- **Schema test helpers are `internal static` with unsuffixed names** (`ArangoUrl`, `CreateUniqueDb`, `DropDb`). The plan's A5 Step 2 wanted them renamed to `XxxStatic`; that step is obsolete.
- **`PostJsonRawAsync` uses `StringContent(JsonSerializer.Serialize(...))` not `JsonContent.Create()`** — ArangoDB rejects chunked transfer encoding (errorNum 9).
- **Default interface members aren't callable via the concrete class** — `ITenantContextAccessor acc = new TenantContextAccessor()` in tests, not `var acc = ...`.

---

## Definition of "Phase 11 done"

1. `ARANGO_TEST_RUN=1 dotnet test` — all tests pass.
2. `dotnet run --project dais-bridge` — boots clean, logs `🚀 Darbee Sovereign Gateway Initializing...`, `EnsureSchemaAsync` runs, no exceptions.
3. PR `feature/graph-backed-rag` → `master`, CI green (G2 service container in place).
4. `HANDOFF.md` Phase 11 entry rewritten as the past-tense summary (G1).
