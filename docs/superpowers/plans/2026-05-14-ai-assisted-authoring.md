# AI-Assisted Authoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add three author-run Node scripts — GEO frontmatter auto-fill, related-posts rebuild, and an image-drop helper — that offload the mechanical parts of authoring to the local LM Studio instance.

**Architecture:** Three `.mjs` scripts under `scripts/` share a `scripts/lib/` of three focused modules (LM Studio HTTP client, content walker, frontmatter merger). Pure logic is unit-tested with `node:test`; the LM-Studio-dependent orchestration is verified by a manual checklist. `#6` reuses the related-posts rendering that already exists in `PostFooterNav` — it only swaps the data source.

**Tech Stack:** Node 24 ESM, `node:test`, `gray-matter`, `chokidar` v4, `sharp` (already a dependency), LM Studio OpenAI-compatible API.

**Spec:** `docs/superpowers/specs/2026-05-14-ai-assisted-authoring-design.md`

**Branch target:** `feature/ai-assisted-authoring` (off `master`). The worktree/branch is created by the execution skill — tasks below assume you are already on it.

**Commit convention:** every commit message ends with the `Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>` trailer (omitted from the `-m` examples below for brevity).

---

## File Structure

**Create:**
- `scripts/lib/lmstudio.mjs` — fetch-based LM Studio client. Knows the HTTP API, nothing about posts.
- `scripts/lib/lmstudio.test.mjs`
- `scripts/lib/posts.mjs` — walks `src/content`, parses frontmatter, hashes. Knows the content layout, nothing about LM Studio.
- `scripts/lib/posts.test.mjs`
- `scripts/lib/frontmatter-merge.mjs` — pure frontmatter merge + serialize. No I/O.
- `scripts/lib/frontmatter-merge.test.mjs`
- `scripts/geo-fill.mjs` — #5 orchestration (one-shot).
- `scripts/related-rebuild.mjs` — #6: exported pure helpers + one-shot orchestration.
- `scripts/related-rebuild.test.mjs`
- `scripts/image-watcher.mjs` — #7: exported pure helpers + long-running watcher.
- `scripts/image-watcher.test.mjs`
- `scripts/README.md` — how to run the three scripts + manual verification checklist.
- `src/data/related-posts.json` — committed build input (`{}` placeholder until first rebuild).
- `src/data/related-posts.cache.json` — committed embedding cache (`{}` placeholder).

**Modify:**
- `package.json` — 2 new deps, 5 new npm scripts.
- `.env.example` — 2 new env var lines.
- `src/utils/getPosts.ts` — rewrite `getRelatedPostsCrossCollection` to read `src/data/related-posts.json`.
- `.github/workflows/ci.yml` — add a `test:scripts` step to the `verify` job.
- `dais-bridge.tests/ScaffoldingTests.cs` — add a guard asserting the new scripts + deps exist.
- `CLAUDE.md` — add an authoring-scripts command table.

---

## Task 1: Dependencies, npm scripts, env example

**Files:**
- Modify: `package.json`
- Modify: `.env.example`

- [ ] **Step 1: Add dependencies and npm scripts to `package.json`**

In `dependencies`, add (keep the block alphabetically sorted):

```json
    "chokidar": "^4.0.3",
    "gray-matter": "^4.0.3",
```

In `scripts`, add these five entries:

```json
    "geo:fill": "node --env-file-if-exists=.env scripts/geo-fill.mjs",
    "geo:fill:all": "node --env-file-if-exists=.env scripts/geo-fill.mjs --all",
    "related:rebuild": "node --env-file-if-exists=.env scripts/related-rebuild.mjs",
    "image:watch": "node --env-file-if-exists=.env scripts/image-watcher.mjs",
    "test:scripts": "node --test scripts/",
```

- [ ] **Step 2: Add the two new env vars to `.env.example`**

Find the existing `AI_MODEL_ID` line in `.env.example` and add these two lines directly after it:

```bash
# Vision-capable model id for the image-watcher alt-text script (image:watch).
AI_VISION_MODEL_ID=
# Embedding model id for the related-posts rebuild script (related:rebuild).
AI_EMBEDDING_MODEL_ID=
```

- [ ] **Step 3: Install dependencies**

Run: `npm install`
Expected: completes without error; `package-lock.json` updated; `node_modules/gray-matter` and `node_modules/chokidar` exist.

- [ ] **Step 4: Verify the test script runs**

Run: `npm run test:scripts`
Expected: exit code 0. With no `*.test.mjs` files yet, Node prints a run with `tests 0` / `pass 0` and exits 0.

- [ ] **Step 5: Commit**

```bash
git add package.json package-lock.json .env.example
git commit -m "feat(phase13): add gray-matter + chokidar, authoring npm scripts"
```

---

## Task 2: `frontmatter-merge.mjs` — non-destructive frontmatter merge

**Files:**
- Create: `scripts/lib/frontmatter-merge.mjs`
- Test: `scripts/lib/frontmatter-merge.test.mjs`

- [ ] **Step 1: Write the failing test**

Create `scripts/lib/frontmatter-merge.test.mjs`:

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import matter from 'gray-matter';
import { isEmpty, mergeFrontmatter, serialize } from './frontmatter-merge.mjs';

test('isEmpty treats undefined/null/blank/empty-array as empty', () => {
	assert.equal(isEmpty(undefined), true);
	assert.equal(isEmpty(null), true);
	assert.equal(isEmpty(''), true);
	assert.equal(isEmpty('   '), true);
	assert.equal(isEmpty([]), true);
	assert.equal(isEmpty('x'), false);
	assert.equal(isEmpty(['a']), false);
	assert.equal(isEmpty(0), false);
	assert.equal(isEmpty(false), false);
});

test('mergeFrontmatter fills only empty keys by default', () => {
	const existing = { title: 'T', aiSummary: '' };
	const generated = { aiSummary: 'S', title: 'NEW' };
	const { merged, changedKeys } = mergeFrontmatter(existing, generated);
	assert.equal(merged.aiSummary, 'S');
	assert.equal(merged.title, 'T');
	assert.deepEqual(changedKeys, ['aiSummary']);
});

test('mergeFrontmatter with force overwrites existing keys', () => {
	const existing = { title: 'T', aiSummary: 'old' };
	const generated = { aiSummary: 'S', title: 'NEW' };
	const { merged, changedKeys } = mergeFrontmatter(existing, generated, { force: true });
	assert.equal(merged.title, 'NEW');
	assert.equal(merged.aiSummary, 'S');
	assert.deepEqual(changedKeys.sort(), ['aiSummary', 'title']);
});

test('mergeFrontmatter preserves existing key order and appends new keys', () => {
	const existing = { title: 'T', description: 'D', draft: true };
	const generated = { aiSummary: 'S', keyTakeaways: ['a'] };
	const { merged } = mergeFrontmatter(existing, generated);
	assert.deepEqual(Object.keys(merged), [
		'title', 'description', 'draft', 'aiSummary', 'keyTakeaways',
	]);
});

