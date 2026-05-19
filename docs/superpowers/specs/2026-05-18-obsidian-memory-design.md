# Obsidian ↔ Memory — Design Spec

**Date:** 2026-05-18
**Status:** Draft, ready for review
**Branch (proposed):** `feature/obsidian-memory` (off `master`)
**Related:** Layers on top of the [Content RAG design](2026-05-16-content-rag-design.md) and the [Content RAG UI design](2026-05-17-content-rag-ui-design.md). Depends on the merged memory data layer (PR #2) and the dev-search UI (PR #4).

---

## 1. Problem

The memory data layer was designed for six `MemoryKind` values — `Decision`, `Observation`, `Fact`, `Summary`, `Entity`, `Post` — but only `Post` is wired end-to-end. The other collections exist in Arango (`memory_observations`, `memory_facts`, `memory_decisions`, `memory_entities`, `memory_edges`) but nothing writes to them, and `/api/memory/search` reads only `memory_posts`.

Obsidian is the authoring environment for everything that lands in `src/content/`, but the bridge has no idea Obsidian exists. Daily notes, scratch thinking, and research jottings live in the same vault as the publishable posts and never reach the memory layer. The author has no way to ask "what have I already written *or thought* about X?" from inside Obsidian itself — the closest tool is `/dev-search`, which only sees published posts and is in a browser tab.

This spec wires Obsidian and the memory layer together in both directions: notes flagged with `memory: true` flow into the appropriate private-tenant collection on save; an Obsidian sidebar queries `memory/search` with a posts / private / both scope toggle. Public posts continue to flow through the existing `rag:reindex` path — that pipeline is untouched.

## 2. Goals

1. **Notes are first-class memory.** Any Obsidian note with `memory: true` in its frontmatter becomes a searchable memory item under `tenant_id == "private"`. Authoring stays in Obsidian; the bridge owns embedding + persistence.
2. **Search is unified.** One bridge endpoint (`/api/memory/search`) returns results across selectable `kinds` and `tenants`. The Obsidian sidebar exposes a posts / private / both toggle that maps directly to those filters.
3. **Native Obsidian UX.** A TypeScript plugin owns file-watch, debounce, frontmatter parsing, sidebar rendering, and settings. On-save ingestion is automatic; the author doesn't run a CLI.
4. **Tenant isolation is verifiable.** Public posts and private notes live in the same Arango database with `tenant_id` as the isolation field. A regression test asserts that `tenants=["public"]` never returns a private row even when scores collide.

## 3. Non-goals

- **No chat-over-memory.** This spec is retrieval only. A RAG answer endpoint that streams chat output from `memory_posts` is a separate spec (`Maverick answer-from-context` in the content-RAG-UI follow-ups).
- **No entity extraction or knowledge graph.** `MemoryKind.Entity` and `memory_edges` stay empty for now. Wikilink-as-edge graphs are a future research direction.
- **No public-facing surface.** The plugin runs in Obsidian on the author's host. Bridge stays on `localhost:5000`. No Cloudflare exposure, no auth layer beyond the existing none-on-localhost.
- **No multi-vault support.** One vault per bridge instance. Vault path is the implicit identifier inside note keys.
- **No encryption at rest.** Current Arango deployment has no special at-rest encryption; private notes are protected by host-level access only.
- **No GUI graph view.** Sidebar is a flat ranked list. Backlink panels are out of scope (would require edges, see [[non-goals-entity-extraction]] above).

## 4. Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  Obsidian vault (= repo root)                                               │
│                                                                             │
│   .obsidian/plugins/darbee-memory/          (gitignored — install target)   │
│              │   built from obsidian-plugin/   (tracked TS source)          │
│              │                                                              │
│   on file save:                                                             │
│     │  read frontmatter → if memory:true → enqueue                          │
│     │  debounce 2s → batch flush                                            │
│     ▼                                                                       │
│   POST /api/memory/ingest-notes                                             │
│     { tenant:"private",                                                     │
│       notes:[ { key, kind, text, title, metadata } ],                       │
│       currentKeys:[…] }   ← full-sync stale-delete envelope                 │
│                                                                             │
│   sidebar query (scope toggle: posts | private | both):                     │
│     ▼                                                                       │
│   POST /api/memory/search                                                   │
│     { query, k, kinds:[…], tenants:[…] }                                    │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
                            │  (Obsidian requestUrl — bypasses CORS)
                            ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  DAIS Bridge (:5000, podman container)                                      │
│     ContentRagEndpoints + MemoryStore + OpenAiCompatibleEmbeddingClient     │
│                            │                                                │
│                            ▼                                                │
│     Arango :8529  (one database: darbees_knowledge)                         │
│        memory_posts          tenant=public    (existing, unchanged)         │
│        memory_observations   tenant=private   (new in-scope writes)         │
│        memory_facts          tenant=private   (new in-scope writes)         │
│        memory_decisions      tenant=private   (new in-scope writes)         │
│                            │                                                │
│                            ▼                                                │
│     Embedding: llama-server :8081 (qwen3-embedding-8b, 4096-dim)            │
│                (same model as posts — no second embedding stack)            │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Three boundaries with clear interfaces:**

- **Plugin** owns: file-watch, debounce, frontmatter parsing, key derivation, sidebar UI, settings panel, request cancellation. No persistence on the plugin side.
- **Bridge** owns: `ingest-notes` endpoint, extended `search`, hashing-for-cache, tenant filter enforcement, stale-delete scoping.
- **MemoryStore** owns: collection writes, per-doc embedding calls, tenant-scoped queries.

## 5. Components

### 5.1 Obsidian plugin (`obsidian-plugin/` → `.obsidian/plugins/darbee-memory/`)

**Source layout (tracked in git):**

```
obsidian-plugin/
  src/
    main.ts                  Plugin entry: events, commands, settings tab, sidebar registration
    ingester.ts              Pure: frontmatter parse, key derivation, hashing, payload build
    bridge-client.ts         requestUrl wrappers: ingestNotes(), searchMemory()
    sidebar-view.ts          ItemView subclass: query input, scope toggle, result cards
    settings.ts              PluginSettingTab + DEFAULT_SETTINGS
    types.ts                 Shared TS interfaces (NoteRecord, IngestPayload, SearchHit)
  test/
    ingester.test.ts         Vitest unit tests (no Obsidian API)
    main.test.ts             Vitest integration with in-memory Vault stub
  manifest.json
  versions.json
  esbuild.config.mjs
  package.json
  tsconfig.json
  README.md                  Install / dev / uninstall instructions
```

**Install:** one-time `npm run obsidian:link` script symlinks the build output into `.obsidian/plugins/darbee-memory/`. Build via `npm run obsidian:build` (esbuild). Watch-mode dev via `npm run obsidian:dev`.

**Stack:** TypeScript, esbuild, Obsidian Plugin API, Vitest for tests. Standard community-plugin shape.

**Frontmatter contract (the only knobs the author touches):**

```yaml
---
title: Some note
memory: true
memory_kind: observation       # observation | fact | decision (default: observation)
memory_tenant: private         # optional override; default from plugin settings
---
```

Anything else is ignored. Tags are passed through into `metadata.tags` but don't change routing.

**Debounce model:** save event → enqueue `{ path, frontmatter, body }` → reset 2s timer → on idle, drain the queue into a single batch. If an ingest call is in-flight when a new save happens, the save buffers into the next batch — at most one request in flight at a time.

**Status bar:** four states — `idle` (hidden), `⟳ syncing N`, `✓ synced N` (auto-clears after 3s), `✗ N error(s) — see console`. Errors include the response message.

**Settings panel:** bridge URL (default `http://localhost:5000`), default tenant (default `private`), default kind (default `observation`), debounce ms (default `2000`), sidebar default scope (default `both`). All are persisted via `loadData()`/`saveData()` per Obsidian convention.

**Sidebar (`ItemView`):**
- Text input + Search button. Enter submits.
- Scope toggle: three pills (`Posts`, `Private`, `Both`). Selection persisted in settings.
- Result cards: kind badge (`post`/`observation`/`fact`/`decision`), tenant badge with color (`public` neutral, `private` warm), score (3 decimals), snippet (first 200 chars), title.
- Click handling: if `kind == "post"`, open the URL in the system browser. Otherwise, `workspace.openLinkText(key, "")` opens the note inside Obsidian.
- Cancellation: each new query aborts the previous in-flight request via `AbortController`. Mirrors the `/dev-search` pattern.

### 5.2 Bridge additions

**New endpoint: `POST /api/memory/ingest-notes`**

Request:

```json
{
  "tenant": "private",
  "notes": [
    {
      "key": "obsidian://daily/2026-05-18.md",
      "kind": "observation",
      "text": "Cast iron pan rusts in the trailer if I don't dry it on the burner.",
      "title": "2026-05-18",
      "metadata": {
        "tags": ["rv", "gear"],
        "source": "obsidian"
      }
    }
  ],
  "currentKeys": [
    "obsidian://daily/2026-05-18.md",
    "obsidian://thoughts/well-siting.md"
  ]
}
```

Response:

```json
{
  "embeddedCount": 1,
  "cachedCount": 0,
  "failedCount": 0,
  "staleDeletedCount": 2,
  "durationMs": 87,
  "perNote": [
    { "key": "obsidian://daily/2026-05-18.md", "outcome": "embedded" }
  ]
}
```

**Per-note algorithm** (mirrors `UpsertOnePostVectorAsync`):
1. `_key = sha1(note.key)` — Arango key constraints can't accept slashes; the human key stays in a `note_key` field.
2. `hash = sha256(text + model_id)` — model is part of the cache key so a model swap invalidates.
3. Look up existing doc by `_key`. If `existing.hash == hash AND existing.status == "ready"` → `outcome=cached`, skip embed.
4. Else → embed via `IEmbeddingClient.EmbedAsync(text)` (server-side already targets :8081). Insert/replace doc with `status="ready"`. `outcome=embedded`.
5. On per-note exception (e.g., bridge can't reach :8081 mid-batch): catch, mark `outcome=failed` with `reason`, continue with the next note. Don't fail the whole batch.

**Stale-delete (one AQL after the loop):**

```aql
FOR d IN UNION(
    (FOR x IN memory_observations FILTER x.tenant_id == @tenant FILTER x.source == "obsidian" RETURN x),
    (FOR x IN memory_facts        FILTER x.tenant_id == @tenant FILTER x.source == "obsidian" RETURN x),
    (FOR x IN memory_decisions    FILTER x.tenant_id == @tenant FILTER x.source == "obsidian" RETURN x)
  )
  FILTER d.note_key NOT IN @currentKeys
  REMOVE d IN <its collection>
```

`source == "obsidian"` is the bright line — Arango-direct or future-CLI inserts with no `source` field are untouched. Returns the deleted count.

**Search extension: `POST /api/memory/search` (modified, back-compat)**

Add two optional request fields:

```json
{
  "query": "well siting",
  "k": 5,
  "kinds": ["post", "observation"],
  "tenants": ["public", "private"]
}
```

Defaults preserve current behavior: `kinds=["post"]`, `tenants=["public"]`. Existing `/dev-search` calls (which send neither field) continue to work unchanged.

**Behavior:**
1. Embed query once via :8081.
2. For each (kind, tenant) Cartesian pair, build an AQL fragment over `MemoryCollections.ForKind(kind)` filtered by `tenant_id == @tenant AND status == "ready"`, scoring with `COSINE_SIMILARITY(d.embedding, @qvec)`.
3. UNION the fragments, `SORT score DESC`, `LIMIT @k`.
4. Project `{ key: d.note_key OR d.url, kind: <bound>, tenant: d.tenant_id, title, snippet, score, source }`. The `kind` value is bound per fragment so callers always know what kind of row each result is.

**Tenant filter enforcement:** every fragment hard-codes `FILTER d.tenant_id == @tenant`. Bound parameters only — no string interpolation. The C# `SearchAsync` method validates that every requested tenant appears in the per-fragment bindVars before the cursor executes; if it doesn't, throw `InvalidOperationException` with a regression message.

### 5.3 MemoryStore additions

New types in `dais-bridge/Memory/Models/`:

- `NoteDocument(string Key, string Title, string Text, MemoryKind Kind, IReadOnlyDictionary<string,object>? Metadata)` — input record.
- `UpsertNoteResult(string Key, VectorWriteOutcome Outcome, string? Reason)` — per-note return.
- `IngestNotesResult(int EmbeddedCount, int CachedCount, int FailedCount, int StaleDeletedCount, long DurationMs, IReadOnlyList<UpsertNoteResult> PerNote)` — overall response shape.

New methods in `MemoryStore`:

- `async Task<UpsertNoteResult> UpsertNoteAsync(NoteDocument note, string tenant, CancellationToken ct = default)`:
  - Cache-check by hash + status (same pattern as posts).
  - On miss: embed → write to `MemoryCollections.ForKind(note.Kind)`.
  - Returns `Embedded` / `Cached` / `Failed`.
- `async Task<int> DeleteStaleNotesAsync(IReadOnlyList<string> currentKeys, string tenant, CancellationToken ct = default)`:
  - Scoped by `tenant_id == tenant AND source == "obsidian"`.
  - Iterates `MemoryCollections.Observations`, `_Facts`, `_Decisions`. Doesn't touch `memory_posts`.
  - Returns total deleted count.

No new interfaces. `IEmbeddingClient` already does the right thing.

### 5.4 Plugin → bridge contract details

**Network:** plugin uses `obsidian.requestUrl` (not `fetch`) — it goes through Obsidian's Electron main process and bypasses CORS. The bridge is reachable at `http://localhost:5000` from the host where Obsidian runs.

**Timeouts:** 30s per call. `requestUrl` supports an `Accept` header and rejects on non-2xx; the plugin wraps it to surface bridge structured errors (`{ error, message }`).

**Auth:** none. Bridge has no auth on localhost. Documented as a constraint in §3 non-goals (host-level access is the security model).

**Payload size:** the `currentKeys` envelope is bounded by note count. At 10k flagged notes the payload is ~500KB JSON — well under Obsidian's `requestUrl` default. A larger vault would justify a hash-of-set protocol but is out of scope.

### 5.5 `CLAUDE.md` update

Two new rows in the authoring-scripts command table:

| Task | Command | When |
|---|---|---|
| Build Obsidian plugin | `npm run obsidian:build` | After editing `obsidian-plugin/` source |
| Link plugin into vault | `npm run obsidian:link` | First-time install; idempotent |

A new "Memory ingest" subsection under "Things to be careful about":

> Obsidian plugin runs on save. The `memory: true` frontmatter is the only opt-in. Removing it un-flags the note; on next save the bridge stale-deletes the corresponding memory row (tenant=private only — public posts unaffected).

## 6. Plugin lifecycle and build

**One-time setup:**

```bash
cd obsidian-plugin
npm install
npm run build
npm run link               # symlinks dist/ into ../.obsidian/plugins/darbee-memory/
```

After install, enable "Darbee Memory" in Obsidian's Community Plugins panel.

**Watch mode (development):** `npm run obsidian:dev` runs esbuild in watch mode; Obsidian's Hot Reload plugin (community) picks up changes automatically. Documented in the plugin README.

**Uninstall:** disable in Obsidian; optionally `npm run obsidian:unlink` to remove the symlink. Memory docs remain in Arango until either reinstall (which will full-sync delete them via `currentKeys=[]`) or manual cleanup.

**No CI build for the plugin in this scope.** Plugin tests run via `npm test` inside `obsidian-plugin/`; the repo root's CI workflow runs them as `npm --prefix obsidian-plugin test` only if the user opts in (out-of-scope for the first iteration; the manual smoke covers correctness).

## 7. Error handling

| Failure | Surface | Behavior |
|---|---|---|
| Bridge unreachable | plugin ingest | Status bar `✗ bridge down`. Queue intact. Next save retries. |
| Bridge timeout (>30s) | plugin ingest | `AbortSignal.timeout(30_000)` aborts. Status bar `✗ timeout`. |
| `:8081` embedding server down (bridge 503) | plugin ingest | Bridge returns structured `{error:"embedding_server_unreachable"}`. Plugin shows the message. |
| Per-note embed failure (empty body, model error) | plugin ingest | Bridge returns `perNote.outcome="failed"` with `reason`. Plugin status bar `⚠ synced N · M failed`. Console logs the failed keys. |
| Empty body on `memory:true` note | plugin ingest | Plugin filters before sending: empty body → drop, `console.warn`. No round-trip. |
| Tenant filter bug returns private row to a public query | search | Bridge integration test 10 (§8) is the regression guard. Hard fail. |
| Unknown `memory_kind: foobar` | plugin ingest | Plugin validates against the enum; unknown → fallback to `settings.defaultKind`, `console.warn`. |
| Stale-delete deletes wrong scope | bridge | AQL anchors to `tenant_id == @tenant AND source == "obsidian"`. Integration test 5 (§8) is the regression guard. |
| Bridge restart mid-flight | plugin ingest | `requestUrl` rejects with `net::*`. Plugin treats as "bridge unreachable". |
| Overlapping saves | plugin ingest | Single in-flight promise + queue. Second save buffered; flushed when first resolves. |
| Settings: invalid bridge URL | plugin save | `new URL(value)` validation. Inline error; no network call until valid. |
| Plugin uninstall leaves orphans | — | Reinstall + first save will full-sync delete (`currentKeys=[]`). Document in plugin README. |
| Note title contains characters Arango forbids in `_key` | bridge | `_key` is `sha1(note.key)`; the human key lives in `note_key`. Sha1 makes any UTF-8 path safe. |
| Vault has 10k+ flagged notes | plugin ingest | `currentKeys` payload ~500KB JSON. Acceptable. Future-proofing to a hash-of-set is out of scope. |

## 8. Testing

### 8.1 Plugin tests (Vitest, `obsidian-plugin/test/`)

**Unit (pure functions, no Obsidian API):**

| # | Subject | Asserts |
|---|---|---|
| 1 | `parseNoteFrontmatter` | `memory:true` + `memory_kind:fact` → `{ shouldIngest:true, kind:"fact" }` |
| 2 | `parseNoteFrontmatter` | missing `memory:true` → `{ shouldIngest:false }` |
| 3 | `parseNoteFrontmatter` | unknown `memory_kind:foobar` → fallback to `settings.defaultKind`, warn emitted |
| 4 | `deriveNoteKey` | `daily/2026-05-18.md` → `obsidian://daily/2026-05-18.md` |
| 5 | `stripMdx` (ported from posts.mjs) | strips imports/jsx/markdown punctuation, collapses whitespace |
| 6 | `buildIngestPayload` | groups queued notes, includes `currentKeys` from vault scan, omits empty bodies |
| 7 | `scopeToFilters` | toggle `"private"` → `{ kinds:["observation","fact","decision"], tenants:["private"] }` |
| 8 | `scopeToFilters` | toggle `"both"` → 4 kinds × 2 tenants |

**Integration (in-memory `Vault` stub):**

| # | Subject | Asserts |
|---|---|---|
| 9 | save event → debounce → single batch | 3 saves within 2s → 1 ingest call with 3 notes |
| 10 | overlapping ingest | save during in-flight → second batch flushes after first resolves; both notes appear |
| 11 | empty body filtered | `memory:true` + empty body → never sent; warn logged |
| 12 | sidebar fetch cancellation | new query while old in flight → old controller aborted |

### 8.2 Bridge tests (xUnit, ArangoDB integration via `MemoryStoreSchemaTests.CreateUniqueDb`)

**MemoryStore (new methods):**

| # | Name | Asserts |
|---|---|---|
| 1 | `UpsertNoteAsync_FreshNote_WritesOneDoc` | one doc in `memory_observations`, status=ready, tenant=private |
| 2 | `UpsertNoteAsync_SameNoteTwice_SecondIsCacheHit` | second call reports `Cached`, no extra embed |
| 3 | `UpsertNoteAsync_HashChanges_ReembedsAndOverwrites` | text edit → new hash → embed runs, doc updated |
| 4 | `UpsertNoteAsync_KindRoutesToCorrectCollection` | kind=Fact → writes to `memory_facts`, not `memory_observations` |
| 5 | `DeleteStaleNotesAsync_ScopedByTenant_AndSource` | seeds 1 post + 2 obsidian notes + 1 private-tenant non-obsidian doc; full-sync delete leaves post AND non-obsidian doc untouched, removes only the 2 obsidian notes |
| 6 | `DeleteStaleNotesAsync_DoesNotTouchMemoryPosts` | seed 5 posts; full-sync delete with `currentKeys=[]` → 0 posts deleted |

**ContentRagEndpoints:**

| # | Name | Asserts |
|---|---|---|
| 7 | `IngestNotes_RoundTrip_ReturnsCounts` | POST with 3 notes (1 new, 1 cached, 1 failed-empty) → response counts match per-outcome |
| 8 | `Search_BackCompat_DefaultsToPostsPublic` | POST without `kinds`/`tenants` returns only public posts (regression guard for `/dev-search`) |
| 9 | `Search_Kinds_FiltersToObservation` | `kinds=["observation"]` returns only observation rows |
| 10 | `Search_TenantIsolation_PrivateNeverLeaks` | identical-embedding public post + private observation; `tenants=["public"]` returns only the post even though the observation could score higher. **Critical regression guard.** |
| 11 | `Search_UnionOrdering_RanksByScore` | top result is highest-score doc regardless of source collection |

**Net change:** Bridge +6 MemoryStore tests + 5 endpoint tests = 11 new C# integration tests. Plugin +12 Vitest tests.

### 8.3 Manual smoke

```bash
make up                                  # full stack including llama-servers
npm run rag:reindex                      # populate posts (existing path)

# One-time plugin install
cd obsidian-plugin && npm install && npm run build && npm run link
# Enable "Darbee Memory" in Obsidian → Community Plugins

# Inside Obsidian:
#   Create note "daily/test.md" with frontmatter:
#     memory: true
#     memory_kind: observation
#   Type body "test note about cast iron pans"
#   Save → wait 2s → status bar "✓ synced 1"

# Sidebar (open via command palette: "Darbee Memory: Open sidebar"):
#   Query "cast iron", scope=Private → 1 result, kind=observation badge, tenant=private badge
#   Query "rv life",   scope=Both    → posts interleaved with notes, scopes visible

# Edit body → save → "✓ synced 1" (overwrite, hash differs from previous)
# Remove memory:true → save → "✓ synced 0 · 1 stale deleted"

# Verify Arango directly:
curl -s -u root:password -X POST \
  http://localhost:8529/_db/darbees_knowledge/_api/cursor \
  -H 'content-type: application/json' \
  -d '{"query":"FOR d IN memory_observations FILTER d.tenant_id==\"private\" RETURN d.note_key"}'
```

### 8.4 Out of scope

- Playwright/E2E in headless Obsidian (no clean headless runner; manual smoke covers UX).
- Performance benchmarks at 10k+ notes (single-host workload, plenty of slack).
- C# unit tests for endpoint wiring (covered by integration tests 7-11).

## 9. Open gaps and follow-ups

1. **Chat-over-memory ("answer-from-context")** — new bridge endpoint that retrieves top-k from memory and pipes through Qwen3.6-27B for a synthesized answer. Plus an Answer button in `/dev-search` and the Obsidian sidebar. Substantial scope (prompt template, streaming response, UI). Separate spec.

2. **Wikilink edges → `memory_edges`** — parse `[[wikilinks]]` in ingested notes, persist edges. Lights up the graph collection. Enables a backlink panel in the sidebar and AQL traversals ("notes that link to this one"). Separate spec — entity extraction belongs alongside.

3. **Entity extraction via chat model** — run posts/notes through Qwen3.6-27B to extract `MemoryKind.Entity` rows (people, places, projects) and `Summary` rows. Knowledge-graph foundation. Separate spec.

4. **Plugin CI** — wire `npm --prefix obsidian-plugin test` into the GitHub Actions workflow. Out of scope here because the manual smoke covers correctness and the plugin source is small.

5. **Tenant model beyond `public` / `private`** — multi-author scenarios, per-collection ACLs, or a shared "family" tenant. Not needed today; the data model already supports it (`tenant_id` is a free-text field).

6. **Bridge auth on localhost** — currently none. If the workstation is ever shared or networked, the plugin would need to authenticate. Out of scope; tracked here as a known constraint.

## 10. Decisions log

| Decision | Rejected alternative | Reason |
|---|---|---|
| Frontmatter opt-in (`memory: true`) | All notes ingested by default | Daily journal entries are private by intent. Opt-in avoids accidental ingestion of one-time scratch notes. |
| Native Obsidian plugin | Manual `npm run memory:ingest` CLI | Author already lives in Obsidian; on-save automation removes a friction point and matches the image-watcher pattern in spirit. |
| Same Arango DB, tenant_id filter | Separate `darbees_personal` database | One schema, one connection, one backup target. Reuses MemoryStore code as-is. Tenant-isolation regression test is the bright line. |
| Full-sync delete via `currentKeys` envelope | Soft-only / GC-CLI / explicit deletes | Same model as `rag-reindex` — consistent, auditable, no plugin-side state needed. Reinstall is safe (next save will re-establish). |
| `source == "obsidian"` filter on stale-delete | Tenant-only filter | Defense in depth: a future CLI that writes Arango-direct private-tenant docs (no `source` field) must not be wiped by the plugin. Bridge-side AQL pins the scope. |
| Vitest for plugin tests | Jest / node:test | Vitest is the de-facto standard in the Obsidian plugin ecosystem and supports TS/ESM out of the box. |
| Search response carries `kind` and `tenant` per row | Group results by collection in the response | Flat list with badges renders better in a narrow sidebar; grouping is a future UI affordance, not a wire-format requirement. |
| `_key = sha1(note.key)` with human key in `note_key` | URL-encode the path into `_key` | Arango `_key` forbids slashes and limits length; sha1 makes any path safe and the human-readable key is preserved alongside for queries and stale-delete. |
| `currentKeys` sent on every ingest | Hash-of-set sent once per session | Simple, stateless, no client-side bookkeeping. 500KB at 10k notes is comfortably within Obsidian's `requestUrl` budget. |
| 2s debounce default | Per-keystroke ingest | Avoids embedding intermediate drafts. 2s aligns with how Obsidian's own autosave windows behave. |
| `memory_tenant` overridable per-note | Tenant fixed by settings | Lets the author flag a single note as `public` (e.g., a research summary they intend to publish later) without changing the plugin default. Rare but cheap to support. |
| No entity extraction in this spec | Roll it in with the plugin | Entity extraction needs the chat model and a prompt template, and is itself a 2-3 sub-design (kinds, edge schema, latency budget). Separate spec keeps scope manageable. |

---

**End of spec.**
