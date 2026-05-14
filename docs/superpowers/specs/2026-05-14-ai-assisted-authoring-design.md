# AI-Assisted Authoring — GEO Auto-Fill, Related Posts, Image Helper (Phase 13)

**Status:** Design approved 2026-05-14
**Author:** Darbee (with Claude Code)
**Branch target:** `feature/ai-assisted-authoring` (off `master`)
**Phase context:** Phase 13 of the Darbees site work. Three author-facing
enhancements to the Obsidian → `.mdx` → Astro pipeline, delivered as one
combined spec because they share a single LM Studio client and content-walking
library. Depends on LM Studio being reachable (the Phase 12 stack already
documents this); does not depend on the DAIS Bridge gateway or ArangoDB.

---

## 1. Problem

Authoring a post in this repo means hand-writing several pieces of metadata
that are mechanical but tedious, and that are easy to skip:

- **GEO / E-E-A-T frontmatter** (`aiSummary`, `keyTakeaways`, `faq`) — every
  collection schema carries these `llmFields`, and the templates show what good
  ones look like, but filling them well for each post is real work. Skipped
  fields weaken the site's whole "quotable by AI agents" strategy.
- **Related posts** — `getRelatedPostsCrossCollection` already exists and is
  already rendered by `PostFooterNav`, but it uses a same-category / shared-tag
  heuristic with an `...others` filler, so it **always returns 3 posts even
  when nothing is genuinely related**. Tag hygiene is the only lever, and it's
  a blunt one.
- **Images** — photos come off a phone as HEIC, need converting, need a
  sensible filename, need to land in `src/assets/<collection>/<slug>/`, and
  need alt text written. Today that's four manual steps per image.

All three are well within reach of the local LM Studio instance (chat, vision,
and embedding models) that the repo already runs for the Phase 11 memory layer.

## 2. Goal

Three author-run Node scripts, invoked on demand, that each remove one of the
chores above:

- `npm run geo:fill <path>` — fill missing GEO frontmatter on one post (or
  `geo:fill:all` across the site), review-then-commit, never destructive.
- `npm run related:rebuild` — embed every published post, compute cosine
  similarity, write a precomputed `src/data/related-posts.json` that the
  existing related-posts function reads at build time. Real similarity, with a
  floor, replaces the heuristic.
- `npm run image:watch` — a long-running folder watcher: drop a phone photo in
  an inbox folder, get a converted, named, placed image plus AI-written alt
  text and a ready-to-paste Markdown snippet.

Everything is opt-in, explicit, and leaves a diff the author reviews before
committing. No AI runs at build time or in CI.

## 3. Non-goals

- **No auto-commit / git integration** in any script — the author reviews the
  diff and commits.
- **No AI calls at build or CI time** — all three scripts are author-run only.
  `src/data/related-posts.json` is committed to the repo precisely so the
  Cloudflare Pages build never needs LM Studio.
