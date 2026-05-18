# Content RAG UI — Design Spec

**Date:** 2026-05-17
**Status:** Draft, ready for review
**Branch (proposed):** `feature/content-rag-ui` (off `master`)
**Related:** Layers on top of the [Content RAG design](2026-05-16-content-rag-design.md) and its merged implementation (PR #2). Depends on `rag-reindex` populating `memory_posts` in Arango.

---

## 1. Problem

PR #2 shipped the content RAG data layer: `memory_posts` collection in Arango, three bridge endpoints (`reindex-posts`, `memory/search`, `migrate-embeddings`), and the `npm run rag:reindex` orchestrator. The retrieval primitive works — `curl POST /api/memory/search` returns ranked posts in ~80ms — but nothing on the Astro side calls it. The data layer is invisible to anyone using the site.

In parallel, `scripts/related-rebuild.mjs` still computes pairwise post similarity by calling LM Studio through `scripts/lib/lmstudio.mjs` and embedding every post a second time. After the stack swap to llama.cpp + qwen3-embedding-8b (sealed in [[hardware-stack]] memory), the LM Studio dependency is stale and the duplicate embedding work is wasteful — Arango already has every post's vector at higher quality.

## 2. Goals

1. **Dogfood the bridge from a real Astro surface** — a `/dev-search` route during `npm run dev` that the operator can use while authoring to discover what they've already written about a topic.
2. **Unify the embedding stack on the bridge** — `related-rebuild.mjs` reads body vectors from `memory_posts` instead of embedding posts a second time via LM Studio. The existing related-posts component on every post page silently benefits.
3. **Keep `src/data/related-posts.json` shape unchanged.** No consumer changes; the rewrite is purely internal to the script.
4. **No production-facing surface.** No public bridge dependency, no Cloudflare Workers, no live API. The `/dev-search` page deploys to production but the fetch path it relies on (a Vite dev-server proxy) doesn't exist in static builds — the page degrades gracefully with a "local-only" notice.

## 3. Non-goals

- **No CORS on the bridge.** The dev-server proxies the request server-side; no cross-origin browser fetch.
- **No public search UI.** A site-wide search box for visitors is a future project — Cloudflare-side hosting, Workers AI / Vectorize migration, auth on the bridge, etc. — none of which is in scope here.
- **No new related-posts block.** The existing component continues to work. This spec only swaps the engine underneath.
- **No build-time auto-chain.** `npm run build` does not implicitly run `rag:reindex` or `related:rebuild`. Author runs them manually before commit. (Matches the current pattern.)
- **No Astro server endpoints.** Astro is in static-output mode; runtime endpoints don't deploy reliably without an adapter. Vite's dev-server `server.proxy` covers the dev case natively.
- **No second LLM round-trip for chat-style answers.** `/dev-search` returns retrieval results only; it does not pipe them through Maverick to compose an answer.
- **No Playwright test for `/dev-search`.** Out-of-CI live-service dependency.

## 4. Architecture

```
┌───────────────────────────────────────────────────────────────────────────┐
│  npm run dev                                                              │
│                                                                           │
│   browser ── fetch ──► /dev-api/search ─► Vite proxy ─► bridge :5000      │
│      │                                       │              │             │
│      │                                       │              ▼             │
│   /dev-search.astro          (only exists in dev)   /api/memory/search    │
│   (search box + results)                                    │             │
│                                                             ▼             │
│                                                       embeds + AQL        │
│                                                       over Arango         │
│                                                                           │
│  npm run build (manual prebuild)                                          │
│                                                                           │
│   scripts/rag-reindex.mjs ─► bridge ─► Arango memory_posts                │
│   scripts/related-rebuild.mjs ─► Arango memory_posts (AQL, read-only)     │
│        │                                                                  │
│        ▼ writes                                                           │
│   src/data/related-posts.json                                             │
│        │                                                                  │
│        ▼ consumed at build time                                           │
│   existing related-posts component on each post page                      │
└───────────────────────────────────────────────────────────────────────────┘
```

**Two surfaces, two flows:**

- **Live (dev-only):** browser → Vite proxy → bridge `/api/memory/search`. The Vite dev server owns the proxy; in static builds the proxy is absent, the fetch 404s, and the page shows a "local-only" notice.
- **Build-time enrichment:** `related-rebuild.mjs` reads body vectors directly from Arango via AQL, runs the existing pure helpers (`cosineSimilarity`, `topRelated`, `buildRelatedMap`), writes the same `related-posts.json`. No bridge involvement during this script — Arango is the single source of truth for vectors after `rag:reindex` has run.

## 5. Components

### 5.1 Vite proxy configuration

`astro.config.ts` gains a `vite.server.proxy` entry:

```js
vite: {
  plugins: [tailwindcss()],
  server: {
    proxy: {
      '/dev-api/search': {
        target: process.env.BRIDGE_URL || 'http://localhost:5000',
        changeOrigin: true,
        rewrite: () => '/api/memory/search',
      },
    },
  },
},
```

Path is `/dev-api/search` rather than `/api/...` to leave room for future static `/api/*` Astro endpoints without collision. `process.env.BRIDGE_URL` honors the existing pattern used by `scripts/lib/bridge-client.mjs` so a non-default bridge URL is configurable without touching code.

The proxy exists only when the Vite dev server runs (`npm run dev`). Production builds produce static HTML/JS that browsers serve without Vite involvement — the path resolves to a 404.

### 5.2 `/dev-search` Astro page

`src/pages/dev-search.astro` — single-file page using the existing `BaseLayout`. Inline `<script>` rather than a separate client module (small enough that splitting adds friction).

**Form contents** — query text input, `k` integer (default 5, clamped 1–20), submit button. On submit, `POST /dev-api/search` with `{ query, k }`. Renders results as DaisyUI cards (consistent with site visual language) showing title, score (3 decimals), `matchedKind`, snippet, and `collection/slug` footer. Each card is an anchor to `/{collection}/{slug}/`.

**Dev-only awareness** — a `{!isDev && <alert>}` branch (using `import.meta.env.DEV`) renders a "this page only works during `npm run dev`" notice on production deploys. The form still renders so the page doesn't look broken — but the user is warned the fetch will fail.

**Meta** — line at the bottom shows `queryEmbedMs` and `searchMs` from the response. `<meta name="robots" content="noindex">` in the page head so search engines don't surface the dev surface in production.

**Accessibility:**
- Every form control has an explicit `<label for="...">`. No placeholder-only labeling.
- Results container has `role="region"` + `aria-live="polite"` + `aria-atomic="false"` so screen readers announce new result counts on each submit without flooding.
- A separate `<p id="status" role="status">` announces transient state ("Searching…", "No results.", error messages) for AT users.
- Submit button is disabled (`aria-disabled="true"`) while a request is in flight.

**Request cancellation** — the inline `<script>` keeps a single `AbortController` per page lifetime. A new submit while a previous fetch is still in flight calls `controller.abort()` first, then starts the new request with a fresh controller. Prevents the user from seeing stale results from a slow previous query racing with a fast new one.

**Request timeout** — every fetch uses `AbortSignal.timeout(30_000)` (30 seconds), composed with the cancellation controller via `AbortSignal.any([ctl.signal, AbortSignal.timeout(30_000)])`. A timeout surfaces as a styled error in the status line rather than hanging the UI.

**No autocomplete, no debounce, no filters.** Submit-button only. Avoids hammering qwen3 on every keystroke; matches the "dev tool" intent. Collection filter is a future enhancement (would require a small bridge-side change to support filtering, see §9).

### 5.3 `scripts/lib/arango-client.mjs` (new)

Minimal HTTP wrapper for Arango. Mirrors `scripts/lib/bridge-client.mjs` in shape.

Exports:
- `ArangoError extends Error` with `status` and `body` fields.
- `async function runAql(query, bindVars = {}, { timeoutMs = 30_000 } = {})` — POSTs to `/_db/{ARANGO_DATABASE}/_api/cursor`, returns the `.result` array.

Reads env vars: `ARANGO_URL` (default `http://localhost:8529`), `ARANGO_USER` (`root`), `ARANGO_PASSWORD` or `ARANGO_ROOT_PASSWORD` (preferring the explicit one), `ARANGO_DATABASE` (`darbees_knowledge`). Uses Basic auth via the `Authorization` header.

**Request timeout** — every fetch is wrapped in `AbortSignal.timeout(timeoutMs)`. Default 30s. Caller can override per call. A timeout produces `ArangoError("Arango timeout after Nms: ...")`.

**No connection pooling, no retry logic.** At the script call rate (one AQL per `related-rebuild` run) both would be over-engineering. A future high-frequency caller can layer them on without changing the interface.

**Same pattern for `bridge-client.mjs`** — as part of this work, retroactively add the same `AbortSignal.timeout` to `bridgePost` (existing helper has no timeout today, so a hung bridge would hang any script that calls it). Default 30s, overridable per call. Net change to `bridge-client.mjs`: one constructor option, one `signal:` field on the fetch call, one new error message branch.

### 5.4 `scripts/related-rebuild.mjs` rewrite

**Preserved** (pure helpers, unchanged):
- `cosineSimilarity(a, b)`
- `topRelated(vector, others, { limit, floor })`
- `buildRelatedMap(posts, opts)`

**Removed:**
- `cacheKey(contentHashValue, embeddingModelId)` and its test (dead code after rewrite — no other consumers).
- The `main()` orchestrator's calls to `createClient()` from `./lib/lmstudio.mjs`, `embedText`, and `contentHash` from `./lib/posts.mjs`. Those helpers stay in `posts.mjs` (other scripts may use them) but `related-rebuild` no longer imports them.

**Replaced** — new `main()`:

```javascript
async function main() {
	const aql = `
		FOR doc IN memory_posts
		  FILTER doc.tenant_id == "public"
		  FILTER doc.status == "ready"
		  FILTER doc.vector_kind == "body"
		  RETURN { collection: doc.collection, id: doc.slug, vector: doc.embedding }
	`;

	let rows;
	try {
		rows = await runAql(aql);
	} catch (err) {
		// ArangoError → human-readable hint (esp. for 404 = missing database);
		// other → stack trace. Always exit 1.
	}

	if (rows.length === 0) {
		console.error('No ready vectors in memory_posts. Run `npm run rag:reindex` first.');
		process.exit(1);
	}

	const floor = Number(process.env.RELATED_FLOOR || 0.5);
	const map = buildRelatedMap(rows, { limit: 3, floor });
	const orphans = Object.values(map).filter((r) => r.length === 0).length;

	await mkdir(DATA_DIR, { recursive: true });
	await writeFile(OUT_PATH, `${JSON.stringify(map, null, '\t')}\n`, 'utf8');

	console.log(`${rows.length} posts indexed, ${orphans} with 0 relations (floor=${floor})`);
	console.log(`Wrote ${OUT_PATH}`);
}
```

**Why `vector_kind == "body"`** — the old `embedText()` composition was `title + description + tags + category + body`. The body vector in `memory_posts` is composed from `title + description + tags + category + mentions + body` (per `PostTextComposer.ComposeBody`). Functionally the same intent: whole-post similarity. The summary vector (composed from aiSummary + keyTakeaways + faq) is a different signal — useful for query-style search but not for post-to-post similarity.

**Dimension consistency check** — before calling `buildRelatedMap(rows, ...)`, verify all vectors have the same length:

```javascript
const dims = new Set(rows.map((r) => r.vector.length));
if (dims.size !== 1) {
    console.error(`Inconsistent vector dimensions in memory_posts: ${[...dims].join(', ')}`);
    console.error('This indicates a partial migration. Run `npm run rag:reindex -- --force` to repair.');
    process.exit(1);
}
```

In normal operation this never trips (all vectors come from the same embedding model). It catches the partial-migration footgun: a `migrate-embeddings` mid-flight, or a manual ArangoDB cursor running with stale model config. Fast guard, no perf cost.

**Cache file disposal:** `src/data/related-posts.cache.json` is removed from the repo as part of this commit. The new script doesn't write or read it. Arango is the cache now.

### 5.5 Rename `scripts/lib/lmstudio.mjs` → `openai-compatible.mjs`

The file is well-designed — it already speaks OpenAI-compatible HTTP against any of {llama.cpp `llama-server`, LM Studio, Ollama with the OpenAI shim}. The internal doc comment even says so. The filename is the only thing that's stale, and the staleness has surfaced repeatedly in this session (the user has corrected "we're not using LM Studio anymore" twice). Aligns the JS authoring tooling with the C# rename from PR #2 (`LmStudioEmbeddingClient` → `OpenAiCompatibleEmbeddingClient`).

**Audited consumers** (verified via `grep -rn "lmstudio" scripts/ --include="*.mjs"`):
- `scripts/image-watcher.mjs:69` — `import { createClient } from './lib/lmstudio.mjs'`
- `scripts/geo-fill.mjs:14` — `import { createClient } from './lib/lmstudio.mjs'`
- `scripts/related-rebuild.mjs:59` — `import { createClient } from './lib/lmstudio.mjs'` *(removed by §5.4)*
- `scripts/lib/lmstudio.test.mjs:3` — `import { createClient } from './lmstudio.mjs'`

**Rename plan (one commit):**

1. `git mv scripts/lib/lmstudio.mjs scripts/lib/openai-compatible.mjs`
2. `git mv scripts/lib/lmstudio.test.mjs scripts/lib/openai-compatible.test.mjs`
3. Inside `openai-compatible.mjs`:
   - Update file-header doc comment to remove the implication that LM Studio is the primary backend.
   - Update `LM Studio` substrings in error messages (e.g. `'LM Studio ${where}: response missing key'` → `'LLM ${where}: response missing key'`).
   - Update env-var precedence: read `LLM_CHAT_URL` first, fall back to `LMSTUDIO_URL` (already done in this direction for chat — but the **default** `baseUrl` still reads `LMSTUDIO_URL || DEFAULT_BASE_URL`. Flip the default to `LLM_CHAT_URL || LMSTUDIO_URL || DEFAULT_BASE_URL` and add a one-time `console.warn` when `LMSTUDIO_URL` is the source). Matches the back-compat pattern in the C# bridge from PR #2.
   - Update default `apiKey` precedence: `AI_API_KEY || LMSTUDIO_API_KEY` (currently only reads `LMSTUDIO_API_KEY`). Same back-compat warning.
   - Update default `embeddingModel` from `'text-embedding-qwen3-embedding-8b'` (stale, doubled-up name) to `'qwen3-embedding-8b'` (matches the bridge default in `appsettings.json`).
   - Update default `chatModel` from `'local-model'` to `'llama-4-maverick'` (matches bridge default).
4. Inside `openai-compatible.test.mjs`:
   - Update `import { createClient } from './openai-compatible.mjs'`.
   - Verify the deprecation-warning tests pass (or add them if not present).
5. In `scripts/image-watcher.mjs:69` and `scripts/geo-fill.mjs:14`:
   - Replace `'./lib/lmstudio.mjs'` with `'./lib/openai-compatible.mjs'`.
6. Run `npm run test:scripts` to confirm no regressions. Existing tests for the client (currently named `lmstudio.test.mjs`) should pass under the new path.

**Verification grep** — after all edits, this should return zero matches:

```bash
grep -rn "lmstudio" scripts/ --include="*.mjs"
```

**No client-API surface change.** Public functions (`createClient`, the returned `chat`/`chatJson`/`embed`/`vision`/`listModels` methods) keep their names and signatures. Internal env-var precedence changes are transparent to existing consumers because env vars are read at client-creation time and back-compat is preserved.

**Commit shape** — one commit `refactor(scripts): rename lmstudio.mjs → openai-compatible.mjs`, body explains the C# parallel and the deprecation warnings.

### 5.6 `scripts/check-related-fresh.mjs` (new — freshness check)

Lightweight check that catches the "edited a post, forgot to re-run `rag:rebuild-all`, committed stale related-posts.json" footgun. Compares mtime of `src/data/related-posts.json` against every published MDX file.

**Behavior:**
- Default: warns on stale, exits 0. Build proceeds; the author sees a warning.
- `--strict` flag: exits 1 on stale. Useful for CI or strict local enforcement.
- Missing `related-posts.json` is treated the same way (warning by default, hard fail in strict).
- Lists up to 5 stale post slugs, then "… and N more" if more.

**Shape:**

```javascript
#!/usr/bin/env node
import { stat } from 'node:fs/promises';
import { listPosts, PRIMARY_COLLECTIONS } from './lib/posts.mjs';

const RELATED_POSTS = 'src/data/related-posts.json';

async function main() {
	const strict = process.argv.includes('--strict');

	let relatedMtime;
	try {
		relatedMtime = (await stat(RELATED_POSTS)).mtimeMs;
	} catch (err) {
		if (err.code !== 'ENOENT') throw err;
		console.warn(`⚠ ${RELATED_POSTS} is missing. Run \`npm run rag:rebuild-all\`.`);
		process.exit(strict ? 1 : 0);
	}

	const posts = await listPosts({ collections: PRIMARY_COLLECTIONS });
	const stale = [];
	for (const p of posts) {
		const m = (await stat(p.filePath)).mtimeMs;
		if (m > relatedMtime) stale.push(`${p.collection}/${p.id}`);
	}

	if (stale.length === 0) {
		console.log(`✓ related-posts.json is up-to-date (${posts.length} posts checked)`);
		return;
	}

	console.warn(`⚠ related-posts.json is stale for ${stale.length} post(s):`);
	for (const s of stale.slice(0, 5)) console.warn(`    ${s}`);
	if (stale.length > 5) console.warn(`    ... and ${stale.length - 5} more`);
	console.warn('  Run `npm run rag:rebuild-all` to refresh.');
	process.exit(strict ? 1 : 0);
}