test('serialize round-trips body unchanged and writes frontmatter', () => {
	const out = serialize({ title: 'T' }, 'body text here');
	const parsed = matter(out);
	assert.equal(parsed.data.title, 'T');
	assert.equal(parsed.content.trim(), 'body text here');
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `node --test scripts/lib/frontmatter-merge.test.mjs`
Expected: FAIL — `Cannot find module './frontmatter-merge.mjs'`.

- [ ] **Step 3: Write the implementation**

Create `scripts/lib/frontmatter-merge.mjs`:

```js
/**
 * Non-destructive frontmatter merge + serialize. Pure functions, no I/O.
 */
import matter from 'gray-matter';

/** A value is "empty" (safe to fill) when undefined, null, blank string, or []. */
export function isEmpty(value) {
	if (value === undefined || value === null) return true;
	if (typeof value === 'string') return value.trim() === '';
	if (Array.isArray(value)) return value.length === 0;
	return false;
}

/**
 * Merge generated frontmatter into existing.
 * - force: overwrite existing keys.
 * - default: write a generated key only when the existing value isEmpty().
 * Existing key order is preserved; brand-new keys are appended.
 * @returns {{ merged: object, changedKeys: string[] }}
 */
export function mergeFrontmatter(existing, generated, { force = false } = {}) {
	const merged = { ...existing };
	const changedKeys = [];
	for (const [key, value] of Object.entries(generated)) {
		if (!force && !isEmpty(existing[key])) continue;
		merged[key] = value;
		changedKeys.push(key);
	}
	return { merged, changedKeys };
}

/** Re-emit an .mdx file: YAML frontmatter block + body. Body passed through as-is. */
export function serialize(frontmatter, body) {
	return matter.stringify(body, frontmatter);
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `node --test scripts/lib/frontmatter-merge.test.mjs`
Expected: PASS — 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add scripts/lib/frontmatter-merge.mjs scripts/lib/frontmatter-merge.test.mjs
git commit -m "feat(phase13): frontmatter-merge lib — non-destructive merge + serialize"
```

---

## Task 3: `posts.mjs` — content walker

**Files:**
- Create: `scripts/lib/posts.mjs`
- Test: `scripts/lib/posts.test.mjs`

- [ ] **Step 1: Write the failing test**

Create `scripts/lib/posts.test.mjs`:

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { deriveId, stripMdx, embedText, contentHash, listPosts } from './posts.mjs';

test('deriveId returns the posix path under the collection root without .mdx', () => {
	assert.equal(deriveId('src/content/blog', 'src/content/blog/foo.mdx'), 'foo');
	assert.equal(deriveId('src/content/blog', 'src/content/blog/2024/bar.mdx'), '2024/bar');
});

test('stripMdx removes imports, JSX tags, and markdown punctuation', () => {
	const body = "import X from 'x';\n\n# Heading\n\n<Callout>hey</Callout>\n\nA [link](http://e.com).";
	const out = stripMdx(body);
	assert.equal(out.includes('import'), false);
	assert.equal(out.includes('<Callout>'), false);
	assert.equal(out.includes('#'), false);
	assert.equal(out.includes('Heading'), true);
	assert.equal(out.includes('link'), true);
});

test('embedText includes identity fields and stripped body', () => {
	const post = {
		frontmatter: { title: 'My Title', description: 'Desc', tags: ['a', 'b'], category: 'RV Life' },
		body: '# Hello world',
	};
	const text = embedText(post);
	assert.equal(text.includes('My Title'), true);
	assert.equal(text.includes('Tags: a, b'), true);
	assert.equal(text.includes('Category: RV Life'), true);
	assert.equal(text.includes('Hello world'), true);
});

test('contentHash is stable and changes with the body', () => {
	const a = { frontmatter: { title: 'T', description: 'D' }, body: 'one' };
	const b = { frontmatter: { title: 'T', description: 'D' }, body: 'one' };
	const c = { frontmatter: { title: 'T', description: 'D' }, body: 'two' };
	assert.equal(contentHash(a), contentHash(b));
	assert.notEqual(contentHash(a), contentHash(c));
});

test('listPosts walks collections, skips drafts and _templates', async () => {
	const root = await mkdtemp(join(tmpdir(), 'posts-'));
	try {
		await mkdir(join(root, 'blog'), { recursive: true });
		await mkdir(join(root, '_templates'), { recursive: true });
		await writeFile(join(root, 'blog', 'a.mdx'), '---\ntitle: A\ndraft: false\n---\nbody a');
		await writeFile(join(root, 'blog', 'b.mdx'), '---\ntitle: B\ndraft: true\n---\nbody b');
		await writeFile(join(root, '_templates', 't.mdx'), '---\ntitle: T\n---\ntemplate');

		const published = await listPosts({ contentRoot: root, collections: ['blog'] });
		assert.deepEqual(published.map((p) => p.id), ['a']);

		const all = await listPosts({ contentRoot: root, collections: ['blog'], includeDrafts: true });
		assert.deepEqual(all.map((p) => p.id).sort(), ['a', 'b']);
	} finally {
		await rm(root, { recursive: true, force: true });
	}
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `node --test scripts/lib/posts.test.mjs`
Expected: FAIL — `Cannot find module './posts.mjs'`.

- [ ] **Step 3: Write the implementation**

Create `scripts/lib/posts.mjs`:

```js
/**
 * Walks the Astro content collections, parses frontmatter, and produces the
 * text + hash used for embedding. Knows the content layout, nothing about LM Studio.
 */
import { readdir, readFile } from 'node:fs/promises';
import { join, relative, sep } from 'node:path';
import { createHash } from 'node:crypto';
import matter from 'gray-matter';

/** Collections that participate in related posts (#6). */
export const PRIMARY_COLLECTIONS = ['blog', 'projects', 'field-notes'];
/** Collections that participate in GEO fill (#5) — includes books. */
export const ALL_COLLECTIONS = ['blog', 'projects', 'field-notes', 'books'];

/** Astro-style id: path under the collection root, posix separators, no .mdx. */
export function deriveId(collectionRoot, filePath) {
	return relative(collectionRoot, filePath).split(sep).join('/').replace(/\.mdx$/, '');
}

/** Reduce MDX to a plain-text approximation for embedding. Naive on purpose. */
export function stripMdx(body) {
	return body
		.replace(/^import\s.+$/gm, '')            // import lines
		.replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')  // links -> link text
		.replace(/<[^>]+>/g, '')                  // JSX / HTML tags
		.replace(/[#*_`>|~-]/g, ' ')              // markdown punctuation
		.replace(/\s+/g, ' ')
		.trim();
}

/** The text we embed for similarity: identity fields + stripped body. */
export function embedText(post) {
	const { title = '', description = '', tags = [], category = '' } = post.frontmatter;
	return [
		title,
		description,
		`Tags: ${tags.join(', ')}`,
		`Category: ${category}`,
		'',
		stripMdx(post.body),
	].join('\n');
}

/** Stable sha256 of exactly what gets embedded — the cache-key base. */
export function contentHash(post) {
	return createHash('sha256').update(embedText(post)).digest('hex');
}

async function walkMdx(dir) {
	const out = [];
	let entries;
	try {
		entries = await readdir(dir, { withFileTypes: true });
	} catch {
		return out; // collection dir absent — fine
	}
	for (const entry of entries) {
		if (entry.name === '_templates') continue;
		const full = join(dir, entry.name);
		if (entry.isDirectory()) {
			out.push(...(await walkMdx(full)));
		} else if (entry.name.endsWith('.mdx')) {
			out.push(full);
		}
	}
	return out;
}

/**
 * Walk content collections and return parsed posts.
 * @returns {Promise<Array<{collection,id,path,frontmatter,body}>>}
 */
export async function listPosts({
	contentRoot = 'src/content',
	collections = PRIMARY_COLLECTIONS,
	includeDrafts = false,
} = {}) {
	const posts = [];
	for (const collection of collections) {
		const root = join(contentRoot, collection);
		for (const path of await walkMdx(root)) {
			const raw = await readFile(path, 'utf8');
			const { data: frontmatter, content: body } = matter(raw);
			if (!includeDrafts && frontmatter.draft === true) continue;
			posts.push({ collection, id: deriveId(root, path), path, frontmatter, body });
		}
	}
	return posts;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `node --test scripts/lib/posts.test.mjs`
Expected: PASS — 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add scripts/lib/posts.mjs scripts/lib/posts.test.mjs
git commit -m "feat(phase13): posts lib — content walker, stripMdx, embedText, hash"
```

---

## Task 4: `lmstudio.mjs` — LM Studio HTTP client

**Files:**
- Create: `scripts/lib/lmstudio.mjs`
- Test: `scripts/lib/lmstudio.test.mjs`

- [ ] **Step 1: Write the failing test**

Create `scripts/lib/lmstudio.test.mjs`:

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { createClient } from './lmstudio.mjs';

/** Build a fake fetch that records calls and returns a canned JSON response. */
function fakeFetch(responseBody, { ok = true, status = 200 } = {}) {
	const calls = [];
	const fn = async (url, init) => {
		calls.push({ url, init });
		return {
			ok,
			status,
			json: async () => responseBody,
			text: async () => JSON.stringify(responseBody),
		};
	};
	fn.calls = calls;
	return fn;
}

const SCHEMA = {
	name: 'demo',
	required: ['value'],
	shape: { type: 'object', properties: { value: { type: 'string' } }, required: ['value'] },
};

test('chatJson sends a json_schema response_format and parses the content string', async () => {
	const fetch = fakeFetch({ choices: [{ message: { content: '{"value":"hi"}' } }] });
	const client = createClient({ fetch, baseUrl: 'http://lm/v1', chatModel: 'm1' });
	const result = await client.chatJson([{ role: 'user', content: 'q' }], SCHEMA);
	assert.deepEqual(result, { value: 'hi' });

	const body = JSON.parse(fetch.calls[0].init.body);
	assert.equal(fetch.calls[0].url, 'http://lm/v1/chat/completions');
	assert.equal(body.response_format.type, 'json_schema');
	assert.equal(body.response_format.json_schema.name, 'demo');
	assert.ok(body.response_format.json_schema.schema);
});

test('chatJson throws when a required key is missing', async () => {
	const fetch = fakeFetch({ choices: [{ message: { content: '{"other":1}' } }] });
	const client = createClient({ fetch, baseUrl: 'http://lm/v1', chatModel: 'm1' });
	await assert.rejects(() => client.chatJson([], SCHEMA), /missing key "value"/);
});

test('vision builds a multimodal content array and requires a vision model', async () => {
	const fetch = fakeFetch({ choices: [{ message: { content: '{"value":"alt"}' } }] });
	const client = createClient({ fetch, baseUrl: 'http://lm/v1', visionModel: 'v1' });
	const result = await client.vision(Buffer.from('img'), 'describe', SCHEMA);
	assert.deepEqual(result, { value: 'alt' });

	const body = JSON.parse(fetch.calls[0].init.body);
	const parts = body.messages[0].content;
	assert.equal(parts[0].type, 'text');
	assert.equal(parts[1].type, 'image_url');
	assert.ok(parts[1].image_url.url.startsWith('data:image/jpeg;base64,'));

	const noVision = createClient({ fetch, baseUrl: 'http://lm/v1', visionModel: '' });
	await assert.rejects(() => noVision.vision(Buffer.from('x'), 'p', SCHEMA), /AI_VISION_MODEL_ID/);
});

test('embed always sends an array input and unwraps single strings', async () => {
	const fetch = fakeFetch({ data: [{ embedding: [1, 2, 3] }] });
	const client = createClient({ fetch, baseUrl: 'http://lm/v1', embeddingModel: 'e1' });
	const single = await client.embed('hello');
	assert.deepEqual(single, [1, 2, 3]);
	assert.ok(Array.isArray(JSON.parse(fetch.calls[0].init.body).input));
});

test('a non-ok response throws', async () => {
	const fetch = fakeFetch({ error: 'boom' }, { ok: false, status: 500 });
	const client = createClient({ fetch, baseUrl: 'http://lm/v1', chatModel: 'm1' });
	await assert.rejects(() => client.chat([{ role: 'user', content: 'q' }]), /500/);
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `node --test scripts/lib/lmstudio.test.mjs`
Expected: FAIL — `Cannot find module './lmstudio.mjs'`.

- [ ] **Step 3: Write the implementation**

Create `scripts/lib/lmstudio.mjs`:

```js
/**
 * Thin fetch-based client for the LM Studio OpenAI-compatible API.
 * `fetch` is injectable for tests; everything else falls back to env vars.
 */
const DEFAULT_BASE_URL = 'http://localhost:1234/v1';

export function createClient({
	fetch = globalThis.fetch,
	baseUrl = process.env.LMSTUDIO_URL || DEFAULT_BASE_URL,
	apiKey = process.env.LMSTUDIO_API_KEY || '',
	chatModel = process.env.AI_MODEL_ID || 'local-model',
	visionModel = process.env.AI_VISION_MODEL_ID || '',
	embeddingModel = process.env.AI_EMBEDDING_MODEL_ID || '',
} = {}) {
	const base = baseUrl.replace(/\/$/, '');

	async function post(path, body) {
		const res = await fetch(`${base}${path}`, {
			method: 'POST',
			headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${apiKey}` },
			body: JSON.stringify(body),
		});
		if (!res.ok) {
			throw new Error(`LM Studio ${path} failed: ${res.status} ${await res.text()}`);
		}
		return res.json();
	}

	function requireKeys(obj, keys, where) {
		for (const k of keys) {
			if (!(k in obj)) throw new Error(`LM Studio ${where}: response missing key "${k}"`);
		}
		return obj;
	}

	function jsonResponseFormat(schema) {
		return {
			type: 'json_schema',
			json_schema: { name: schema.name, schema: schema.shape },
		};
	}

	return {
		async chat(messages, { model = chatModel, temperature = 0.7 } = {}) {
			const data = await post('/chat/completions', { model, messages, temperature, stream: false });
			return data.choices[0].message.content;
		},

		async chatJson(messages, schema, { model = chatModel, temperature = 0.4 } = {}) {
			const data = await post('/chat/completions', {
				model, messages, temperature, stream: false,
				response_format: jsonResponseFormat(schema),
			});
			const parsed = JSON.parse(data.choices[0].message.content);
			return requireKeys(parsed, schema.required ?? [], 'chatJson');
		},

		async embed(textOrTexts) {
			const isArray = Array.isArray(textOrTexts);
			const input = isArray ? textOrTexts : [textOrTexts];
			const data = await post('/embeddings', { model: embeddingModel, input });
			const vectors = data.data.map((d) => d.embedding);
			return isArray ? vectors : vectors[0];
		},

		async vision(imageBuffer, prompt, schema, { model = visionModel } = {}) {
			if (!model) throw new Error('AI_VISION_MODEL_ID is not set');
			const dataUrl = `data:image/jpeg;base64,${imageBuffer.toString('base64')}`;
			const data = await post('/chat/completions', {
				model, stream: false,
				messages: [{
					role: 'user',
					content: [
						{ type: 'text', text: prompt },
						{ type: 'image_url', image_url: { url: dataUrl } },
					],
				}],
				response_format: jsonResponseFormat(schema),
			});
			const parsed = JSON.parse(data.choices[0].message.content);
			return requireKeys(parsed, schema.required ?? [], 'vision');
		},

		async listModels() {
			const res = await fetch(`${base}/models`, {
				headers: { Authorization: `Bearer ${apiKey}` },
			});
			if (!res.ok) throw new Error(`LM Studio /models failed: ${res.status}`);
			return res.json();
		},
	};
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `node --test scripts/lib/lmstudio.test.mjs`
Expected: PASS — 5 tests pass.

- [ ] **Step 5: Run the whole script test suite**

Run: `npm run test:scripts`
Expected: PASS — all three lib test files pass (15 tests total).

- [ ] **Step 6: Commit**

```bash
git add scripts/lib/lmstudio.mjs scripts/lib/lmstudio.test.mjs
git commit -m "feat(phase13): lmstudio lib — chat/chatJson/embed/vision client"
```

---

## Task 5: `geo-fill.mjs` — #5 GEO auto-fill script

**Files:**
- Create: `scripts/geo-fill.mjs`

No unit test file — per the spec (§10) the LM-Studio-dependent orchestration is verified by the manual checklist. Step 3 below is a smoke check that runs without LM Studio.

- [ ] **Step 1: Write the script**

Create `scripts/geo-fill.mjs`:

```js
#!/usr/bin/env node
/**
 * #5 GEO auto-fill — populate aiSummary / keyTakeaways / faq on a post.
 *
 *   npm run geo:fill -- src/content/blog/<slug>.mdx     one post (any draft state)
 *   npm run geo:fill:all                                every published post
 *   ... -- --force                                      overwrite existing fields
 *
 * Fills only empty fields by default. Never touches the body. Never commits.
 */
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import matter from 'gray-matter';
import { createClient } from './lib/lmstudio.mjs';
import { listPosts, stripMdx, ALL_COLLECTIONS } from './lib/posts.mjs';
import { mergeFrontmatter, serialize } from './lib/frontmatter-merge.mjs';

const GREEN = '\x1b[32m';
const DIM = '\x1b[2m';
const RESET = '\x1b[0m';

const GEO_SCHEMA = {
	name: 'geo_fields',
	required: ['aiSummary', 'keyTakeaways', 'faq'],
	shape: {
		type: 'object',
		properties: {
			aiSummary: { type: 'string' },
			keyTakeaways: { type: 'array', items: { type: 'string' }, minItems: 3, maxItems: 6 },
			faq: {
				type: 'array',
				maxItems: 4,
				items: {
					type: 'object',
					properties: { question: { type: 'string' }, answer: { type: 'string' } },
					required: ['question', 'answer'],
				},
			},
		},
		required: ['aiSummary', 'keyTakeaways', 'faq'],
	},
};

export function buildGeoMessages(post) {
	const { title = '', description = '' } = post.frontmatter;
	return [
		{
			role: 'system',
			content:
				'You write GEO (Generative Engine Optimization) metadata for a family blog. ' +
				'Write in third person. aiSummary: 1-2 citation-ready sentences naming the entity. ' +
				'keyTakeaways: 3-6 short scannable bullets. faq: 0-4 genuine reader questions with ' +
				'concrete answers. Return JSON only.',
		},
		{
			role: 'user',
			content: `Title: ${title}\nDescription: ${description}\n\nBody:\n${stripMdx(post.body)}`,
		},
	];
}

function parseArgs(argv) {
	const args = argv.slice(2);
	return {
		force: args.includes('--force'),
		all: args.includes('--all'),
		path: args.find((a) => !a.startsWith('--')),
	};
}

function printDiff(path, changedKeys, merged) {
	console.log(`${GREEN}✓${RESET} ${path}`);
	for (const key of changedKeys) {
		const value = JSON.stringify(merged[key]);
		const shown = value.length > 120 ? `${value.slice(0, 117)}...` : value;
		console.log(`  ${GREEN}+ ${key}${RESET} ${DIM}${shown}${RESET}`);
	}
}

async function fillOne(client, post, force) {
	const generated = await client.chatJson(buildGeoMessages(post), GEO_SCHEMA);
	const raw = await readFile(post.path, 'utf8');
	const { data: existing, content: body } = matter(raw);
	const { merged, changedKeys } = mergeFrontmatter(existing, generated, { force });
	if (changedKeys.length === 0) return { status: 'skipped' };
	await writeFile(post.path, serialize(merged, body), 'utf8');
	return { status: 'filled', changedKeys, merged };
}

async function main() {
	const { force, all, path } = parseArgs(process.argv);
	if (!all && !path) {
		console.error('Usage: npm run geo:fill -- <path-to-post.mdx> [-- --force]');
		console.error('       npm run geo:fill:all [-- --force]');
		process.exit(1);
	}

	const client = createClient();
	let posts;
	if (all) {
		posts = await listPosts({ collections: ALL_COLLECTIONS, includeDrafts: false });
	} else {
		const raw = await readFile(path, 'utf8');
		const { data: frontmatter, content: body } = matter(raw);
		posts = [{ collection: '', id: path, path, frontmatter, body }];
	}

	let filled = 0, skipped = 0, failed = 0;
	for (const post of posts) {
		try {
			const result = await fillOne(client, post, force);
			if (result.status === 'filled') {
				filled++;
				printDiff(post.path, result.changedKeys, result.merged);
			} else {
				skipped++;
				console.log(`${DIM}· ${post.path} — already populated${RESET}`);
			}
		} catch (err) {
			failed++;
			console.error(`✗ ${post.path} — ${err.message}`);
		}
	}
	console.log(`\n${filled} filled, ${skipped} skipped, ${failed} failed`);
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
	main();
}
```

- [ ] **Step 2: Run the no-args smoke check**

Run: `node scripts/geo-fill.mjs`
Expected: prints the two-line `Usage:` message to stderr and exits with code 1. (Verify the exit code: `node scripts/geo-fill.mjs; echo $?` → `1`.)

- [ ] **Step 3: Verify the suite still passes**

Run: `npm run test:scripts`
Expected: PASS — still 15 tests (geo-fill.mjs has no test file; importing it must not break discovery).

- [ ] **Step 4: Commit**

```bash
git add scripts/geo-fill.mjs
git commit -m "feat(phase13): geo-fill script — #5 GEO frontmatter auto-fill"
```

> **Manual verification (requires LM Studio running)** — checklist items 1-2 in `scripts/README.md`, added in Task 11. Not blocking for plan execution.

---

## Task 6: `related-rebuild.mjs` — #6 pure helpers

**Files:**
- Create: `scripts/related-rebuild.mjs` (helpers only — orchestration added in Task 7)
- Test: `scripts/related-rebuild.test.mjs`

- [ ] **Step 1: Write the failing test**

Create `scripts/related-rebuild.test.mjs`:

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { cosineSimilarity, cacheKey, topRelated, buildRelatedMap } from './related-rebuild.mjs';

test('cosineSimilarity: identical=1, orthogonal=0, zero-vector=0', () => {
	assert.equal(cosineSimilarity([1, 0], [1, 0]), 1);
	assert.equal(cosineSimilarity([1, 0], [0, 1]), 0);
	assert.equal(cosineSimilarity([0, 0], [1, 1]), 0);
	assert.ok(Math.abs(cosineSimilarity([1, 0], [1, 1]) - 0.7071) < 0.001);
});

test('cacheKey combines content hash and embedding model id', () => {
	assert.equal(cacheKey('abc', 'model-x'), 'abc:model-x');
	assert.notEqual(cacheKey('abc', 'model-x'), cacheKey('abc', 'model-y'));
});

test('topRelated applies the floor, sorts desc, and caps at the limit', () => {
	const others = [
		{ collection: 'blog', id: 'same', vector: [1, 0] },       // score 1
		{ collection: 'projects', id: 'half', vector: [1, 1] },   // score ~0.707
		{ collection: 'blog', id: 'ortho', vector: [0, 1] },      // score 0 — below floor
	];
	const result = topRelated([1, 0], others, { limit: 3, floor: 0.6 });
	assert.deepEqual(result.map((r) => r.id), ['same', 'half']);
	assert.ok(result[0].score >= result[1].score);
});

test('topRelated honours the limit', () => {
	const others = [
		{ collection: 'blog', id: 'a', vector: [1, 0] },
		{ collection: 'blog', id: 'b', vector: [1, 0] },
		{ collection: 'blog', id: 'c', vector: [1, 0] },
	];
	assert.equal(topRelated([1, 0], others, { limit: 2, floor: 0.6 }).length, 2);
});

test('buildRelatedMap uses composite collection/id keys and excludes self', () => {
	const posts = [
		{ collection: 'blog', id: 'a', vector: [1, 0] },
		{ collection: 'projects', id: 'a', vector: [1, 0] },
	];
	const map = buildRelatedMap(posts, { limit: 3, floor: 0.6 });
	assert.deepEqual(Object.keys(map).sort(), ['blog/a', 'projects/a']);
	assert.deepEqual(map['blog/a'].map((r) => `${r.collection}/${r.id}`), ['projects/a']);
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `node --test scripts/related-rebuild.test.mjs`
Expected: FAIL — `Cannot find module './related-rebuild.mjs'`.

- [ ] **Step 3: Write the helpers**

Create `scripts/related-rebuild.mjs`:

```js
#!/usr/bin/env node
/**
 * #6 related posts — embed every published post, compute cosine similarity,
 * write src/data/related-posts.json (consumed at Astro build time).
 *
 * This file exports its pure helpers (for tests) and runs the rebuild when
 * invoked directly. Orchestration lives below the helpers.
 */

/** Cosine similarity of two equal-length numeric vectors. Returns 0 for a zero vector. */
export function cosineSimilarity(a, b) {
	let dot = 0, na = 0, nb = 0;
	for (let i = 0; i < a.length; i++) {
		dot += a[i] * b[i];
		na += a[i] * a[i];
		nb += b[i] * b[i];
	}
	if (na === 0 || nb === 0) return 0;
	return dot / (Math.sqrt(na) * Math.sqrt(nb));
}

/** Cache key: content hash + embedding model id, so a model swap invalidates the cache. */
export function cacheKey(contentHashValue, embeddingModelId) {
	return `${contentHashValue}:${embeddingModelId}`;
}

/**
 * For one post's vector, the top `limit` of `others` with score >= floor, highest first.
 * `others` is [{ collection, id, vector }] — caller has already excluded self.
 */
export function topRelated(vector, others, { limit = 3, floor = 0.6 } = {}) {
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
		const others = posts.filter(
			(p) => !(p.collection === post.collection && p.id === post.id),
		);
		map[`${post.collection}/${post.id}`] = topRelated(post.vector, others, opts);
	}
	return map;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `node --test scripts/related-rebuild.test.mjs`
Expected: PASS — 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add scripts/related-rebuild.mjs scripts/related-rebuild.test.mjs
git commit -m "feat(phase13): related-rebuild helpers — cosine, topRelated, map builder"
```

---

## Task 7: `related-rebuild.mjs` orchestration + `src/data/` placeholders

**Files:**
- Create: `src/data/related-posts.json` (`{}` placeholder)
- Create: `src/data/related-posts.cache.json` (`{}` placeholder)
- Modify: `scripts/related-rebuild.mjs` (append orchestration below the helpers)

- [ ] **Step 1: Create the committed placeholder data files**

Create `src/data/related-posts.json` with exactly:

```json
{}
```

Create `src/data/related-posts.cache.json` with exactly:

```json
{}
```

These are committed so the Astro build (Task 8's import target) always resolves, even before the first real rebuild.

- [ ] **Step 2: Append the orchestration to `scripts/related-rebuild.mjs`**

Add to the **end** of `scripts/related-rebuild.mjs` (below the helpers from Task 6):

```js
// ---------------------------------------------------------------------------
// Orchestration — runs only when this file is invoked directly.
// ---------------------------------------------------------------------------
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { createClient } from './lib/lmstudio.mjs';
import { listPosts, embedText, contentHash, PRIMARY_COLLECTIONS } from './lib/posts.mjs';

const DATA_DIR = 'src/data';
const OUT_PATH = `${DATA_DIR}/related-posts.json`;
const CACHE_PATH = `${DATA_DIR}/related-posts.cache.json`;

async function readJson(path, fallback) {
	try {
		return JSON.parse(await readFile(path, 'utf8'));
	} catch {
		return fallback;
	}
}

async function main() {
	const embeddingModel = process.env.AI_EMBEDDING_MODEL_ID || '';
	if (!embeddingModel) {
		console.error('AI_EMBEDDING_MODEL_ID is not set — cannot rebuild related posts.');
		process.exit(1);
	}

	const client = createClient();
	const posts = await listPosts({ collections: PRIMARY_COLLECTIONS, includeDrafts: false });
	const cache = await readJson(CACHE_PATH, {});
	const nextCache = {};
	let embedded = 0, fromCache = 0;

	const withVectors = [];
	for (const post of posts) {
		const key = cacheKey(contentHash(post), embeddingModel);
		let vector = cache[key];
		if (vector) {
			fromCache++;
		} else {
			vector = await client.embed(embedText(post));
			embedded++;
		}
		nextCache[key] = vector;
		withVectors.push({ collection: post.collection, id: post.id, vector });
	}

	const map = buildRelatedMap(withVectors, { limit: 3, floor: 0.6 });
	const orphans = Object.values(map).filter((r) => r.length === 0).length;

	await mkdir(DATA_DIR, { recursive: true });
	await writeFile(OUT_PATH, `${JSON.stringify(map, null, '\t')}\n`, 'utf8');
	await writeFile(CACHE_PATH, `${JSON.stringify(nextCache, null, '\t')}\n`, 'utf8');

	console.log(`${embedded} embedded, ${fromCache} from cache, ${orphans} posts with 0 relations`);
	console.log(`Wrote ${OUT_PATH}`);
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
	main();
}
```

- [ ] **Step 3: Run the no-env smoke check**

Run: `AI_EMBEDDING_MODEL_ID= node scripts/related-rebuild.mjs; echo $?`
Expected: prints `AI_EMBEDDING_MODEL_ID is not set — cannot rebuild related posts.` and exits `1`.

- [ ] **Step 4: Verify the suite still passes**

Run: `npm run test:scripts`
Expected: PASS — still 20 tests; appending the orchestration must not break the helper tests.

- [ ] **Step 5: Commit**

```bash
git add scripts/related-rebuild.mjs src/data/related-posts.json src/data/related-posts.cache.json
git commit -m "feat(phase13): related-rebuild orchestration + src/data placeholders"
```

> **Manual verification (requires LM Studio running)** — checklist item 3 in `scripts/README.md`.

---

## Task 8: Rewrite `getRelatedPostsCrossCollection` to read the JSON

**Files:**
- Modify: `src/utils/getPosts.ts:144-181` (the `getRelatedPostsCrossCollection` function)

- [ ] **Step 1: Replace the function body**

In `src/utils/getPosts.ts`, replace the entire existing `getRelatedPostsCrossCollection` function (the JSDoc comment plus the function, currently lines ~144-181) with:

```ts
// related-posts.json shape: { "<collection>/<id>": [{ id, collection, score }] }
// Generated by `npm run related:rebuild` (scripts/related-rebuild.mjs).
import relatedPostsData from '../data/related-posts.json';

type RelatedCollection = 'blog' | 'projects' | 'field-notes';
type RelatedRef = { id: string; collection: RelatedCollection; score: number };
const relatedMap = relatedPostsData as Record<string, RelatedRef[]>;

/**
 * Related entries across blog / projects / field-notes, sourced from the
 * precomputed embedding-similarity map in `src/data/related-posts.json`.
 *
 * Returns [] when the post has no entry — either it predates the last
 * `related:rebuild`, or nothing cleared the similarity floor. PostFooterNav
 * then renders nothing, which is the intended "no strongly-related posts".
 */
export async function getRelatedPostsCrossCollection(
	currentEntry: { id: string; collection: string },
	limit = 3,
): Promise<
	Array<CollectionEntry<'blog'> | CollectionEntry<'projects'> | CollectionEntry<'field-notes'>>
> {
	const refs = relatedMap[`${currentEntry.collection}/${currentEntry.id}`];
	if (!refs || refs.length === 0) return [];

	const [blog, projects, fieldNotes] = await Promise.all([
		getBlogPosts(),
		getProjects(),
		getFieldNotes(),
	]);

	const byKey = new Map<
		string,
		CollectionEntry<'blog'> | CollectionEntry<'projects'> | CollectionEntry<'field-notes'>
	>();
	for (const entry of [...blog, ...projects, ...fieldNotes]) {
		byKey.set(`${entry.collection}/${entry.id}`, entry);
	}

	const resolved: Array<
		CollectionEntry<'blog'> | CollectionEntry<'projects'> | CollectionEntry<'field-notes'>
	> = [];
	for (const ref of refs.slice(0, limit)) {
		const entry = byKey.get(`${ref.collection}/${ref.id}`);
		if (entry) resolved.push(entry);
	}
	return resolved;
}
```

Notes:
- The `import` line must sit with the other imports — move `import relatedPostsData ...` to the top of the file alongside `import { getCollection, type CollectionEntry } from 'astro:content';` if your linter requires imports-first; the comment above it can stay at the top too.
- The standalone `getRelatedPosts` heuristic function (above this one) is **left untouched**.
- The three layout wrappers already call `getRelatedPostsCrossCollection(post, 3)` with a full `CollectionEntry`, which satisfies the new `{ id, collection }` parameter — no caller changes needed.

- [ ] **Step 2: Type-check**

Run: `npm run check`
Expected: PASS — 0 errors. If it reports the JSON import is not a module, add `"resolveJsonModule": true` to `compilerOptions` in `tsconfig.json`, then re-run.

- [ ] **Step 3: Build**

Run: `npm run build`
Expected: PASS — build completes, `dist/` produced. (With the `{}` placeholder data file, every post resolves to `[]` related — the related section simply does not render. That is correct pre-rebuild behaviour.)

- [ ] **Step 4: Commit**

```bash
git add src/utils/getPosts.ts tsconfig.json
git commit -m "feat(phase13): related posts read from src/data/related-posts.json"
```

> **Manual verification (after a real `related:rebuild`)** — checklist item 4 in `scripts/README.md`.

---

## Task 9: `image-watcher.mjs` — #7 pure helpers

**Files:**
- Create: `scripts/image-watcher.mjs` (helpers only — orchestration added in Task 10)
- Test: `scripts/image-watcher.test.mjs`

- [ ] **Step 1: Write the failing test**

Create `scripts/image-watcher.test.mjs`:

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { join } from 'node:path';
import {
	deriveCollectionSlug,
	sanitizeName,
	classifyFormat,
	resolveOutputPath,
	buildMarkdownSnippet,
} from './image-watcher.mjs';

test('deriveCollectionSlug parses inbox/<collection>/<slug>/<file>', () => {
	const root = 'obsidian-templates/inbox';
	assert.deepEqual(deriveCollectionSlug(root, join(root, 'blog', 'my-post', 'a.heic')), {
		collection: 'blog',
		slug: 'my-post',
		filename: 'a.heic',
	});
});

test('deriveCollectionSlug rejects bad depth and unknown collections', () => {
	const root = 'obsidian-templates/inbox';
	assert.equal(deriveCollectionSlug(root, join(root, 'blog', 'a.heic')), null);
	assert.equal(deriveCollectionSlug(root, join(root, 'nope', 'slug', 'a.heic')), null);
});

test('sanitizeName lowercases, hyphenates, strips junk, and keeps the extension', () => {
	assert.deepEqual(sanitizeName('My Photo 1.HEIC'), { base: 'my-photo-1', ext: '.heic' });
	assert.deepEqual(sanitizeName('weird!!name.JPG'), { base: 'weirdname', ext: '.jpg' });
	assert.deepEqual(sanitizeName('***.png'), { base: 'image', ext: '.png' });
});

test('classifyFormat distinguishes heic / web / unsupported', () => {
	assert.equal(classifyFormat('.heic'), 'heic');
	assert.equal(classifyFormat('.HEIF'), 'heic');
	assert.equal(classifyFormat('.jpg'), 'web');
	assert.equal(classifyFormat('.png'), 'web');
	assert.equal(classifyFormat('.webp'), 'web');
	assert.equal(classifyFormat('.gif'), 'unsupported');
});

test('resolveOutputPath suffixes -2, -3 on collision', () => {
	const taken = new Set(['out/a.jpg', 'out/a-2.jpg']);
	const exists = (p) => taken.has(p);
	assert.equal(resolveOutputPath('out', 'b', '.jpg', exists), join('out', 'b.jpg'));
	assert.equal(resolveOutputPath('out', 'a', '.jpg', exists), join('out', 'a-3.jpg'));
});

test('buildMarkdownSnippet produces a relative assets image tag', () => {
	assert.equal(
		buildMarkdownSnippet('blog', 'my-post', 'photo.jpg', 'A sunset'),
		'![A sunset](../../assets/blog/my-post/photo.jpg)',
	);
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `node --test scripts/image-watcher.test.mjs`
Expected: FAIL — `Cannot find module './image-watcher.mjs'`.

- [ ] **Step 3: Write the helpers**

Create `scripts/image-watcher.mjs`:

```js
#!/usr/bin/env node
/**
 * #7 image helper — watch an inbox folder; on a dropped photo, convert it,
 * name it, place it under src/assets/, write AI alt text, print a Markdown
 * snippet, and delete the source.
 *
 * This file exports its pure helpers (for tests) and runs the watcher when
 * invoked directly. Orchestration lives below the helpers.
 */
import { relative, sep, join, extname, basename } from 'node:path';

export const KNOWN_COLLECTIONS = ['blog', 'projects', 'field-notes'];
const WEB_FORMATS = new Set(['.jpg', '.jpeg', '.png', '.webp']);
const HEIC_FORMATS = new Set(['.heic', '.heif']);

/** inbox/<collection>/<slug>/<file> -> { collection, slug, filename } or null. */
export function deriveCollectionSlug(inboxRoot, filePath) {
	const parts = relative(inboxRoot, filePath).split(sep);
	if (parts.length !== 3) return null;
	const [collection, slug, filename] = parts;
	if (!KNOWN_COLLECTIONS.includes(collection)) return null;
	return { collection, slug, filename };
}

/** Lowercase, spaces -> hyphens, strip non [a-z0-9-]; empty base falls back to "image". */
export function sanitizeName(filename) {
	const ext = extname(filename).toLowerCase();
	const base = basename(filename, extname(filename))
		.toLowerCase()
		.replace(/\s+/g, '-')
		.replace(/[^a-z0-9-]/g, '')
		.replace(/-+/g, '-')
		.replace(/^-|-$/g, '');
	return { base: base || 'image', ext };
}

/** 'heic' (needs transcode), 'web' (copy as-is), or 'unsupported'. */
export function classifyFormat(ext) {
	const e = ext.toLowerCase();
	if (HEIC_FORMATS.has(e)) return 'heic';
	if (WEB_FORMATS.has(e)) return 'web';
	return 'unsupported';
}

/** Pick an output path, suffixing -2, -3... on collision. `exists` is injectable. */
export function resolveOutputPath(dir, base, ext, exists) {
	let candidate = join(dir, `${base}${ext}`);
	let n = 2;
	while (exists(candidate)) {
		candidate = join(dir, `${base}-${n}${ext}`);
		n++;
	}
	return candidate;
}

/** The ready-to-paste Markdown image tag (paths are relative to src/content/<collection>/). */
export function buildMarkdownSnippet(collection, slug, filename, altText) {
	return `![${altText}](../../assets/${collection}/${slug}/${filename})`;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `node --test scripts/image-watcher.test.mjs`
Expected: PASS — 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add scripts/image-watcher.mjs scripts/image-watcher.test.mjs
git commit -m "feat(phase13): image-watcher helpers — path/name/format/snippet"
```

---

## Task 10: `image-watcher.mjs` orchestration

**Files:**
- Modify: `scripts/image-watcher.mjs` (append orchestration below the helpers)

- [ ] **Step 1: Append the orchestration to `scripts/image-watcher.mjs`**

Add to the **end** of `scripts/image-watcher.mjs` (below the helpers from Task 9):

```js
// ---------------------------------------------------------------------------
// Orchestration — runs only when this file is invoked directly.
// ---------------------------------------------------------------------------
import { readFile, writeFile, mkdir, rm, access } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { dirname } from 'node:path';
import { pathToFileURL } from 'node:url';
import { watch } from 'chokidar';
import sharp from 'sharp';
import { createClient } from './lib/lmstudio.mjs';

const INBOX_ROOT = 'obsidian-templates/inbox';
const ASSETS_ROOT = 'src/assets';
const CONTENT_ROOT = 'src/content';

const ALT_SCHEMA = {
	name: 'image_alt',
	required: ['altText'],
	shape: {
		type: 'object',
		properties: { altText: { type: 'string' } },
		required: ['altText'],
	},
};

async function postExists(collection, slug) {
	try {
		await access(join(CONTENT_ROOT, collection, `${slug}.mdx`));
		return true;
	} catch {
		return false;
	}
}

async function processFile(client, filePath) {
	const derived = deriveCollectionSlug(INBOX_ROOT, filePath);
	if (!derived) {
		console.error(`✗ ${filePath} — expected inbox/<collection>/<slug>/<file>`);
		return;
	}
	const { collection, slug, filename } = derived;
	if (!(await postExists(collection, slug))) {
		console.error(`✗ ${filePath} — no post at ${collection}/${slug}.mdx`);
		return;
	}

	const { base, ext } = sanitizeName(filename);
	const format = classifyFormat(ext);
	if (format === 'unsupported') {
		console.error(`✗ ${filePath} — unsupported image format ${ext}`);
		return;
	}

	const sourceBuffer = await readFile(filePath);
	// A JPEG buffer is always produced for the vision payload.
	const jpegBuffer = await sharp(sourceBuffer).jpeg({ quality: 90 }).toBuffer();
	// Output: transcoded JPEG for HEIC, original bytes for web formats.
	const outBuffer = format === 'heic' ? jpegBuffer : sourceBuffer;
	const outExt = format === 'heic' ? '.jpg' : ext;

	const postBody = await readFile(join(CONTENT_ROOT, collection, `${slug}.mdx`), 'utf8');
	const prompt =
		'Write concise alt text describing what is literally visible in this image ' +
		'(not what the post is about). One sentence. Context from the post:\n\n' +
		postBody.slice(0, 1500);
	const { altText } = await client.vision(jpegBuffer, prompt, ALT_SCHEMA);

	const outDir = join(ASSETS_ROOT, collection, slug);
	await mkdir(outDir, { recursive: true });
	const outPath = resolveOutputPath(outDir, base, outExt, existsSync);
	await writeFile(outPath, outBuffer);

	const outName = basename(outPath);
	console.log(`✓ ${filePath}`);
	console.log(`  → ${outPath}`);
	console.log(`  ${buildMarkdownSnippet(collection, slug, outName, altText)}`);
	await rm(filePath);
}

async function main() {
	const visionModel = process.env.AI_VISION_MODEL_ID || '';
	if (!visionModel) {
		console.error('AI_VISION_MODEL_ID is not set — cannot write alt text. Exiting.');
		process.exit(1);
	}

	await mkdir(INBOX_ROOT, { recursive: true });
	const client = createClient();
	client
		.listModels()
		.then(() => console.log('LM Studio reachable.'))
		.catch(() => console.warn('⚠ LM Studio not reachable yet — will retry per dropped file.'));

	// chokidar v4 watches a directory path (no glob support). Recursive by default.
	let queue = Promise.resolve();
	const watcher = watch(INBOX_ROOT, {
		ignoreInitial: false,
		ignored: (p) => basename(p).startsWith('.'),
		awaitWriteFinish: { stabilityThreshold: 500, pollInterval: 100 },
	});
	watcher.on('add', (filePath) => {
		// Serialize processing — one file at a time.
		queue = queue
			.then(() => processFile(client, filePath))
			.catch((err) => console.error(`✗ ${filePath} — ${err.message}`));
	});

	console.log(`Watching ${INBOX_ROOT}/ for new images … (Ctrl-C to stop)`);
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
	main();
}
```

Note: `dirname` is imported for completeness of the path helpers block; if your linter flags it as unused, remove it from the import — `basename` and `join` are the ones actually used here (`join`/`basename`/`extname` come from the Task 9 import line at the top of the file, so this second import line only needs `dirname` removed or kept per linter).

Correction to the import line: the top of the file already imports `relative, sep, join, extname, basename` from `node:path`. Do **not** re-import those. The orchestration block above should import only what is new — replace its `import { dirname } from 'node:path';` line with nothing if `dirname` is unused, i.e. delete that line.

- [ ] **Step 2: Run the no-env smoke check**

Run: `AI_VISION_MODEL_ID= node scripts/image-watcher.mjs; echo $?`
Expected: prints `AI_VISION_MODEL_ID is not set — cannot write alt text. Exiting.` and exits `1`.

- [ ] **Step 3: Verify the suite still passes**

Run: `npm run test:scripts`
Expected: PASS — still 26 tests; appending the orchestration must not break the helper tests.

- [ ] **Step 4: Lint the scripts directory**

Run: `npm run lint`
Expected: PASS for `scripts/` files (no new errors introduced). If ESLint flags an unused import in `image-watcher.mjs`, remove that import and re-run.

- [ ] **Step 5: Commit**

```bash
git add scripts/image-watcher.mjs
git commit -m "feat(phase13): image-watcher orchestration — chokidar + sharp + vision"
```

> **Manual verification (requires LM Studio + a vision model)** — checklist item 5 in `scripts/README.md`.

---

## Task 11: Wrap-up — README, CI, scaffolding guard, CLAUDE.md

**Files:**
- Create: `scripts/README.md`
- Modify: `.github/workflows/ci.yml`
- Modify: `dais-bridge.tests/ScaffoldingTests.cs`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Write `scripts/README.md`**

Create `scripts/README.md`:

```markdown
# Authoring scripts

Author-run helpers for the Obsidian → `.mdx` → Astro pipeline. All three call
the local LM Studio instance and never run at build or CI time. Each leaves a
diff you review before committing.

Requires these env vars (in `.env`, see `.env.example`): `LMSTUDIO_URL`,
`LMSTUDIO_API_KEY`, `AI_MODEL_ID`, `AI_VISION_MODEL_ID`, `AI_EMBEDDING_MODEL_ID`.

| Script | Command | What it does |
| ------ | ------- | ------------ |
| `geo-fill.mjs` | `npm run geo:fill -- <post.mdx>` / `npm run geo:fill:all` | Fill `aiSummary` / `keyTakeaways` / `faq`. `-- --force` overwrites. |
| `related-rebuild.mjs` | `npm run related:rebuild` | Embed every published post, write `src/data/related-posts.json`. |
| `image-watcher.mjs` | `npm run image:watch` | Watch `obsidian-templates/inbox/<collection>/<slug>/`; convert + place + alt-text dropped photos. |

Unit tests: `npm run test:scripts`.

## Manual verification checklist

Requires LM Studio running with chat, embedding, and vision models loaded.

1. `npm run geo:fill -- src/content/blog/<draft>.mdx` on a post with empty GEO
   fields → fields populated, diff printed, body unchanged.
2. Re-run → reports "already populated"; add `-- --force` → regenerates.
3. `npm run related:rebuild` → `src/data/related-posts.json` written; every
   entry has `score >= 0.6` and `<= 3` per post; second run reports all-from-cache.
4. `npm run build` → `PostFooterNav` related section renders for a post with
   relations, renders nothing for an orphan post.
5. `npm run image:watch`, drop a `.heic` into
   `obsidian-templates/inbox/blog/<slug>/` → JPEG appears under
   `src/assets/blog/<slug>/`, alt text + Markdown snippet printed, inbox source
   deleted. Drop into a bad folder name → file left in place, error logged.
```

- [ ] **Step 2: Add the `test:scripts` step to CI**

In `.github/workflows/ci.yml`, in the `verify` job, add this step immediately
after the `Prettier (check only)` step and before the `Build` step:

```yaml
      - name: Script unit tests
        # node:test suite for the authoring scripts (scripts/). No LM Studio
        # needed — only the pure-logic units run in CI.
        run: npm run test:scripts
```

- [ ] **Step 3: Add the scaffolding guard test**

In `dais-bridge.tests/ScaffoldingTests.cs`, add this `[Fact]` inside the
`ScaffoldingTests` class (after the existing `AstroConfig_...` test):

```csharp
    [Fact]
    public void PackageJson_ShouldDeclareAuthoringScriptsAndDeps()
    {
        // Phase 13 authoring scripts must stay wired into package.json so the
        // npm-run entry points and their two deps don't silently disappear.
        var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (currentDir != null && !File.Exists(Path.Combine(currentDir.FullName, "astro.config.mjs")))
        {
            currentDir = currentDir.Parent;
        }

        Assert.NotNull(currentDir);
        var packageJsonPath = Path.Combine(currentDir.FullName, "package.json");
        Assert.True(File.Exists(packageJsonPath), $"package.json should exist at {packageJsonPath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
        var root = doc.RootElement;

        var scripts = root.GetProperty("scripts");
        foreach (var name in new[] { "geo:fill", "geo:fill:all", "related:rebuild", "image:watch", "test:scripts" })
        {
            Assert.True(scripts.TryGetProperty(name, out _), $"package.json scripts missing '{name}'");
        }

        var deps = root.GetProperty("dependencies");
        foreach (var dep in new[] { "gray-matter", "chokidar" })
        {
            Assert.True(deps.TryGetProperty(dep, out _), $"package.json dependencies missing '{dep}'");
        }
    }
```

- [ ] **Step 4: Run the scaffolding test**

Run: `dotnet test dais-bridge.tests/dais-bridge.tests.csproj --filter "FullyQualifiedName~ScaffoldingTests"`
Expected: PASS — 3 tests pass (the 2 existing + the new one). It passes immediately because Task 1 already updated `package.json`; this is a regression guard.

- [ ] **Step 5: Update `CLAUDE.md`**

In `CLAUDE.md`, add this table immediately after the existing **"Commands (Astro)"** table (before the "Commands (DAIS Bridge / Phase 11 services)" table):

```markdown
## Commands (Authoring scripts — Phase 13)

| Task                      | Command                          | When                  |
| ------------------------- | -------------------------------- | --------------------- |
| Fill GEO frontmatter      | `npm run geo:fill -- <post.mdx>` | Before publishing     |
| Fill GEO across the site  | `npm run geo:fill:all`           | Bulk backfill         |
| Rebuild related posts     | `npm run related:rebuild`        | After adding/editing posts |
| Watch image inbox         | `npm run image:watch`            | While adding photos   |
| Script unit tests         | `npm run test:scripts`           | CI runs this          |

Full guide: [scripts/README.md](scripts/README.md). All three call local LM Studio.
```

- [ ] **Step 6: Final verification — full check suite**

Run each and confirm PASS:
- `npm run test:scripts` → 26 tests pass
- `npm run check` → 0 errors
- `npm run lint` → no new errors in `scripts/`
- `npm run build` → build completes

- [ ] **Step 7: Commit**

```bash
git add scripts/README.md .github/workflows/ci.yml dais-bridge.tests/ScaffoldingTests.cs CLAUDE.md
git commit -m "docs(phase13): scripts README, CI step, scaffolding guard, CLAUDE.md"
```

---

## Self-Review

**Spec coverage:**
- §6.1 `lmstudio.mjs` → Task 4. §6.2 `posts.mjs` → Task 3. §6.3 `frontmatter-merge.mjs` → Task 2.
- §7 #5 GEO auto-fill → Task 5. §8 #6 related posts → Tasks 6 (helpers), 7 (orchestration + data files), 8 (util rewrite). §9 #7 image helper → Tasks 9 (helpers), 10 (orchestration).
- §10 testing → unit tests in Tasks 2/3/4/6/9; `test:scripts` script in Task 1; CI step + `ScaffoldingTests.cs` guard in Task 11; manual checklist in `scripts/README.md` (Task 11).
- §11 build sequencing → Task order matches (lib → #5 → #6 → #7 → wrap-up). §1 architecture (deps, env vars, npm scripts) → Task 1. CLAUDE.md command table → Task 11.
- No gaps.

**Placeholder scan:** every code step contains the full file or the exact block to add. The one conditional (`tsconfig.json` `resolveJsonModule` in Task 8) is a run-and-fix instruction with the exact change, not a placeholder.

**Type consistency:** the schema object shape `{ name, required, shape }` is consistent across `lmstudio.mjs` (`createClient`/`chatJson`/`vision`), `geo-fill.mjs` (`GEO_SCHEMA`), `related-rebuild.mjs` (n/a — no schema), and `image-watcher.mjs` (`ALT_SCHEMA`). `cacheKey(contentHashValue, embeddingModelId)`, `topRelated(vector, others, opts)`, `buildRelatedMap(posts, opts)` signatures match between Task 6's definitions and Task 7's usage. `deriveCollectionSlug`/`sanitizeName`/`classifyFormat`/`resolveOutputPath`/`buildMarkdownSnippet` signatures match between Task 9's definitions and Task 10's usage. `getRelatedPostsCrossCollection({ id, collection }, limit)` in Task 8 matches the existing caller pattern `getRelatedPostsCrossCollection(post, 3)`.