- **No generation of `entityMentions`, `sources`, or `imageAlt` frontmatter by
  #5** — those are too judgment-heavy / source-of-truth-sensitive to autofill.
  (`#7` writes alt text, but as a Markdown snippet for an inline image, not as
  the post's `imageAlt` hero field.)
- **No frontmatter mutation by #6** — related posts live only in the generated
  JSON, never written back into post files.
- **No watch mode for #5 / #6** — explicit on-demand only. Only #7 watches,
  because its UX (drop a file, get a result) needs it.
- **No new `compose.yaml` service** — these are local CLI scripts, not gateway
  features. They join the existing `scripts/` directory next to
  `check-internal-links.mjs`.
- **No batch / re-processing of images already in `src/assets/`** — #7 only
  handles newly-dropped files.
- **No changes to the `cms/` directory** — it remains historical dead code.

## 4. Constraints + context

- **Runtime:** Node (same version the Astro 6 build uses). Scripts are ESM
  `.mjs`, matching the existing `scripts/check-internal-links.mjs`.
- **LM Studio:** reachable at `LMSTUDIO_URL` (host: `http://localhost:1234/v1`),
  Bearer token in `LMSTUDIO_API_KEY`. OpenAI-compatible API:
  - `/v1/chat/completions` supports `response_format: { type: "json_schema",
    json_schema: { name, schema } }`. The result comes back as a **JSON string**
    in `choices[0].message.content` and must be `JSON.parse`d.
  - `/v1/chat/completions` accepts OpenAI-style multimodal `content` arrays with
    `{ type: "image_url", image_url: { url: "data:image/jpeg;base64,..." } }`
    parts — used for the vision call.
  - `/v1/embeddings` for the related-posts vectors.
  - `/v1/models` lists loaded models — used as a startup reachability ping.
  - Structured output and vision are **model-dependent**; a model that supports
    neither will fail. Scripts validate output and fail loud rather than
    writing garbage.
- **Existing dependency:** `sharp` (`^0.34.3`) is **already** in
  `package.json` — #7 reuses it, does not add it.
- **New dependencies (2):** `gray-matter` (frontmatter parse/stringify) and
  `chokidar` (folder watcher for #7).
- **Existing related-posts wiring (do not rebuild):**
  - `src/utils/getPosts.ts` → `getRelatedPostsCrossCollection(entry, limit)`.
  - `BlogPostLayout` / `ProjectPostLayout` / `FieldNotesLayout` each already
    call it and pass `related` → `PostLayout` → `PostFooterNav`.
  - `PostFooterNav.astro` already renders the related section in full (hero
    thumb, category, title, description).
  - #6 changes **only** the body of `getRelatedPostsCrossCollection` and adds
    the generator script + data files. No new component, no layout edits.
- **Content collections:** `blog`, `projects`, `field-notes` participate in
  related posts and GEO fill. `books` has its own page (`bookshelf/[id].astro`)
  and its own schema shape — **in scope for #5** (it carries the same
  `aiSummary` / `keyTakeaways` / `faq` fields) but **out of scope for #6**
  (no cross-collection related rendering for books today).
- **Inbox location:** `obsidian-templates/` is already gitignored, so the #7
  inbox at `obsidian-templates/inbox/<collection>/<slug>/` is never committed.

## 5. Architecture overview

```
scripts/
  geo-fill.mjs          #5  — one-shot, fills GEO frontmatter
  related-rebuild.mjs   #6  — one-shot, writes src/data/related-posts.json
  image-watcher.mjs     #7  — long-running, watches the inbox folder
  lib/
    lmstudio.mjs        fetch-based LM Studio client (chat/chatJson/embed/vision)
    posts.mjs           walks src/content/**/*.mdx, parses frontmatter, hashes
    frontmatter-merge.mjs   non-destructive frontmatter merge + serialize
  *.test.mjs            node:test unit tests, colocated

src/
  data/                 NEW — committed build inputs
    related-posts.json        postId -> [{ id, collection, score }]
    related-posts.cache.json  embedding cache keyed by contentHash+modelId
  utils/getPosts.ts     MODIFIED — getRelatedPostsCrossCollection reads the JSON
```

Data flow:

```
#5  post.mdx ──posts.mjs──▶ {frontmatter, body}
              ──lmstudio.chatJson(schema)──▶ {aiSummary,keyTakeaways,faq}
              ──frontmatter-merge──▶ post.mdx (frontmatter only, body untouched)
              prints diff. author commits.

#6  all posts ──posts.mjs──▶ [{id,collection,hash,embedText}]
              ──cache lookup OR lmstudio.embed──▶ vectors
              ──cosine, top-3, score>=0.6──▶ src/data/related-posts.json
              ──(build time)──▶ getRelatedPostsCrossCollection ──▶ PostFooterNav

#7  inbox/<collection>/<slug>/photo.heic
              ──chokidar add event──▶ derive (collection, slug); validate post
              ──sharp──▶ JPEG buffer
              ──lmstudio.vision(image, postBody, schema)──▶ {altText}
              ──copy/transcode──▶ src/assets/<collection>/<slug>/<name>.<ext>
              prints Markdown snippet; deletes inbox source.
```

Each unit has one job and a narrow interface:

- **`lmstudio.mjs`** — knows the LM Studio HTTP API and nothing about posts.
  Exports `chat(messages, opts)`, `chatJson(messages, schema, opts)`,
  `embed(textOrTexts)`, `vision(imageBuffer, prompt, schema)`, `listModels()`.
  Reads `LMSTUDIO_URL` / `LMSTUDIO_API_KEY` / model-id env vars. Throws on
  non-2xx and on JSON that fails schema-shape validation.
- **`posts.mjs`** — knows the content directory layout and nothing about LM
  Studio. Exports `listPosts({ collections, includeDrafts })` →
  `[{ collection, id, path, frontmatter, body, contentHash }]`, plus
  `embedText(post)` (title + description + tags + category + stripped body)
  and a `stripMdx(body)` helper.
- **`frontmatter-merge.mjs`** — pure functions, no I/O. `mergeFrontmatter(existing,
  generated, { force })` and `serialize(frontmatter, body)`. Preserves existing
  key order, appends new keys, leaves the body string byte-identical.

## 6. Component — `scripts/lib/` (shared)

### 6.1 `lmstudio.mjs`

A thin `fetch` wrapper. No SDK dependency.

- **Config** (env, with the Phase 12 names): `LMSTUDIO_URL` (default
  `http://localhost:1234/v1`), `LMSTUDIO_API_KEY`, `AI_MODEL_ID` (chat),
  `AI_VISION_MODEL_ID` (**new**), `AI_EMBEDDING_MODEL_ID` (**new**).
- `chatJson(messages, schema, opts)` — POSTs `/chat/completions` with
  `response_format: { type: "json_schema", json_schema: { name: schema.name,
  schema: schema.shape } }`, `stream: false`. Parses `choices[0].message.content`
  with `JSON.parse`, then shallow-validates required keys are present; throws a
  descriptive error otherwise.
- `chat(messages, opts)` — same minus `response_format`; returns the raw string.
- `embed(textOrTexts)` — POSTs `/embeddings` with `AI_EMBEDDING_MODEL_ID`;
  returns `number[]` or `number[][]`.
- `vision(imageBuffer, prompt, schema)` — builds a single user message whose
  `content` is `[{ type: "text", text: prompt }, { type: "image_url",
  image_url: { url: "data:image/jpeg;base64,..." } }]` and calls the same
  json_schema path with `AI_VISION_MODEL_ID`.
- `listModels()` — GETs `/models`; used by #7's startup ping.
- All requests send the body as a pre-serialized string with an explicit
  `Content-Type: application/json` header (avoids the chunked-encoding class of
  problem seen with the .NET client in Phase 11).

### 6.2 `posts.mjs`

- `listPosts({ collections = ['blog','projects','field-notes','books'],
  includeDrafts = false })` — walks `src/content/<collection>/**/*.mdx` (skips
  `_templates/`), parses each with `gray-matter`, derives the Astro-style `id`
  from the path relative to the collection root (minus `.mdx`), computes
  `contentHash` (sha256 of frontmatter-relevant fields + body), and filters out
  `draft: true` unless `includeDrafts`.
- `stripMdx(body)` — removes `import` lines, JSX tags, and Markdown punctuation
  to a plain-text approximation for embedding. Naive on purpose; embedding
  quality does not need a real MDX parser.
- `embedText(post)` — `\`${title}\n${description}\nTags: ${tags}\nCategory:
  ${category}\n\n${stripMdx(body)}\``.

### 6.3 `frontmatter-merge.mjs`

- `mergeFrontmatter(existing, generated, { force })` — for each generated key:
  if `force`, overwrite; otherwise write only when the existing value is
  missing, `null`, `''`, or `[]` ("empty"). Returns `{ merged, changedKeys }`.
- `serialize(frontmatter, body)` — re-emits the file. Existing keys keep their
  original order; newly-added keys are appended. The body string is passed
  through untouched. (`gray-matter`'s stringify is used for the YAML block;
  field-order preservation is asserted by a unit test in §10.)

## 7. Component — #5 GEO auto-fill (`scripts/geo-fill.mjs`)

**Invocation**

- `npm run geo:fill -- src/content/blog/<slug>.mdx` — single post.
- `npm run geo:fill:all` — every post in `blog` / `projects` / `field-notes` /
  `books`, **skipping drafts** (single-post mode operates on whatever path you
  pass, draft or not — you fill GEO right before publishing).
- `--force` flag — overwrite existing GEO fields instead of filling only empties.

**Behaviour**

1. Load the post(s) via `posts.mjs`.
2. For each, build a prompt from the title, description, and `stripMdx` body,
   and call `lmstudio.chatJson` with a schema covering exactly three fields:
   - `aiSummary` — string, 1–2 sentences, third person, names the entity.
   - `keyTakeaways` — array of 3–6 short strings.
   - `faq` — array of `{ question, answer }`, 0–4 items (may legitimately be
     empty for some posts).
3. Validate the shape; on failure, log and skip that post (no partial write).
4. `mergeFrontmatter` with `{ force }`, `serialize`, write the file.
5. Print a per-post colored diff of the frontmatter block and a summary line
   (`N filled, M skipped (already populated), K failed`).

**Boundaries:** never touches the body; never generates `entityMentions` /
`sources` / `imageAlt`; never commits. `books` posts use the same three-field
schema (the `books` schema carries the same fields).

## 8. Component — #6 related posts (`scripts/related-rebuild.mjs` + util change)

**The generator script**

1. `listPosts({ collections: ['blog','projects','field-notes'], includeDrafts:
   false })`.
2. For each post compute `contentHash`. Look it up in
   `src/data/related-posts.cache.json`, which is keyed by
   **`contentHash + ':' + AI_EMBEDDING_MODEL_ID`** — so editing a post *or*
   swapping the embedding model invalidates that entry. Cache hit → reuse the
   stored vector; miss → `lmstudio.embed(embedText(post))` and store it.
3. Compute pairwise cosine similarity. For each post, take the top 3 others
   with `score >= 0.6`, across all three collections.
4. Write `src/data/related-posts.json`:
   `{ "<collection>/<id>": [{ id, collection, score }, ...], ... }`
   The composite `collection/id` key avoids cross-collection id collisions.
5. Write the refreshed `src/data/related-posts.cache.json`.
6. Print a summary (`N posts embedded, M from cache, P posts with 0 relations`).

Both `src/data/*.json` files are **committed to git** — they are build inputs.

**The util change** (`src/utils/getPosts.ts`)

`getRelatedPostsCrossCollection(currentEntry, limit = 3)` is rewritten to:

1. Read `src/data/related-posts.json` (static import).
2. Look up `\`${currentEntry.collection}/${currentEntry.id}\``.
3. If absent (post not yet rebuilt, or nothing cleared the 0.6 floor) → return
   `[]`. `PostFooterNav` then renders nothing — an honest "no strongly-related
   posts" instead of weak filler. **This is a deliberate behaviour change** from
   the current always-returns-3.
4. If present → resolve each `{ id, collection }` to its `CollectionEntry` via
   the existing `getBlogPosts` / `getProjects` / `getFieldNotes` loaders,
   preserving score order, capped at `limit`. Drop any entry that no longer
   resolves (e.g. deleted post) without failing the build.

The standalone `getRelatedPosts` heuristic (used elsewhere) is left untouched.
No layout or component files change.

## 9. Component — #7 image helper (`scripts/image-watcher.mjs`)

**Invocation:** `npm run image:watch` — long-running; Ctrl-C to stop.

**Startup:** read `AI_VISION_MODEL_ID` (required — exit with a clear message if
unset); ping `lmstudio.listModels()` and warn if unreachable; ensure the inbox
root exists; start `chokidar` watching
`obsidian-templates/inbox/**` (ignoring dotfiles and partial-write temp files).

**On a new file (`add` event), processed one at a time (serialized queue):**

1. Derive `(collection, slug)` from the path
   `obsidian-templates/inbox/<collection>/<slug>/<filename>`. Reject — log and
   leave the file — if the path doesn't match this shape or `<collection>` is
   not a known content collection.
2. Validate that `src/content/<collection>/<slug>.mdx` exists; if not, log and
   leave the file (likely a typo in the folder name).
3. Read the image. If HEIC/HEIF, `sharp` transcodes to JPEG; if already
   `jpg`/`jpeg`/`png`/`webp`, it is used as-is. A JPEG/PNG buffer is always
   produced for the vision payload.
4. Call `lmstudio.vision(buffer, prompt, schema)` — the prompt includes the
   post body (for context) and asks for `{ altText: string }`: a concrete
   description of *what is in the image*, not what the post is about.
5. Write the result image to
   `src/assets/<collection>/<slug>/<sanitized-name>.<ext>` — `<ext>` is `jpg`
   for transcoded HEIC, otherwise the original extension; `<sanitized-name>` is
   the original basename lowercased, spaces→hyphens, non-`[a-z0-9-]` stripped.
   On filename collision, append `-2`, `-3`, …
6. Print a ready-to-paste Markdown snippet:
   `![<altText>](../../assets/<collection>/<slug>/<name>.<ext>)`.
7. Delete the source file from the inbox.

**Failure handling:** any step that throws logs the error and **leaves the
inbox file in place** (so it can be retried), then moves to the next item.

## 10. Testing + verification

**Runner:** Node's built-in `node:test` + `node:assert` — no new dependency.
Test files colocated as `scripts/lib/*.test.mjs` and `scripts/*.test.mjs`. New
npm script `test:scripts` → `node --test scripts/`.

**Unit tests (pure logic — no LM Studio):**

- `frontmatter-merge.test.mjs` — fill-only-missing vs `--force`; empty string /
  `[]` / `null` count as missing; existing key order preserved and new keys
  appended; body string byte-identical through a parse→merge→serialize round
  trip; array fields (`keyTakeaways`, `faq`) merged correctly.
- `posts.test.mjs` — walks all four collections; skips `_templates/`; derives
  the Astro `id` from nested paths; `contentHash` stable across re-read and
  changes when frontmatter or body changes; draft filtering honoured.
- `related-rebuild.test.mjs` — cosine math (orthogonal → 0, identical → 1);
  top-3 cap; 0.6 floor excludes; cross-collection inclusion; cache key includes
  the model id so a model change forces a re-embed; composite `collection/id`
  keying.
- `image-watcher.test.mjs` — path → `(collection, slug)` derivation; rejects
  paths outside known collections; filename sanitization; collision suffixing;
  HEIC vs web-format branch selection.

**LM Studio client:** `lmstudio.mjs` is tested by injecting a fake `fetch` —
assert it builds the right request body (`response_format` shape, model id,
multimodal `content` array for `vision`) and that it parses / throws correctly
on good and bad responses. No live calls in tests.

**Not unit-tested (manual only):** model output *quality*, `chokidar` file
events, `sharp` HEIC transcoding.

**Manual verification checklist** (documented in a new `scripts/README.md`):

1. `npm run geo:fill -- src/content/blog/<draft>.mdx` on a post with empty GEO
   fields → fields populated, diff printed, body byte-identical.
2. Re-run → no-op ("already populated"); add `--force` → regenerates.
3. `npm run related:rebuild` → `src/data/related-posts.json` written; every
   entry `score >= 0.6`, `<= 3` per post; cache file refreshed; second run
   reports all-from-cache.
4. `npm run build` → `PostFooterNav` related section renders for a post with
   relations, renders nothing for an orphan post.
5. `npm run image:watch`, drop a `.heic` into
   `obsidian-templates/inbox/blog/<slug>/` → JPEG appears under
   `src/assets/blog/<slug>/`, alt text + Markdown snippet printed, inbox source
   deleted. Drop into a bad folder name → file left in place, error logged.

**CI:** add `test:scripts` to `.github/workflows/ci.yml` alongside
`check` / `lint` / `test`. The LM-Studio-dependent steps stay manual (CI has no
LM Studio). Extend `dais-bridge.tests/ScaffoldingTests.cs` with a test asserting
the four new npm scripts (`geo:fill`, `geo:fill:all`, `related:rebuild`,
`image:watch`) and the two new deps (`gray-matter`, `chokidar`) exist in
`package.json` — consistent with that file's existing scaffolding-guard pattern.

## 11. Build sequencing

One implementation plan, sequenced tasks:

1. **Shared lib + deps + harness** — `scripts/lib/{lmstudio,posts,
   frontmatter-merge}.mjs`, add `gray-matter` + `chokidar`, the `node:test`
   harness, the `test:scripts` npm script, and the env-var wiring. Everything
   else depends on this.
2. **#5 GEO auto-fill** — `geo-fill.mjs` + `geo:fill` / `geo:fill:all` scripts.
   Depends only on the lib.
3. **#6 related posts** — `related-rebuild.mjs` + `related:rebuild` script + the
   `getRelatedPostsCrossCollection` rewrite + `src/data/` files. Depends only on
   the lib.
4. **#7 image helper** — `image-watcher.mjs` + `image:watch` script. Depends
   only on the lib; independent of #5 and #6.
5. **Wrap-up** — `scripts/README.md`, CI step, `ScaffoldingTests.cs` guard,
   CLAUDE.md command-table update.

Tasks 2–4 are mutually independent; sequencing them keeps review simple.

## 12. Risks

- **Model output shape drift** — a model swap could change output structure.
  Mitigated by `response_format: json_schema` plus explicit shape validation in
  `chatJson` / `vision` that fails loud rather than writing garbage.
- **Embedding-model drift** — switching the embedding model would silently mix
  incompatible vector spaces. Mitigated by keying the #6 cache on
  `contentHash + AI_EMBEDDING_MODEL_ID`, so a model change forces a clean
  re-embed.
- **Structured-output / vision support is model-dependent** — not every locally
  loaded model supports `json_schema` or images. Mitigated by the startup
  `/v1/models` ping (#7) and loud per-post failures (#5) rather than silent
  bad writes.
- **`gray-matter` re-serialization cosmetics** — round-tripping YAML can shift
  quoting/whitespace even when values are unchanged. Mitigated by the
  field-order + body-identity unit test; any remaining cosmetic churn is
  caught in the author's pre-commit diff review.
