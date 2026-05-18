# Content RAG UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up a dev-only `/dev-search` page that talks to the bridge through a Vite proxy, rewrite `related-rebuild` to read body vectors straight from Arango, rename the OpenAI-compatible client, and add a build-time freshness check for `related-posts.json`.

**Architecture:** Two surfaces, no production HTTP. Browser → Vite dev-server proxy (`/dev-api/search`) → bridge `/api/memory/search` at request time during `npm run dev`. Build-time → `related-rebuild.mjs` reads vectors from `memory_posts` over AQL (`scripts/lib/arango-client.mjs`) and writes `src/data/related-posts.json`. Astro stays in static-output mode — no server endpoints, no adapter.

**Tech Stack:** Astro 6, Vite 7 (server.proxy), DaisyUI 5, Node 22, native `fetch` + `AbortSignal.timeout`, `node:test` + `node:assert/strict`, ArangoDB 3.12 HTTP API (`/_db/{db}/_api/cursor`).

**Spec:** `docs/superpowers/specs/2026-05-17-content-rag-ui-design.md` (commit `685e42e`).

**Branch:** `feature/content-rag-ui` (already created).

---

## File Map

| Path | Action | Responsibility |
|---|---|---|
| `astro.config.mjs` | modify | Add `vite.server.proxy['/dev-api/search']` → bridge `/api/memory/search` |
| `src/pages/dev-search.astro` | create | Search form + results UI, inline `<script>` fetch, AbortSignal cancel + 30s timeout, dev-only awareness, accessibility |
| `scripts/lib/arango-client.mjs` | create | Minimal HTTP wrapper for AQL: `runAql(query, bindVars, { timeoutMs })` + `ArangoError`, env-driven Basic auth |
| `scripts/lib/arango-client.test.mjs` | create | 6 unit tests, stubbed `globalThis.fetch` |
| `scripts/lib/bridge-client.mjs` | modify | Retroactive `AbortSignal.timeout(timeoutMs)`, default 30s |
| `scripts/lib/bridge-client.test.mjs` | modify | +1 timeout test |
| `scripts/related-rebuild.mjs` | rewrite `main()` | Read body vectors from Arango via `runAql`, drop `createClient`/`embedText`/`contentHash`/`cacheKey`, add dimension-consistency guard, drop cache file |
| `scripts/related-rebuild.test.mjs` | modify | Delete `cacheKey` test, keep pure-helper tests |
| `src/data/related-posts.cache.json` | delete | Arango is the cache now |
| `scripts/lib/lmstudio.mjs` | rename → `openai-compatible.mjs` | Rename, drop `LM Studio` strings, env-var/model defaults refresh, deprecation warns |
| `scripts/lib/lmstudio.test.mjs` | rename → `openai-compatible.test.mjs` | Update import, optionally add deprecation tests |
| `scripts/image-watcher.mjs` | modify (line 69) | Update import to `./lib/openai-compatible.mjs` |
| `scripts/geo-fill.mjs` | modify (line 14) | Update import to `./lib/openai-compatible.mjs` |
| `scripts/check-related-fresh.mjs` | create | Compare mtimes; warn (default) / exit 1 (`--strict`) |
| `scripts/check-related-fresh.test.mjs` | create | 5 unit tests, mkdtemp fixtures |
| `package.json` | modify | Add `rag:rebuild-all`, `rag:check-fresh`, `prebuild` |
| `CLAUDE.md` | modify | Add 2 rows to authoring-scripts table |

---

## Task 1: Wire the Vite dev-server proxy

**Files:**
- Modify: `astro.config.mjs:13-15`

- [ ] **Step 1: Open `astro.config.mjs` and locate the `vite:` block**

Current:

```js
vite: {
    plugins: [tailwindcss()],
},
```

- [ ] **Step 2: Replace the `vite:` block with the proxy-enabled version**

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

Path is `/dev-api/search` (not `/api/...`) to leave room for future static `/api/*` Astro endpoints without collision. `BRIDGE_URL` honors the existing pattern used by `scripts/lib/bridge-client.mjs`.

- [ ] **Step 3: Smoke-check the dev server still boots**

Run: `npm run dev`
Expected: Astro reports `Local http://localhost:4321/` with no Vite errors. Kill the server (Ctrl-C).

- [ ] **Step 4: Smoke-check the proxy reaches the bridge (requires `make up`)**

Pre-req: `make up` and `npm run rag:reindex` have populated Arango. If you don't have that yet, skip this step and revisit during Task 2 smoke.

Run (in a separate shell with `npm run dev` running):

```bash
curl -s -X POST http://localhost:4321/dev-api/search \
  -H 'content-type: application/json' \
  -d '{"query":"cast iron","k":3}' | head -c 200
```

Expected: JSON response with `results: [...]` and timing fields. If 502, the bridge isn't up — bring up the stack and retry. If 404, the proxy isn't wired — recheck Step 2.

- [ ] **Step 5: Commit**

```bash
git add astro.config.mjs
git commit -m "feat(astro): proxy /dev-api/search to dais-bridge during dev"
```

---

## Task 2: Create the `/dev-search` Astro page

**Files:**
- Create: `src/pages/dev-search.astro`

- [ ] **Step 1: Create `src/pages/dev-search.astro` with the full page contents**