main().catch((err) => {
	console.error(err.stack || err.message);
	process.exit(1);
});
```

**Wired into `prebuild`** — see §5.7. Non-blocking by default, so CI builds without Arango still succeed; authors get a visible nudge when they forgot.

**Tests** — `scripts/check-related-fresh.test.mjs`:
- Uses `mkdtemp` + chdir for isolation (consistent with `posts.test.mjs`'s fixture pattern).
- Tests: (a) all fresh → exit 0 with success message, (b) one MDX newer → warn, exit 0 (default), (c) one MDX newer with `--strict` → exit 1, (d) missing related-posts.json → warn, exit 0 (default), (e) missing with `--strict` → exit 1.

**Why this is best practice (not over-engineering):**
- The stale-related-posts symptom is invisible at build time. Author commits, deploys, never knows readers are seeing stale "related" suggestions.
- The check is read-only on the file system, no dependencies, runs in milliseconds.
- Default-warn-not-fail keeps CI green and respects the offline-edit workflow (author can commit MDX without Arango up; the warning surfaces on next build/CI).

**`posts.mjs` requirement** — `listPosts()` already returns posts with `.filePath` (verify before implementation; if it doesn't expose this, add it — it's a one-line change in the walker).

### 5.7 `package.json` script additions

Two new entries:

```json
"rag:rebuild-all":   "npm run rag:reindex && npm run related:rebuild",
"rag:check-fresh":   "node scripts/check-related-fresh.mjs",
"prebuild":          "node scripts/check-related-fresh.mjs"
```

- `rag:rebuild-all` — convenience for the common edit → rebuild flow. Subscripts remain individually runnable.
- `rag:check-fresh` — direct script invocation (also supports `npm run rag:check-fresh -- --strict` for CI).
- `prebuild` — npm-convention hook that runs automatically before `npm run build`. Non-strict by default — warns, doesn't fail.

The existing `postbuild` (which runs `check:links`) stays untouched. Now `npm run build` is effectively: prebuild (fresh check) → astro build + pagefind → postbuild (check:links).

### 5.8 `CLAUDE.md` update

Two new rows in the Authoring-scripts command table:

| Task | Command | When |
|------|---------|------|
| Reindex + rebuild related | `npm run rag:rebuild-all` | After adding/editing posts, before commit |
| Check related-posts freshness | `npm run rag:check-fresh` | Manual sanity check (also runs automatically before `npm run build`) |

## 6. Build pipeline

**No automatic regeneration** (no auto-`rag:reindex` or auto-`related:rebuild`). The author chains those manually because they require Arango + bridge up, which CI doesn't have.

**Automatic freshness check** — `prebuild` hook runs `scripts/check-related-fresh.mjs` in non-strict mode. Warns when stale, never fails. CI builds without Arango still succeed; local devs get a visible nudge when they forgot to regenerate.

**Authoring workflow:**

```
edit posts
  ↓
