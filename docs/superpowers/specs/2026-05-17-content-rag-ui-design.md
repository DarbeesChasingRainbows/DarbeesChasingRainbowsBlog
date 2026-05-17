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

Form: query text input, k integer (default 5, clamped 1–20), submit button. On submit, `POST /dev-api/search` with `{ query, k }`. Renders results as DaisyUI cards (consistent with site visual language) showing title, score (3 decimals), `matchedKind`, snippet, and `collection/slug` footer. Each card is an anchor to `/{collection}/{slug}/`.

A `{!isDev && <alert>}` branch (using `import.meta.env.DEV`) renders a "this page only works during `npm run dev`" notice on production deploys. The form still renders so the page doesn't look broken — but the user is warned the fetch will fail.

Meta line at the bottom shows `queryEmbedMs` and `searchMs` from the response.

Adds `<meta name="robots" content="noindex">` so search engines don't index the dev surface in production.

**No autocomplete, no debounce, no filters.** Submit-button only. Avoids hammering qwen3 on every keystroke; matches the "dev tool" intent. Future enhancements (collection filter, debounced suggest-as-you-type, "ask Maverick to answer from these" follow-up) can layer in without restructuring.

### 5.3 `scripts/lib/arango-client.mjs` (new)

Minimal HTTP wrapper for Arango. Mirrors `scripts/lib/bridge-client.mjs` in shape.

Exports:
- `ArangoError extends Error` with `status` and `body` fields.
- `async function runAql(query, bindVars = {})` — POSTs to `/_db/{ARANGO_DATABASE}/_api/cursor`, returns the `.result` array.

Reads env vars: `ARANGO_URL` (default `http://localhost:8529`), `ARANGO_USER` (`root`), `ARANGO_PASSWORD` or `ARANGO_ROOT_PASSWORD` (preferring the explicit one), `ARANGO_DATABASE` (`darbees_knowledge`). Uses Basic auth via the `Authorization` header.

No connection pooling, no retry logic — at the script call rate (one AQL per `related-rebuild` run) it's unnecessary.

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

**Cache file disposal:** `src/data/related-posts.cache.json` is removed from the repo as part of this commit. The new script doesn't write or read it. Arango is the cache now.

### 5.5 `scripts/lib/lmstudio.mjs` audit

Before deletion, grep for remaining consumers:

```bash
grep -rn "from './lib/lmstudio.mjs'\|from '../lib/lmstudio.mjs'\|require.*lmstudio.mjs" scripts/
```

Likely consumers: `geo-fill.mjs`, `image-watcher.mjs`. If any reference remains, leave the file in place (renaming it from `lmstudio.mjs` → `openai-compatible.mjs` is out of scope here — separate cleanup). If nothing references it, delete the file and its test.

### 5.6 `package.json` script additions

One new entry:

```json
"rag:rebuild-all": "npm run rag:reindex && npm run related:rebuild"
```

Convenience for the common "I edited a few posts, regenerate everything related-derived" flow. Both subscripts remain individually runnable.

### 5.7 `CLAUDE.md` update

One new row in the Authoring-scripts command table:

| Task | Command | When |
|------|---------|------|
| Reindex + rebuild related | `npm run rag:rebuild-all` | After adding/editing posts, before commit |

## 6. Build pipeline

No automatic chaining. The authoring workflow:

```
edit posts
  ↓
npm run geo:fill <new-post.mdx>       # if frontmatter needs filling
  ↓
npm run rag:rebuild-all                # Arango + related-posts.json
  ↓
npm run build                          # astro + pagefind + check:links
  ↓
git commit + push
```

CI runs `astro build` against committed `related-posts.json` — unchanged. Phase 11 G2 (Arango service container in CI) would let CI regenerate during builds; out of scope here.

## 7. Error handling

| Failure mode | Surface | Behavior |
|---|---|---|
| Bridge unreachable from Vite proxy | `/dev-search` fetch | Vite returns 502; page shows the error inline |
| Bridge returns 503 (e.g., embedding-config mismatch) | `/dev-search` fetch | Page shows the bridge's structured error |
| Bridge returns 400 (malformed request) | `/dev-search` fetch | Page shows the error |
| Arango unreachable from `related-rebuild` | script | Exits 1 with `ArangoError` message |
| Arango 404 ("database not found") | script | Exits 1 with the `darbees_knowledge` create-DB curl hint |
| `memory_posts` empty | script | Exits 1 with "run `npm run rag:reindex` first" hint |
| Build runs without prior `related:rebuild` | astro build | Reads the last-committed `related-posts.json`; if missing or empty, the related-posts component falls back to its existing empty-state behavior |
| `/dev-search` accessed in production | rendered page | "local-only" alert visible; fetch resolves to 404; no JS errors thrown — the form's `try/catch` surfaces the failure as a styled alert |

## 8. Testing

**Two test gates already exist** in the JS suite (`node --test 'scripts/**/*.test.mjs'`): no Arango required, no LLM required. The new tests follow that pattern.

**New unit tests** — `scripts/lib/arango-client.test.mjs` (5 tests, stubbed `globalThis.fetch`):

| # | Name | Asserts |
|---|---|---|
| 1 | `runAql_2xx_returns_result_array` | parsed `.result` returned |
| 2 | `runAql_4xx_throws_ArangoError_with_body` | thrown error has `status` + parsed body |
| 3 | `runAql_404_thrown` | covers the "database not found" path |
| 4 | `runAql_network_failure_throws_ArangoError` | fetch throws → ArangoError with "unreachable" message |
| 5 | `runAql_sends_basic_auth_from_env` | Authorization header matches `Basic base64(user:pass)` |

**Modified existing tests** — `scripts/related-rebuild.test.mjs`:
- Keep: `cosineSimilarity`, `topRelated`, `buildRelatedMap` (pure-helper coverage stays valid).
- Delete: `cacheKey` test (function removed).

Net change: -1 deleted, +5 added → JS suite goes from 38 → 42 passing.

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

1. **`scripts/lib/lmstudio.mjs` is misnamed.** It speaks OpenAI-compatible HTTP — the same protocol the C# `OpenAiCompatibleEmbeddingClient` uses. After this spec lands, audit non-related-rebuild consumers and rename to `openai-compatible.mjs` (matching the C# rename from PR #2). Separate cleanup, not in scope here.
2. **Manual chaining of `rag:reindex` + `related:rebuild` before every build is error-prone.** A pre-commit hook or build-time auto-chain would tighten the loop. Skipped here because:
   - Pre-commit hooks require Arango + bridge up during commit (friction for offline edits)
   - Build-time auto-chain breaks CI without Arango service container (Phase 11 G2)
   - Both can be added later without rework
3. **Eventually a public "ask the blog" UI** — Cloudflare Workers + Workers AI embeddings + Vectorize (or proxy the local bridge through a tunnel). Separate, large project.
4. **`/dev-search` doesn't filter by collection.** Adding `<select name="collection">` is one form control + one query param; deferred unless authoring use revealed it's needed.

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

---

**End of spec.**