```astro
---
import BaseLayout from '../layouts/BaseLayout.astro';

const isDev = import.meta.env.DEV;
const description =
    'Local-only search across blog, projects, and field notes. Requires the dev-server proxy.';
---

<BaseLayout title="Search the blog (dev)" description={description}>
    <Fragment slot="head">
        <meta name="robots" content="noindex" />
    </Fragment>

    <main class="container mx-auto max-w-3xl px-4 py-12">
        <h1 class="font-display mb-2 text-3xl font-bold">Search the blog (dev)</h1>
        <p class="text-base-content/70 mb-6">{description}</p>

        {!isDev && (
            <div class="alert alert-warning mb-6" role="alert">
                <span>
                    This page only works under <code>npm run dev</code>. In production builds the proxy is absent and the search request will fail.
                </span>
            </div>
        )}

        <form id="search-form" class="card bg-base-200 mb-6 p-4">
            <label for="search-query" class="label">
                <span class="label-text font-medium">Query</span>
            </label>
            <input
                id="search-query"
                name="query"
                type="text"
                class="input input-bordered w-full"
                placeholder="What did I write about cast iron pans?"
                required
                autocomplete="off"
            />

            <label for="search-k" class="label mt-3">
                <span class="label-text font-medium">Results (1–20)</span>
            </label>
            <input
                id="search-k"
                name="k"
                type="number"
                min="1"
                max="20"
                value="5"
                class="input input-bordered w-24"
            />

            <button id="search-submit" type="submit" class="btn btn-primary mt-4 w-fit">
                Search
            </button>
        </form>

        <p id="status" class="text-base-content/60 mb-4 min-h-[1.5rem] text-sm" role="status"></p>
        <p id="timings" class="text-base-content/50 mb-4 min-h-[1.25rem] text-xs"></p>

        <section
            id="results"
            class="space-y-3"
            role="region"
            aria-live="polite"
            aria-atomic="false"
            aria-label="Search results"
        ></section>
    </main>

    <script>
        const form = document.getElementById('search-form') as HTMLFormElement;
        const submit = document.getElementById('search-submit') as HTMLButtonElement;
        const status = document.getElementById('status') as HTMLParagraphElement;
        const timings = document.getElementById('timings') as HTMLParagraphElement;
        const results = document.getElementById('results') as HTMLElement;

        let activeController: AbortController | null = null;

        function escapeHtml(s: string): string {
            return s
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;');
        }

        function renderResults(items: any[]): void {
            if (items.length === 0) {
                results.innerHTML = '';
                return;
            }
            results.innerHTML = items
                .map((r) => {
                    const href = `/${escapeHtml(r.collection)}/${escapeHtml(r.slug)}/`;
                    const title = escapeHtml(r.title || r.slug);
                    const snippet = escapeHtml(r.snippet || r.description || '');
                    const score = (Number(r.score) || 0).toFixed(3);
                    const kind = escapeHtml(r.matchedKind || '');
                    return `
                        <a href="${href}" class="card bg-base-100 hover:bg-base-200 block p-4 shadow-sm transition">
                            <div class="flex items-center justify-between gap-3">
                                <h2 class="font-display text-lg font-semibold">${title}</h2>
                                <span class="badge badge-outline shrink-0">${score} · ${kind}</span>
                            </div>
                            ${snippet ? `<p class="text-base-content/70 mt-1 text-sm">${snippet}</p>` : ''}
                            <p class="text-base-content/50 mt-2 text-xs">${escapeHtml(r.collection)}/${escapeHtml(r.slug)}</p>
                        </a>
                    `;
                })
                .join('');
        }

        form.addEventListener('submit', async (event) => {
            event.preventDefault();

            if (activeController) activeController.abort();
            activeController = new AbortController();
            const signal = AbortSignal.any([activeController.signal, AbortSignal.timeout(30_000)]);

            const data = new FormData(form);
            const query = String(data.get('query') || '').trim();
            const k = Math.max(1, Math.min(20, Number(data.get('k') || 5)));
            if (!query) return;

            submit.setAttribute('aria-disabled', 'true');
            submit.disabled = true;
            status.textContent = 'Searching…';
            timings.textContent = '';
            results.innerHTML = '';

            try {
                const res = await fetch('/dev-api/search', {
                    method: 'POST',
                    headers: { 'content-type': 'application/json' },
                    body: JSON.stringify({ query, k }),
                    signal,
                });
                if (!res.ok) {
                    const body = await res.text();
                    status.textContent = `Error ${res.status}: ${body.slice(0, 200)}`;
                    return;
                }
                const payload = await res.json();
                const items = payload.results || [];
                if (items.length === 0) {
                    status.textContent = 'No results.';
                } else {
                    status.textContent = `${items.length} result${items.length === 1 ? '' : 's'}.`;
                }
                if (payload.queryEmbedMs != null && payload.searchMs != null) {
                    timings.textContent = `embed ${payload.queryEmbedMs}ms · search ${payload.searchMs}ms`;
                }
                renderResults(items);
            } catch (err: any) {
                if (err?.name === 'AbortError' && signal.reason?.name === 'TimeoutError') {
                    status.textContent = 'Search timed out after 30s.';
                } else if (err?.name === 'AbortError') {
                    // Superseded by a newer submit — leave the newer handler to update status.
                } else {
                    status.textContent = `Network error: ${err?.message || err}`;
                }
            } finally {
                submit.removeAttribute('aria-disabled');
                submit.disabled = false;
            }
        });
    </script>
</BaseLayout>
```

- [ ] **Step 2: Verify the page renders under the dev server**

Pre-req: bridge stack up, `npm run rag:reindex` has run at least once.

Run: `npm run dev` (in one shell), then open `http://localhost:4321/dev-search` in a browser.
Expected: Form renders. No "local-only" banner. No console errors.

- [ ] **Step 3: Manual smoke — search the welcome post**

In the browser at `/dev-search`:
1. Type a query you know has a match (e.g. `cast iron pan`).
2. Set `k` to `3`. Submit.
3. Expect 1-3 cards with score ≥ 0.4. Timings line shows two ms values. Clicking a card navigates to `/<collection>/<slug>/`.

- [ ] **Step 4: Manual smoke — error path**