npm run geo:fill <new-post.mdx>          # if frontmatter needs filling
  ↓
npm run rag:rebuild-all                  # Arango + related-posts.json
  ↓
npm run build                            # prebuild (fresh check, warn-only)
                                         #   → astro build + pagefind
                                         #   → postbuild (check:links)
  ↓
git commit + push
```

**CI** runs `astro build` against committed `related-posts.json`. The prebuild fresh-check warns about stale entries but doesn't break the build. Phase 11 G2 (Arango service container in CI) would let CI regenerate; out of scope here.

**Strict CI option** — if you later want CI to *fail* on stale related-posts, change the workflow's build command to `npm run rag:check-fresh -- --strict && npm run build`. Not adopted here because it requires the regeneration step to also be in CI.

## 7. Error handling

| Failure mode | Surface | Behavior |
|---|---|---|
| Bridge unreachable from Vite proxy | `/dev-search` fetch | Vite returns 502; status line shows styled error |
| Bridge returns 503 (e.g., embedding-config mismatch) | `/dev-search` fetch | Status line shows the bridge's structured error |
| Bridge returns 400 (malformed request) | `/dev-search` fetch | Status line shows the error message |
| Bridge takes >30s | `/dev-search` fetch | `AbortSignal.timeout` aborts; status line shows "Search timed out" |
| User submits while previous fetch in flight | `/dev-search` fetch | Previous controller aborted; new request runs; status line replaces "Searching…" |
| Arango unreachable from `related-rebuild` | script | Exits 1 with `ArangoError` message |
| Arango takes >30s | script | `AbortSignal.timeout` aborts; ArangoError with "Arango timeout after 30000ms" |
| Arango 404 ("database not found") | script | Exits 1 with the `darbees_knowledge` create-DB curl hint |
| `memory_posts` empty | script | Exits 1 with "run `npm run rag:reindex` first" hint |
| Inconsistent vector dimensions in `memory_posts` | script | Exits 1 with "partial migration; run `rag:reindex --force`" hint |
| `prebuild` finds stale related-posts.json | build | Warns with stale slug list; build continues |
| `prebuild` finds missing related-posts.json | build | Warns; build continues (build will see empty related lists per the existing component fallback) |
| `prebuild` runs with `--strict` and detects stale/missing | build | Exits 1; build fails |
| `/dev-search` accessed in production | rendered page | "local-only" alert visible; fetch 404s into a styled error in the status line |

## 8. Testing

JS suite (`node --test 'scripts/**/*.test.mjs'`) — no Arango or LLM required. All new tests use fetch-stubbing and temp-directory patterns consistent with existing tests.

**New unit tests** — `scripts/lib/arango-client.test.mjs` (6 tests, stubbed `globalThis.fetch`):

| # | Name | Asserts |
|---|---|---|
| 1 | `runAql_2xx_returns_result_array` | parsed `.result` returned |
| 2 | `runAql_4xx_throws_ArangoError_with_body` | thrown error has `status` + parsed body |
| 3 | `runAql_404_thrown` | covers the "database not found" path |
| 4 | `runAql_network_failure_throws_ArangoError` | fetch throws → ArangoError with "unreachable" message |
| 5 | `runAql_sends_basic_auth_from_env` | Authorization header matches `Basic base64(user:pass)` |
| 6 | `runAql_aborts_after_timeoutMs` | `AbortSignal.timeout` cancels the fetch; ArangoError mentions timeout |

**New unit tests** — `scripts/check-related-fresh.test.mjs` (5 tests, temp-dir fixtures):

| # | Name | Asserts |
|---|---|---|
| 1 | `freshness_check_all_fresh_exits_0_with_ok_message` | no warnings, success message printed |
| 2 | `freshness_check_one_mdx_newer_warns_exits_0` | warning printed, slug listed, exit code 0 |
| 3 | `freshness_check_one_mdx_newer_strict_exits_1` | warning printed, exit code 1 |
| 4 | `freshness_check_missing_related_posts_warns_exits_0` | "missing" warning, exit code 0 |
| 5 | `freshness_check_missing_related_posts_strict_exits_1` | "missing" warning, exit code 1 |

**New unit tests** — `scripts/lib/bridge-client.test.mjs` extension (1 test added):

| # | Name | Asserts |
|---|---|---|
| 7 | `bridgePost_aborts_after_timeoutMs` | timeout configurable via option, default 30s, ArangoError-equivalent on timeout |

**Modified existing tests:**
- `scripts/related-rebuild.test.mjs` — keep `cosineSimilarity`, `topRelated`, `buildRelatedMap` (pure-helper coverage). Delete `cacheKey` test (function removed).
- `scripts/lib/lmstudio.test.mjs` → renamed `openai-compatible.test.mjs`. Imports updated. Existing tests pass under the new path.

Net change: -1 deleted (`cacheKey`), +12 added (6 arango + 5 fresh-check + 1 bridge-client timeout) → **JS suite goes from 38 → 49 passing**.

**No new C# tests** — the bridge surface (`/api/memory/search`) is already covered by the existing `ContentRagEndpointsTests.HandleSearchAsync_*` integration tests.

**Manual smoke** documented in the spec — run during implementation to verify end-to-end behavior:

```bash
# Prereqs: llama-server on :8080 + :8081, make up, npm run rag:reindex
npm run dev
# Browser: http://localhost:4321/dev-search
# Query: "cast iron pan", k=3, submit
# Expect: top hit = what-we-pack-first-in-the-rv with score ≥ 0.4
# Expect: meta line shows two timings
# Expect: clicking a card navigates to /<collection>/<slug>/

# related-rebuild smoke
rm -f src/data/related-posts.json
npm run related:rebuild
# Expect: "12 posts indexed, ≤2 with 0 relations"
# Expect: src/data/related-posts.json shape matches the previous format
git diff src/data/related-posts.json    # eyeball deltas vs last committed

# Error-path checks
podman stop darbees-dev-arango-1
npm run related:rebuild
# Expect: exit 1, "Arango error: ..." message, no file overwrite

# Production-build safety
npm run build
# Expect: build succeeds, dist/dev-search/index.html exists
# Loading it in a browser without the dev server: form renders, alert visible, fetch 404s into a styled error
```

## 9. Open gaps and follow-ups

In-scope items previously listed here have been resolved (see §5.5 for the `lmstudio.mjs` rename, §5.6–§5.7 for the freshness check + prebuild hook). Remaining out-of-scope items:

1. **Public "ask the blog" UI** — for site visitors, not just the local author. Requires either: (a) hosting the bridge somewhere reachable from `darbeeschasingrainbows.com` (a tunneled local exposure, or a hosted instance), or (b) re-platforming retrieval on Cloudflare Workers AI + Vectorize. Either is a separate, large project with its own scope (auth, rate-limiting, abuse handling, cost model). Out of scope here — this spec is explicit about "no production-facing surface" (§3).

2. **`/dev-search` collection filter.** Adding a `<select name="collection">` to the form is trivial UI; the bridge's `SearchAsync` doesn't currently accept a collection filter though, so this would require a bridge change first (`SearchRequest.Collections: List<string>?` → AQL `FILTER doc.collection IN @collections`). One commit each side. Deferred until authoring usage shows we need it.

3. **`/dev-search` "ask Maverick to answer from these" follow-up.** A button next to the results that POSTs the retrieved snippets to a new bridge endpoint (`/api/chat/answer-from-context`) which builds a RAG prompt and returns Maverick's answer. Substantial bridge work (new endpoint, prompt template, streaming response handling) and material UI work (chat-style answer pane). Separate spec.

4. **CI Arango service container** — Phase 11 G2 territory. Would let CI run `rag:reindex` + `related:rebuild` itself, which would let us flip `prebuild` to `--strict` and have build genuinely fail on stale `related-posts.json`. Currently CI is fresh-check-warn-only because Arango isn't available there.

## 10. Decisions log

| Decision | Rejected alternative | Reason |
|---|---|---|
| Vite `server.proxy` for the dev fetch | Astro server endpoint (`src/pages/api/search.json.ts`) | Astro is in static-output mode (no adapter). Server endpoints need `output: 'server'` or `'hybrid'` to run at request time — Vite proxy is native to the dev server and falls out automatically in production builds. |
| Vite proxy at `/dev-api/search` | `/api/search` | Avoids collision with any future static `/api/*` Astro endpoints. |
| Inline `<script>` in `dev-search.astro` | Separate `src/scripts/dev-search.client.ts` module | Page is small (~50 lines of JS). Splitting adds friction without payoff. Move if it grows. |
| Submit-button only (no debounce, no suggest-as-you-type) | Live search | Avoids hammering qwen3 (~50ms per embed) on every keystroke. Dev tool intent — single-query workflow. |
| Body vector for related-rebuild | Summary vector | The old `embedText` was a whole-post composition. Body vector matches that intent. Summary vector is for query-style search (different signal). |
| `related-rebuild` reads Arango directly | Bridge endpoint that dumps all vectors | One consumer of Arango reads doesn't justify a new bridge endpoint. The AQL is tiny; auth is the same Basic pattern the script already needs. |
| Delete `cacheKey` + its test | Keep as exported helper | No remaining consumers after the rewrite. Dead code is dead code. |
| Keep `embedText` and `contentHash` in `posts.mjs` | Delete | Other authoring scripts (`geo-fill.mjs`, `image-watcher.mjs`) may use them — verified via grep at implementation time. Don't break unrelated tools. |
| Convenience `rag:rebuild-all` script | Two separate invocations always | One-line script keeps the common "regenerate everything related-derived" flow ergonomic. Subscripts remain individually runnable. |
| No automatic prebuild chaining | `prebuild` npm hook | Author may commit only docs changes (no MDX edits → no need to re-embed). Forcing the chain wastes bridge time and breaks CI without Arango. Manual remains explicit. |
| `<meta name="robots" content="noindex">` on the dev page | Allow indexing | Page deploys to production (Astro static build doesn't filter it out); search engines shouldn't surface "Search the blog (dev)" as a result. |
| Rename `lmstudio.mjs` → `openai-compatible.mjs` in this spec's scope | Defer to a follow-up commit | The rename touches the same four files (`related-rebuild`, `image-watcher`, `geo-fill`, the test) that this spec already edits or audits, and the staleness has already surfaced twice in this brainstorm. Bundling them avoids a second drive-by PR. |
| Freshness check warns by default, `--strict` opts into fail | Hard-fail by default | CI doesn't have Arango, so a default-fail would block every PR that touches MDX. Warn-by-default surfaces the footgun locally without breaking CI. Strict mode stays available for authors who want stricter pre-commit gates and for the future CI-with-Arango setup (§9 #4). |
| 30s `AbortSignal.timeout` on every fetch (proxy, arango-client, bridge-client) | Unbounded fetch | A hung llama-server or wedged Arango would hang the UI/script indefinitely. 30s is well above the p99 (search ~80ms, AQL ~50ms, embed ~50ms) so legitimate requests never trip it. |
| Retroactively add timeout to existing `bridge-client.mjs` | Leave bridge-client alone, only timeout the new code | Inconsistent timeouts are a worse failure mode than no timeouts (script hangs only when it calls the un-timed-out helper, surprising the author). Cheap to add now, no behavior change in the happy path. |
| Dimension consistency check before `buildRelatedMap` | Trust the data | A partial `migrate-embeddings` or a manual cursor with stale model config could leave mixed-dimension vectors in `memory_posts`. The check is one `Set` construction with no perf cost and catches a class of bug that would otherwise produce silent garbage similarity scores. |

---

**End of spec.**