Tear down the bridge: `make down`. Resubmit the form.
Expected: Status line shows `Error 502: ...` (Vite proxy error). Bring the bridge back up: `make up && make health`.

- [ ] **Step 5: Manual smoke — production build doesn't break**

```bash
npm run build
```

Expected: Build succeeds. `dist/dev-search/index.html` exists. `<meta name="robots" content="noindex">` appears in the head. (Build also runs `prebuild` which doesn't exist yet — but it does run `postbuild` which calls `check:links`; that may flag `/dev-search` as a new page. Acceptable for now; the link checker only flags broken internal links, not new pages, so this should be clean.)

- [ ] **Step 6: Commit**

```bash
git add src/pages/dev-search.astro
git commit -m "feat(pages): dev-only /dev-search page using the Vite proxy"
```

---

## Task 3: Create `scripts/lib/arango-client.mjs` with tests (TDD)

**Files:**
- Create: `scripts/lib/arango-client.mjs`
- Create: `scripts/lib/arango-client.test.mjs`

- [ ] **Step 1: Write the failing test file**

Create `scripts/lib/arango-client.test.mjs`:

```javascript
import test from 'node:test';
import assert from 'node:assert/strict';
import { ArangoError, runAql } from './arango-client.mjs';

function withFetch(stub, fn) {
    const original = globalThis.fetch;
    globalThis.fetch = stub;
    return Promise.resolve(fn()).finally(() => {
        globalThis.fetch = original;
    });
}

function jsonResponse(body, { status = 200 } = {}) {
    return new Response(JSON.stringify(body), {
        status,
        headers: { 'content-type': 'application/json' },
    });
}

test('runAql returns the result array on 2xx', async () => {
    await withFetch(
        async () => jsonResponse({ result: [{ a: 1 }, { a: 2 }], hasMore: false }),
        async () => {
            const rows = await runAql('FOR x IN c RETURN x');
            assert.deepEqual(rows, [{ a: 1 }, { a: 2 }]);
        },
    );
});

test('runAql throws ArangoError with status and parsed body on 4xx', async () => {
    await withFetch(
        async () => jsonResponse({ errorMessage: 'bad query', code: 400 }, { status: 400 }),
        async () => {
            await assert.rejects(
                () => runAql('NOT AQL'),
                (err) => {
                    assert.ok(err instanceof ArangoError);
                    assert.equal(err.status, 400);
                    assert.equal(err.body.errorMessage, 'bad query');
                    return true;
                },
            );
        },
    );
});

test('runAql 404 surfaces "database not found" path', async () => {
    await withFetch(
        async () => jsonResponse({ errorMessage: 'database not found' }, { status: 404 }),
        async () => {
            await assert.rejects(
                () => runAql('FOR x IN c RETURN x'),
                (err) => {
                    assert.equal(err.status, 404);
                    return true;
                },
            );
        },
    );
});

test('runAql wraps fetch network failures in ArangoError', async () => {
    await withFetch(
        async () => {
            throw new TypeError('fetch failed');
        },
        async () => {
            await assert.rejects(
                () => runAql('FOR x IN c RETURN x'),
                (err) => {
                    assert.ok(err instanceof ArangoError);
                    assert.match(err.message, /unreachable/);
                    return true;
                },
            );
        },
    );
});

test('runAql sends Basic auth from env vars', async () => {
    process.env.ARANGO_USER = 'root';
    process.env.ARANGO_PASSWORD = 's3cret';
    let capturedAuth;
    await withFetch(
        async (_url, init) => {
            capturedAuth = init.headers.Authorization;
            return jsonResponse({ result: [] });
        },
        async () => {
            await runAql('FOR x IN c RETURN x');
        },
    );
    const expected = `Basic ${Buffer.from('root:s3cret').toString('base64')}`;
    assert.equal(capturedAuth, expected);
});

test('runAql aborts after timeoutMs and throws ArangoError mentioning timeout', async () => {
    await withFetch(
        async (_url, init) => {
            return new Promise((_, reject) => {
                init.signal.addEventListener('abort', () => {
                    const err = new Error('aborted');
                    err.name = 'AbortError';
                    reject(err);
                });
            });
        },
        async () => {
            await assert.rejects(
                () => runAql('FOR x IN c RETURN x', {}, { timeoutMs: 50 }),
                (err) => {
                    assert.ok(err instanceof ArangoError);
                    assert.match(err.message, /timeout|Arango timeout/i);
                    return true;
                },
            );
        },
    );
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `node --test scripts/lib/arango-client.test.mjs`
Expected: 6 failures with `Cannot find module './arango-client.mjs'`.

- [ ] **Step 3: Implement `scripts/lib/arango-client.mjs`**

```javascript
/**
 * Minimal HTTP wrapper for ArangoDB. Reads connection env vars at call time,
 * uses Basic auth, applies a per-call AbortSignal.timeout.
 */

export class ArangoError extends Error {
    constructor(message, { status, body } = {}) {
        super(message);
        this.name = 'ArangoError';
        this.status = status;
        this.body = body;
    }
}

function connInfo() {
    const url = (process.env.ARANGO_URL || 'http://localhost:8529').replace(/\/$/, '');
    const db = process.env.ARANGO_DATABASE || 'darbees_knowledge';
    const user = process.env.ARANGO_USER || 'root';
    const pass = process.env.ARANGO_PASSWORD || process.env.ARANGO_ROOT_PASSWORD || '';
    const auth = `Basic ${Buffer.from(`${user}:${pass}`).toString('base64')}`;
    return { url, db, auth };
}

export async function runAql(query, bindVars = {}, { timeoutMs = 30_000 } = {}) {
    const { url, db, auth } = connInfo();
    const endpoint = `${url}/_db/${encodeURIComponent(db)}/_api/cursor`;
    const signal = AbortSignal.timeout(timeoutMs);

    let response;
    try {
        response = await fetch(endpoint, {
            method: 'POST',
            headers: { 'content-type': 'application/json', Authorization: auth },
            body: JSON.stringify({ query, bindVars }),
            signal,
        });
    } catch (cause) {
        if (cause?.name === 'AbortError' || cause?.name === 'TimeoutError') {
            throw new ArangoError(`Arango timeout after ${timeoutMs}ms: ${endpoint}`);
        }
        throw new ArangoError(`Arango unreachable at ${endpoint}: ${cause.message}`);
    }

    const text = await response.text();
    let parsed;
    try {
        parsed = text ? JSON.parse(text) : null;
    } catch {
        parsed = text;
    }
    if (!response.ok) {
        throw new ArangoError(`Arango ${response.status}: ${text.slice(0, 200)}`, {
            status: response.status,
            body: parsed,
        });
    }
    return parsed?.result ?? [];
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `node --test scripts/lib/arango-client.test.mjs`
Expected: `pass 6`, `fail 0`.

- [ ] **Step 5: Commit**

```bash
git add scripts/lib/arango-client.mjs scripts/lib/arango-client.test.mjs
git commit -m "feat(scripts): arango-client.mjs with timeout, basic auth, ArangoError"
```

---

## Task 4: Retroactively add timeout to `bridge-client.mjs`

**Files:**
- Modify: `scripts/lib/bridge-client.mjs`
- Modify: `scripts/lib/bridge-client.test.mjs`

- [ ] **Step 1: Add the failing timeout test**

Append to `scripts/lib/bridge-client.test.mjs`:

```javascript
test('bridgePost aborts after timeoutMs and throws BridgeError mentioning timeout', async () => {
    const originalFetch = globalThis.fetch;
    try {
        globalThis.fetch = async (_url, init) =>
            new Promise((_, reject) => {
                init.signal.addEventListener('abort', () => {
                    const err = new Error('aborted');
                    err.name = 'AbortError';
                    reject(err);
                });
            });
        await assert.rejects(
            () => bridgePost('/x', {}, { bridgeUrl: 'http://test', timeoutMs: 50 }),
            (err) => {
                assert.ok(err instanceof BridgeError);
                assert.match(err.message, /timeout|bridge timeout/i);
                return true;
            },
        );
    } finally {
        globalThis.fetch = originalFetch;
    }
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `node --test scripts/lib/bridge-client.test.mjs`
Expected: 1 failure (the new timeout test) — `bridgePost` ignores `timeoutMs` so the test hangs or the assertion about timeout message fails.

- [ ] **Step 3: Add timeout support to `bridgePost`**

Replace the function in `scripts/lib/bridge-client.mjs`:

```javascript
export async function bridgePost(
    path,
    body,
    { bridgeUrl = DEFAULT_BRIDGE_URL, timeoutMs = 30_000 } = {},
) {
    const url = `${bridgeUrl.replace(/\/$/, '')}${path}`;
    const signal = AbortSignal.timeout(timeoutMs);
    let response;
    try {
        response = await fetch(url, {
            method: 'POST',
            headers: { 'content-type': 'application/json' },
            body: JSON.stringify(body),
            signal,
        });
    } catch (cause) {
        if (cause?.name === 'AbortError' || cause?.name === 'TimeoutError') {
            throw new BridgeError(`bridge timeout after ${timeoutMs}ms: ${url}`);
        }
        throw new BridgeError(`bridge unreachable at ${url}: ${cause.message}`);
    }
    const text = await response.text();
    let parsed;
    try {
        parsed = text ? JSON.parse(text) : null;
    } catch {
        parsed = text;
    }
    if (!response.ok) {
        throw new BridgeError(`bridge ${response.status}: ${text}`, {
            status: response.status,
            body: parsed,
        });
    }
    return parsed;
}
```

- [ ] **Step 4: Run all bridge-client tests**

Run: `node --test scripts/lib/bridge-client.test.mjs`
Expected: `pass 5`, `fail 0`.

- [ ] **Step 5: Commit**

```bash
git add scripts/lib/bridge-client.mjs scripts/lib/bridge-client.test.mjs
git commit -m "feat(scripts): bridge-client gets 30s default AbortSignal.timeout"
```

---

## Task 5: Rewrite `scripts/related-rebuild.mjs` over Arango

**Files:**
- Modify: `scripts/related-rebuild.mjs` (rewrite `main()` and imports, remove `cacheKey`)
- Modify: `scripts/related-rebuild.test.mjs` (delete `cacheKey` test)
- Delete: `src/data/related-posts.cache.json`

- [ ] **Step 1: Delete the `cacheKey` test from `scripts/related-rebuild.test.mjs`**

Open `scripts/related-rebuild.test.mjs`. Find and remove the test block that imports/uses `cacheKey`. Keep every other test (`cosineSimilarity`, `topRelated`, `buildRelatedMap`).

- [ ] **Step 2: Run the suite to confirm pure-helper tests still pass**

Run: `node --test scripts/related-rebuild.test.mjs`
Expected: All remaining tests pass. (No `cacheKey` reference left.)

- [ ] **Step 3: Rewrite `scripts/related-rebuild.mjs`**

Replace the entire file with:

```javascript
#!/usr/bin/env node
/**
 * Related posts (#6) — read body vectors from Arango (memory_posts), compute
 * pairwise cosine similarity, write src/data/related-posts.json (consumed at
 * Astro build time). Arango is the single source of truth for vectors; this
 * script does not embed.
 */

/** Cosine similarity of two equal-length numeric vectors. Returns 0 for a zero vector. */
export function cosineSimilarity(a, b) {
    let dot = 0,
        na = 0,
        nb = 0;
    for (let i = 0; i < a.length; i++) {
        dot += a[i] * b[i];
        na += a[i] * a[i];
        nb += b[i] * b[i];
    }
    if (na === 0 || nb === 0) return 0;
    return dot / (Math.sqrt(na) * Math.sqrt(nb));
}

/**
 * For one post's vector, the top `limit` of `others` with score >= floor, highest first.
 * `others` is [{ collection, id, vector }] — caller has already excluded self.
 */
export function topRelated(vector, others, { limit = 3, floor = 0.5 } = {}) {
    return others
        .map((o) => ({ id: o.id, collection: o.collection, score: cosineSimilarity(vector, o.vector) }))
        .filter((o) => o.score >= floor)
        .sort((a, b) => b.score - a.score)
        .slice(0, limit);
}

/**
 * Build the full related-posts map from [{ collection, id, vector }].
 * Key is `${collection}/${id}`; each post's own entry is excluded from its list.
 */
export function buildRelatedMap(posts, opts) {
    const map = {};
    for (const post of posts) {
        const others = posts.filter((p) => !(p.collection === post.collection && p.id === post.id));
        map[`${post.collection}/${post.id}`] = topRelated(post.vector, others, opts);
    }
    return map;
}

// ---------------------------------------------------------------------------
// Orchestration — runs only when this file is invoked directly.
// ---------------------------------------------------------------------------
import { writeFile, mkdir } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { ArangoError, runAql } from './lib/arango-client.mjs';

const DATA_DIR = 'src/data';
const OUT_PATH = `${DATA_DIR}/related-posts.json`;

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
        if (err instanceof ArangoError) {
            console.error(`Arango error: ${err.message}`);
            if (err.status === 404) {
                console.error(
                    'Hint: database `darbees_knowledge` may not exist yet. ' +
                        'Run `make up` then `npm run rag:reindex` to bootstrap it.',
                );
            }
        } else {
            console.error(err.stack || err.message);
        }
        process.exit(1);
    }

    if (rows.length === 0) {
        console.error('No ready vectors in memory_posts. Run `npm run rag:reindex` first.');
        process.exit(1);
    }

    const dims = new Set(rows.map((r) => r.vector.length));
    if (dims.size !== 1) {
        console.error(`Inconsistent vector dimensions in memory_posts: ${[...dims].join(', ')}`);
        console.error('This indicates a partial migration. Run `npm run rag:reindex -- --force` to repair.');
        process.exit(1);
    }

    const floor = Number(process.env.RELATED_FLOOR ?? 0.5);
    const map = buildRelatedMap(rows, { limit: 3, floor });
    const orphans = Object.values(map).filter((r) => r.length === 0).length;

    await mkdir(DATA_DIR, { recursive: true });
    await writeFile(OUT_PATH, `${JSON.stringify(map, null, '\t')}\n`, 'utf8');

    console.log(`${rows.length} posts indexed, ${orphans} with 0 relations (floor=${floor})`);
    console.log(`Wrote ${OUT_PATH}`);
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
    main();
}
```

- [ ] **Step 4: Delete the cache file**

```bash
git rm src/data/related-posts.cache.json
```

If the file doesn't exist on disk, `git rm` will fail — in that case `git ls-files src/data/related-posts.cache.json` will show whether it's tracked, and you can skip this step if it's already absent.

- [ ] **Step 5: Run the pure-helper test suite**

Run: `node --test scripts/related-rebuild.test.mjs`
Expected: All tests pass (cosineSimilarity, topRelated, buildRelatedMap).

- [ ] **Step 6: Run the full JS suite**

Run: `npm run test:scripts`
Expected: All tests pass. (The `cacheKey` test is gone; arango-client tests pass; bridge-client tests pass.)

- [ ] **Step 7: Manual smoke (requires `make up` + `npm run rag:reindex`)**

```bash
rm -f src/data/related-posts.json
npm run related:rebuild
```

Expected:
- `N posts indexed, M with 0 relations (floor=0.5)` line
- `Wrote src/data/related-posts.json` line
- File exists, JSON keyed by `<collection>/<slug>`, each value an array of `{id, collection, score}`.

- [ ] **Step 8: Manual smoke — Arango down**

```bash
podman stop darbees-dev-arango-1   # adjust container name if different
npm run related:rebuild
```

Expected: exit code 1, message contains `Arango unreachable`. Bring Arango back: `podman start darbees-dev-arango-1`.

- [ ] **Step 9: Commit**

```bash
git add scripts/related-rebuild.mjs scripts/related-rebuild.test.mjs src/data/related-posts.cache.json
git commit -m "refactor(scripts): related-rebuild reads body vectors from Arango"
```

(If the cache file was already untracked/absent, drop it from the `git add` list.)

---

## Task 6: Rename `lmstudio.mjs` → `openai-compatible.mjs`

**Files:**
- Rename: `scripts/lib/lmstudio.mjs` → `scripts/lib/openai-compatible.mjs`
- Rename: `scripts/lib/lmstudio.test.mjs` → `scripts/lib/openai-compatible.test.mjs`
- Modify (rename + content): the renamed `.mjs` (env-var/model defaults, error strings)
- Modify (import path): `scripts/image-watcher.mjs:69`
- Modify (import path): `scripts/geo-fill.mjs:14`

- [ ] **Step 1: Confirm `related-rebuild.mjs` no longer imports `lmstudio`**

Run: `grep -n lmstudio scripts/related-rebuild.mjs`
Expected: no matches. (Task 5 removed the import.)

- [ ] **Step 2: Rename the source file**

```bash
git mv scripts/lib/lmstudio.mjs scripts/lib/openai-compatible.mjs
```

- [ ] **Step 3: Rename the test file**

```bash
git mv scripts/lib/lmstudio.test.mjs scripts/lib/openai-compatible.test.mjs
```

- [ ] **Step 4: Update the test file's import**

Open `scripts/lib/openai-compatible.test.mjs`. Change line 3:

```javascript
import { createClient } from './lmstudio.mjs';
```

to:

```javascript
import { createClient } from './openai-compatible.mjs';
```

- [ ] **Step 5: Update `openai-compatible.mjs` defaults and strings**

In `scripts/lib/openai-compatible.mjs`:

- Replace the header doc comment with:

```javascript
/**
 * Thin fetch-based client for any OpenAI-compatible local server
 * (llama.cpp `llama-server` is the primary target; LM Studio and Ollama
 * with its OpenAI shim work too).
 *
 * `fetch` is injectable for tests; everything else falls back to env vars.
 *
 * Multi-server deployments: llama.cpp serves one model per process, so
 * chat / embedding / vision typically run on separate ports. Override
 * the per-task base URLs with LLM_CHAT_URL / LLM_EMBEDDING_URL /
 * LLM_VISION_URL; otherwise all three fall back to the global baseUrl.
 *
 * Back-compat: `LMSTUDIO_URL` and `LMSTUDIO_API_KEY` are still honored
 * but emit a one-time deprecation warning at client-creation time.
 */
```

- Replace the `createClient` signature and prelude. Current:

```javascript
export function createClient({
    fetch = globalThis.fetch,
    baseUrl = process.env.LMSTUDIO_URL || DEFAULT_BASE_URL,
    chatBaseUrl = process.env.LLM_CHAT_URL,
    embeddingBaseUrl = process.env.LLM_EMBEDDING_URL,
    visionBaseUrl = process.env.LLM_VISION_URL,
    apiKey = process.env.LMSTUDIO_API_KEY || '',
    chatModel = process.env.AI_MODEL_ID || 'local-model',
    visionModel = process.env.AI_VISION_MODEL_ID || 'qwen/qwen3-vl-8b-instruct',
    embeddingModel = process.env.AI_EMBEDDING_MODEL_ID || 'text-embedding-qwen3-embedding-8b',
} = {}) {
```

Replace with:

```javascript
let warnedLmstudioUrl = false;
let warnedLmstudioApiKey = false;

function resolveBaseUrl() {
    if (process.env.LLM_CHAT_URL) return process.env.LLM_CHAT_URL;
    if (process.env.LMSTUDIO_URL) {
        if (!warnedLmstudioUrl) {
            console.warn(
                '[openai-compatible] LMSTUDIO_URL is deprecated; set LLM_CHAT_URL (or pass baseUrl) instead.',
            );
            warnedLmstudioUrl = true;
        }
        return process.env.LMSTUDIO_URL;
    }
    return DEFAULT_BASE_URL;
}

function resolveApiKey() {
    if (process.env.AI_API_KEY) return process.env.AI_API_KEY;
    if (process.env.LMSTUDIO_API_KEY) {
        if (!warnedLmstudioApiKey) {
            console.warn(
                '[openai-compatible] LMSTUDIO_API_KEY is deprecated; set AI_API_KEY instead.',
            );
            warnedLmstudioApiKey = true;
        }
        return process.env.LMSTUDIO_API_KEY;
    }
    return '';
}

export function createClient({
    fetch = globalThis.fetch,
    baseUrl = resolveBaseUrl(),
    chatBaseUrl = process.env.LLM_CHAT_URL,
    embeddingBaseUrl = process.env.LLM_EMBEDDING_URL,
    visionBaseUrl = process.env.LLM_VISION_URL,
    apiKey = resolveApiKey(),
    chatModel = process.env.AI_MODEL_ID || 'llama-4-maverick',
    visionModel = process.env.AI_VISION_MODEL_ID || 'qwen/qwen3-vl-8b-instruct',
    embeddingModel = process.env.AI_EMBEDDING_MODEL_ID || 'qwen3-embedding-8b',
} = {}) {
```

- In the `requireKeys` helper, change `'LM Studio ${where}: response missing key'` to `'LLM ${where}: response missing key'`:

```javascript
function requireKeys(obj, keys, where) {
    for (const k of keys) {
        if (!(k in obj)) throw new Error(`LLM ${where}: response missing key "${k}"`);
    }
    return obj;
}
```

- [ ] **Step 6: Update consumer imports**

Edit `scripts/image-watcher.mjs` line 69:

```javascript
import { createClient } from './lib/lmstudio.mjs';
```

to:

```javascript
import { createClient } from './lib/openai-compatible.mjs';
```

Edit `scripts/geo-fill.mjs` line 14 the same way.

- [ ] **Step 7: Verify no stale references remain**

Run: `grep -rn "lmstudio" scripts/ --include="*.mjs"`
Expected: no matches.

Run: `grep -rn "lmstudio" scripts/ --include="*.mjs" --include="*.json"`
Expected: no matches.

- [ ] **Step 8: Run the renamed test file**

Run: `node --test scripts/lib/openai-compatible.test.mjs`
Expected: All existing tests pass under the new import path.

- [ ] **Step 9: Run the full JS suite**

Run: `npm run test:scripts`
Expected: All tests pass.

- [ ] **Step 10: Commit**

```bash
git add -A scripts/lib/openai-compatible.mjs scripts/lib/openai-compatible.test.mjs \
         scripts/image-watcher.mjs scripts/geo-fill.mjs
git commit -m "refactor(scripts): rename lmstudio.mjs to openai-compatible.mjs

- Default chatModel: llama-4-maverick (was 'local-model').
- Default embeddingModel: qwen3-embedding-8b (was 'text-embedding-qwen3-embedding-8b').
- LLM_CHAT_URL takes precedence over LMSTUDIO_URL with a one-time deprecation warn.
- AI_API_KEY takes precedence over LMSTUDIO_API_KEY with a one-time deprecation warn.
- Error strings drop the 'LM Studio' prefix in favor of 'LLM'.
- Mirrors the C# LmStudioEmbeddingClient -> OpenAiCompatibleEmbeddingClient rename from PR #2.

Consumers updated: image-watcher.mjs, geo-fill.mjs. related-rebuild.mjs no longer imports this module after the Arango rewrite (commit before)."
```

---

## Task 7: Create `scripts/check-related-fresh.mjs` with tests (TDD)

**Files:**
- Create: `scripts/check-related-fresh.mjs`
- Create: `scripts/check-related-fresh.test.mjs`

- [ ] **Step 1: Confirm `listPosts` returns `.path` (not `.filePath`)**

Run: `grep -n "path:" scripts/lib/posts.mjs`
Expected output includes: `posts.push({ collection, id: deriveId(root, path), path, frontmatter, body });`

So in the freshness check use `p.path`, not `p.filePath` (the spec text used `filePath` colloquially — actual field is `path`).

- [ ] **Step 2: Write the failing test file**

Create `scripts/check-related-fresh.test.mjs`:

```javascript
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, utimes } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';

const SCRIPT = new URL('./check-related-fresh.mjs', import.meta.url).pathname;

async function setup({ withRelated = true, mdxMtime, relatedMtime } = {}) {
    const dir = await mkdtemp(join(tmpdir(), 'fresh-'));
    await mkdir(join(dir, 'src/content/blog'), { recursive: true });
    await mkdir(join(dir, 'src/data'), { recursive: true });

    const post = join(dir, 'src/content/blog/hello.mdx');
    await writeFile(
        post,
        '---\ntitle: Hello\npubDate: 2026-01-01\n---\nbody',
        'utf8',
    );
    if (mdxMtime != null) await utimes(post, mdxMtime / 1000, mdxMtime / 1000);

    if (withRelated) {
        const related = join(dir, 'src/data/related-posts.json');
        await writeFile(related, '{}', 'utf8');
        if (relatedMtime != null) await utimes(related, relatedMtime / 1000, relatedMtime / 1000);
    }

    return dir;
}

function runScript(cwd, args = []) {
    return spawnSync(process.execPath, [SCRIPT, ...args], { cwd, encoding: 'utf8' });
}

test('all fresh: exits 0 with success message', async () => {
    const dir = await setup({ mdxMtime: 1_000_000, relatedMtime: 2_000_000 });
    const res = runScript(dir);
    assert.equal(res.status, 0);
    assert.match(res.stdout, /up-to-date/);
});

test('mdx newer than related-posts.json: warns, exits 0 (default)', async () => {
    const dir = await setup({ mdxMtime: 2_000_000, relatedMtime: 1_000_000 });
    const res = runScript(dir);
    assert.equal(res.status, 0);
    assert.match(res.stderr, /stale|out of date/i);
    assert.match(res.stderr, /blog\/hello/);
});

test('mdx newer with --strict: exits 1', async () => {
    const dir = await setup({ mdxMtime: 2_000_000, relatedMtime: 1_000_000 });
    const res = runScript(dir, ['--strict']);
    assert.equal(res.status, 1);
    assert.match(res.stderr, /stale/i);
});

test('missing related-posts.json: warns, exits 0 (default)', async () => {
    const dir = await setup({ withRelated: false });
    const res = runScript(dir);
    assert.equal(res.status, 0);
    assert.match(res.stderr, /missing/i);
});

test('missing related-posts.json with --strict: exits 1', async () => {
    const dir = await setup({ withRelated: false });
    const res = runScript(dir, ['--strict']);
    assert.equal(res.status, 1);
    assert.match(res.stderr, /missing/i);
});
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `node --test scripts/check-related-fresh.test.mjs`
Expected: All 5 fail (script missing).

- [ ] **Step 4: Implement `scripts/check-related-fresh.mjs`**

```javascript
#!/usr/bin/env node
/**
 * Compare mtimes of published MDX files against src/data/related-posts.json.
 * Warns by default, exits 1 with --strict. Used as the npm `prebuild` hook.
 */
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
        const m = (await stat(p.path)).mtimeMs;
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

- [ ] **Step 5: Run the tests to verify they pass**

Run: `node --test scripts/check-related-fresh.test.mjs`
Expected: `pass 5`, `fail 0`.

- [ ] **Step 6: Run the full JS suite**

Run: `npm run test:scripts`
Expected: All tests pass (prior tasks' tests still green).

- [ ] **Step 7: Commit**

```bash
git add scripts/check-related-fresh.mjs scripts/check-related-fresh.test.mjs
git commit -m "feat(scripts): check-related-fresh.mjs warns on stale related-posts.json"
```

---

## Task 8: Wire npm scripts and update `CLAUDE.md`

**Files:**
- Modify: `package.json`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Add the three scripts to `package.json`**

In `package.json`, inside the `"scripts"` object, add (alphabetical order is fine — these belong after the existing `rag:` and `related:` entries):

```json
"prebuild": "node scripts/check-related-fresh.mjs",
"rag:check-fresh": "node scripts/check-related-fresh.mjs",
"rag:rebuild-all": "npm run rag:reindex && npm run related:rebuild",
```

The `prebuild` hook runs automatically before `npm run build` (npm convention). It's non-strict by default — warns, never fails.

- [ ] **Step 2: Verify `prebuild` fires automatically and is non-blocking**

Run: `touch src/content/blog/$(ls src/content/blog | head -n 1)` (touch a real MDX to bump its mtime)
Run: `npm run build`
Expected: First line(s) of output include `⚠ related-posts.json is stale for ... post(s):` (assuming the touched post is newer than `related-posts.json`). Build completes successfully. Reset the touched mtime if you care: `git checkout src/content/blog/<that-file>.mdx` — actually `touch` doesn't change content, so no `git checkout` needed; just refresh `related-posts.json` via `npm run rag:rebuild-all` if you want to silence the warn on subsequent builds.

- [ ] **Step 3: Verify `rag:check-fresh -- --strict` exits 1 on stale**

Run: `touch src/content/blog/$(ls src/content/blog | head -n 1)`
Run: `npm run rag:check-fresh -- --strict`
Expected: Exit code 1. `echo $?` immediately after prints `1`. Stderr lists the stale post.

- [ ] **Step 4: Verify `rag:rebuild-all` chains both steps**

Pre-req: `make up` is running, llama-servers on :8080/:8081.
Run: `npm run rag:rebuild-all`
Expected: `rag:reindex` output (per-post progress), then `related:rebuild` output (`N posts indexed, M with 0 relations`). Exit code 0.

- [ ] **Step 5: Update `CLAUDE.md`**

Find the "Commands (Authoring scripts — Phase 13)" table. Add two rows (right after `Reindex content RAG`):

```markdown
| Reindex + rebuild related | `npm run rag:rebuild-all`        | After adding/editing posts, before commit |
| Check related-posts freshness | `npm run rag:check-fresh`    | Manual sanity check (also runs automatically before `npm run build`) |
```

- [ ] **Step 6: Commit**

```bash
git add package.json CLAUDE.md
git commit -m "feat(build): rag:rebuild-all, rag:check-fresh, and prebuild fresh-check hook"
```

---

## Task 9: Final integration smoke and PR prep

**Files:** (no edits — pure verification)

- [ ] **Step 1: Run the full JS suite**

Run: `npm run test:scripts`
Expected: `pass 49`, `fail 0`. (Was 38 pre-feature; +12 added, −1 deleted.)

- [ ] **Step 2: Run lint and format checks**

Run: `npm run lint`
Expected: No new errors beyond the ~55 pre-existing ones documented in `CLAUDE.md`. Compare numerically if unsure.

Run: `npm run format:check`
Expected: Same — no *new* drift introduced by this branch.

- [ ] **Step 3: Run type check**

Run: `npm run check`
Expected: Astro check passes. The inline `<script>` in `dev-search.astro` is TypeScript — verify it type-checks cleanly. If errors, fix inline TypeScript types and re-run.

- [ ] **Step 4: End-to-end smoke (requires `make up` + llama-servers)**

```bash
make up
make health
npm run rag:reindex
npm run rag:rebuild-all
npm run dev
```

Browser at `http://localhost:4321/dev-search`:
- Query `cast iron`, k=3, submit. Expect ≥1 result with a score, snippet, and timing line.
- Stop the bridge (`make down`), resubmit. Expect 502 in the status line.

- [ ] **Step 5: Build + preview smoke**

```bash
make up    # bring back if Step 4 took it down
npm run build
npm run preview
```

Open `http://localhost:4321/dev-search` (preview port may differ; check console output). Expect:
- `local-only` alert visible (preview = production build, `import.meta.env.DEV === false`).
- Form renders. Submit → status line shows a 404 or network error (no proxy).

- [ ] **Step 6: Open PR**

```bash
git push -u origin feature/content-rag-ui
gh pr create --title "feat: content-RAG UI — /dev-search + Arango-backed related-rebuild" --body "$(cat <<'EOF'
## Summary
- `/dev-search` dev-only page goes through a Vite proxy to the bridge's `/api/memory/search`. Production builds render the page with a "local-only" notice.
- `scripts/related-rebuild.mjs` now reads body vectors from `memory_posts` (Arango) via a new `scripts/lib/arango-client.mjs`; no second-pass embedding.
- `scripts/lib/lmstudio.mjs` renamed to `openai-compatible.mjs` with `llama-4-maverick` / `qwen3-embedding-8b` defaults and `LLM_CHAT_URL` / `AI_API_KEY` taking precedence.
- New `scripts/check-related-fresh.mjs` runs as `prebuild` (warn-only) to flag stale `related-posts.json` after MDX edits.
- `bridge-client.mjs` retroactively gains a 30s `AbortSignal.timeout`.

Spec: `docs/superpowers/specs/2026-05-17-content-rag-ui-design.md` (commit 685e42e).

## Test plan
- [ ] `npm run test:scripts` → 49 passing.
- [ ] `npm run check` clean.
- [ ] `npm run dev` + browser smoke on `/dev-search` (results, timings, click-through, bridge-down error path).
- [ ] `npm run build` → `prebuild` warns when stale, build still succeeds; `npm run preview` shows the local-only banner.
- [ ] `npm run rag:rebuild-all` runs both steps and writes a fresh `related-posts.json`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 7: Verify PR opened**

Expected: `gh pr create` prints the PR URL. Open it; CI runs `lint`, `format:check`, `check`, `check:links`, `test:scripts`, `playwright`, and the dotnet integration tests. Of these, only `test:scripts` and `check` (Astro) exercise this branch's new JS; the rest validate that nothing regressed.

---

## Decision references (from spec §10)

These are non-obvious choices that this plan inherits from the spec. Don't re-litigate them; flag any new info to the spec author.

- Vite `server.proxy`, not an Astro server endpoint (Astro is static-output; no adapter).
- Body vector, not summary vector, for `related-rebuild` (matches the old `embedText` intent).
- Submit-button only — no debounce, no live search (avoids hammering qwen3 per keystroke).
- 30s default `AbortSignal.timeout` everywhere (proxy fetch, arango-client, bridge-client).
- Freshness check warns by default; CI without Arango still builds (`--strict` is opt-in).
- `prebuild` is the npm hook for the fresh check; `rag:reindex` and `related:rebuild` stay manual.
