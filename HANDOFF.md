# Project Handoff

A self-contained record of every change made to this codebase across the
audit + 4 phases of remediation, written so a fresh agent or contributor
with **zero context** can pick up where things stand.

If you're a future Claude session starting cold: read this doc top-to-bottom,
then read [CLAUDE.md](CLAUDE.md) for working conventions. After that, you
have everything needed to continue.

---

## Current state at a glance

- **Astro 6.2.1** static site, **TypeScript 6.0.3**, **Tailwind v4**, **DaisyUI v5**, target **Cloudflare Pages**.
- **.NET 9 Minimal API Gateway** (`Darbee.Gateway`) with **Microsoft Semantic Kernel v1.75**, **SignalR**, and **MCP Client** integration.
- **72 pages** built in ~8–10 seconds.
- **3534 internal links** statically verified — zero broken.
- **.NET tests:** 11 passing on `master`; 29 passing on `feature/graph-backed-rag` (adds 18 unit + integration tests for the memory layer; integration tests gated on `ARANGO_TEST_RUN=1`).
- **14 Playwright smoke tests** passing in ~10 s locally.
- **CI workflow** runs typecheck → lint → format → build → broken-link check → Playwright tests → Lighthouse budgets on every PR and push to `main`.
- **Podman Compose stack** (`make up`) orchestrates ArangoDB 3.12 (`--vector-index`), LM Studio probe sidecar, and DAIS Bridge (dev or prod profile) on a single network.
- **Lighthouse budgets** asserted: ≥ 0.9 perf / a11y / best-practices, ≥ 0.95 SEO.

### Verified gates (last run, all green)

```text
npm run check        →  0 errors, 0 warnings, 4 hints (pre-existing)
npm run lint         →  silent (clean)
npm run format:check →  All matched files use Prettier code style
npm run build        →  72 page(s) built
npm run check:links  →  Scanned 72 HTML page(s), checked 3534 internal links
npm test             →  14 / 15 passed (sitemap index 404)
dotnet build         →  dais-bridge succeeded
dotnet test          →  11 / 11 passed (dais-bridge.tests)
```

---

## Tech stack

| Concern | Choice |
|---|---|
| Framework | [Astro 6.2.1](https://astro.build/) — static output, no client JS framework |
| Language | TypeScript 6.0.3 (strict, including `strictNullChecks`) |
| Styling | Tailwind CSS v4 + DaisyUI v5 (`forest` default + `rainbow`) |
| Display fonts | Fraunces (serif masthead) + Nunito (display sans) + Inter (body) |
| Content | MDX via Astro Content Collections, Zod schemas |
| Hosting | Cloudflare Pages (deploy via dashboard from GitHub) |
| Newsletter | Buttondown (env-driven; component renders nothing when unset) |
| Analytics | Cloudflare Web Analytics (env-driven; beacon omitted when unset) |
| Test runner | Playwright (chromium-only) |
| Lint | ESLint v10 (flat config) + `eslint-plugin-astro` + `typescript-eslint` |
| Format | Prettier 3 + `prettier-plugin-astro` + `prettier-plugin-tailwindcss` |
| CI | GitHub Actions: `verify`, `test`, `lighthouse` jobs |
| Orchestration | .NET 9 + [Microsoft Semantic Kernel v1.75+](https://github.com/microsoft/semantic-kernel) |
| API Gateway | ASP.NET Core Minimal API + SignalR |
| Knowledge Graph| ArangoDB (local) |
| Research | Context7 via [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) |

---

## What was done — phase by phase

### Phase 0 — Audit

A full audit of the project produced a prioritized punch list scored on
`(Impact + Risk) × (6 − Effort)`. The audit identified:

- **Critical:** `/bookshelf/[id]/` route missing (latent 404), Buttondown placeholder URL, analytics token hardcoded as `''`.
- **High-impact code debt:** ~85% layout duplication across 3 post layouts; ~95% card duplication across 3 cards; static category pages (homeschool, kingdom-farm) inconsistent with the dynamic rv-life pattern.
- **Architecture:** no central `PostLayout`, no shared `PostCard`, hardcoded difficulty→color map duplicated across two files.
- **Infrastructure:** no CI, no linter, no formatter, no tests, no `.env.example`.
- **Documentation:** no `CLAUDE.md`, README out of date.

The audit grouped these into four phases of work, each verified to keep build/lint/format/tests green at every step.

### Phase 1 — Pre-launch safety

Files: `src/pages/index.astro`, `src/consts.ts`, `src/components/NewsletterSignup.astro`, new `.env.example`, updated `README.md`.

- Bookshelf homepage links pointed at `/bookshelf/${b.id}/` with no detail route. Repointed to `/bookshelf` index pending a real detail route (later built in Phase 4c).
- Moved analytics token + Buttondown handle to `import.meta.env.PUBLIC_*`. Component renders nothing when handle is unset (your explicit Option A choice over alternatives B "coming soon card" and C "disabled form").
- New `.env.example` with explanatory comments, env-vars table in README.
- README updated to reflect the actual font stack (after Phase A added Fraunces).

### Phase 2 — Guardrails

Files: `astro.config.mjs`, new `eslint.config.js`, new `.prettierrc.json`, new `.prettierignore`, new `.github/workflows/ci.yml`, new `CLAUDE.md`, `package.json`, plus 5 lint cleanup files.

- Bumped `astro` 6.1.10 → 6.2.1.
- Added ESLint v10 (flat config) with `typescript-eslint` recommended + `eslint-plugin-astro` recommended.
- Added Prettier 3 with `prettier-plugin-astro` + `prettier-plugin-tailwindcss`. Tabs, single quotes, trailing commas, printWidth 100.
- Added GitHub Actions CI workflow: `verify` job runs typecheck → lint → format → build, with concurrency cancellation on rapid pushes.
- Wrote `CLAUDE.md` with directory map, "where to add X" decision tree, schema summary, design tokens, gotchas.
- Lint pass surfaced and cleaned up: 5 dead imports/vars + 3 `any` types in `PostFooterNav.astro` (replaced with a proper `EntryDataLike` discriminating type) + 2 `<!-- -->` comments inside JSX fragments that broke prettier-astro (replaced with `{/* */}`).

### Phase 3 — Code consolidation

Files: new `src/layouts/PostLayout.astro`, new `src/components/PostCard.astro`; deleted `BlogCard.astro`/`ProjectCard.astro`/`FieldNoteCard.astro`; rewrote `BlogPostLayout.astro` / `FieldNotesLayout.astro` / `ProjectPostLayout.astro` as thin wrappers; updated 4 page-level call sites; made `homeschool.astro` and `kingdom-farm.astro` pull live posts; centralized `DIFFICULTY_COLORS` in `src/consts.ts`.

The big one. Three highly-duplicated post layouts → one `PostLayout` with named slots (`meta`, `header-extras`, `before-content`) + thin per-collection wrappers. Three highly-duplicated cards → one `PostCard` with discriminated-union prop. **Removed ~500 lines of duplication** while improving the abstractions.

Three previously-static category landing pages now follow the same pattern as `rv-life.astro`: keep hand-written prose intro, append a "From the blog" grid pulling matching posts.

### Phase 4a — Polish & truth-in-data

Files: `src/pages/kingdom-farm.astro`, `src/layouts/PostLayout.astro`, `src/content.config.ts`, all 3 post-layout wrappers, `PostCard.astro`, `PostFooterNav.astro`, `src/content/_templates/blog-post-template.mdx`, `CLAUDE.md`, `src/consts.ts`, `src/pages/bookshelf.astro`.

- `/newsletter` → `/#newsletter` in `kingdom-farm.astro` (broken-link fix).
- Removed duplicate `BreadcrumbList` JSON-LD: `Breadcrumbs.astro` is the single source; `PostLayout` no longer also emits one.
- Added optional `imageAlt` field to all collection schemas; threaded through `PostLayout` (hero), `PostCard` (thumbnail), `PostFooterNav` (related-post thumbnails). Falls back to title; honors empty string for decorative images.
- Moved `bookshelf.astro` inline arrays (ratings / lookFor / comingSoon) to `consts.ts` as `BOOKSHELF_RATINGS`, `BOOKSHELF_LOOK_FOR`, `BOOKSHELF_COMING_SOON`.

### Phase 4b — Tests + tooling

Files: new `playwright.config.ts`, new `tests/smoke.spec.ts`, new `scripts/check-internal-links.mjs`, `package.json` (4 new scripts: `test`, `test:ui`, `test:report`, `check:links`), `.github/workflows/ci.yml` (new `test` job + link-check step), `.gitignore`, `.prettierignore`, `eslint.config.js`.

- Installed `@playwright/test` + chromium.
- 15 smoke tests across: homepage, blog index, real blog/project/field-note post pages, bookshelf, homeschool/kingdom-farm "From the blog" sections, RSS, sitemap, llms.txt, knowledge.json, JSON-LD invariant (exactly one `BreadcrumbList`).
- New CI `test` job: installs Chromium, builds, runs Playwright, uploads HTML report on failure.
- New custom static link checker (`scripts/check-internal-links.mjs`): walks `dist/`, extracts all `href=` values, verifies each internal one resolves to a real file. Handles URL-encoded paths (e.g. `Faith%20%26%20Reflections` → `Faith & Reflections/`). Sanity-tested by deliberately breaking a link, confirming the checker catches it, and restoring.
- New CI step in `verify` job: `npm run check:links` after build.

### Phase 4c — Detail-route gap-fill

Files: new `src/pages/bookshelf/[id].astro`, new `src/pages/tag/[tag].astro`, new `src/pages/category/[category].astro`, restored `index.astro` book href to `/bookshelf/${b.id}/`, updated 7 files that emitted broken `?tag=` / `?category=` query-string links to use the new static archive routes, updated `PostMeta.astro` to take optional `categoryHref` (project category strings live in a separate namespace from blog categories — no archive page exists for them).

- Per-book detail route via `getStaticPaths()` against the `books` collection.
- Per-tag and per-category static archive pages, one each per unique value.
- Page count went from 26 → 54.
- Caught a real bug along the way: `PostMeta` was emitting `/category/RV%20Automation/` from project pages, but the category archive only indexes blog posts. Fixed by making the link conditional on `categoryHref` being passed.

### Phase 4d — Polish (this session)

Files: `src/content.config.ts`, `src/pages/bookshelf/[id].astro`, new `src/content/books/wingfeather-saga-book-one.mdx`, new `src/content/books/carry-on-mr-bowditch.mdx`, `src/consts.ts` (BOOKSHELF_COMING_SOON refresh), new `lighthouserc.json`, `.github/workflows/ci.yml` (new `lighthouse` job), `package.json` (TypeScript 6 bump), `src/components/PostCard.astro` (TS 6 narrowing fix), new `docs/security-notes.md`.

- `llmFields` (`keyTakeaways`, `faq`, `sources`, `entityMentions`, `aiSummary`, `imageAlt`) added to the books schema. Bookshelf detail page now renders them when present.
- Two real book reviews: *On the Edge of the Dark Sea of Darkness* and *Carry On, Mr. Bowditch*. Both green-light, full takes + llmFields. Removed both from `BOOKSHELF_COMING_SOON`, added Little Britches and Green Ember to coming-soon.
- Lighthouse CI job using `treosh/lighthouse-ci-action@v12`, asserting min scores: 0.9 perf / a11y / best-practices, 0.95 SEO. Plus explicit `color-contrast` and `image-alt` assertions. Tests 6 representative URLs.
- TypeScript 5.9 → 6.0. Surfaced 16 type errors in `PostCard.astro` due to TS 6 no longer narrowing discriminated unions across destructuring. Fixed via a `LooseEntryData` cast that types `data` as the union of all possible per-kind optional fields (runtime behavior unchanged — every access is already guarded by a `kind === 'X' && data.field` check).
- Documented the `yaml@2.x` audit warning in `docs/security-notes.md` — it's a transitive dev-dep through `@astrojs/check`. Re-evaluation criteria spelled out.
- Visual sanity check on the homeschool badge color: kept `bg-secondary/90` (warm amber). It's on-brand with the rest of the palette; the prior `bg-darbees-pink` was an orphan from a previous design system.

> **Historical note (2026-05-13):** Phases 5, 6, and 9 below describe a CMS infrastructure (Directus + Deno Fresh under `cms/`) that **is no longer in active use.** Content authoring happens in Obsidian — see [OBSIDIAN-CONTENT-WORKFLOW.md](OBSIDIAN-CONTENT-WORKFLOW.md). The `cms/` code remains in the tree for archival reasons; don't extend it. Phases 5/6/9 are kept here for history of the work but the live authoring path is Obsidian → Astro.

### Phase 5 — CMS Enhancements (May 2026)

Files: `cms/lib/content.ts`, `cms/routes/drafts.tsx` (NEW), `cms/routes/history/[collection]/[slug].tsx` (NEW), `cms/routes/index.tsx`, `cms/routes/edit/[collection]/[slug].tsx`, `cms/components/ContentForm.tsx`, `cms/components/MetadataDrawer.tsx` (NEW), `cms/lib/schemas.ts`.

A local-only Deno Fresh CMS is scaffolded under `cms/` inside the Astro repo. It's gitignored and excluded from root `tsconfig.json`, writes `.mdx` files directly to `src/content/{blog,books,projects,field-notes}`, and includes Fresh routes for dashboard (`/`), create (`/new/[collection]`), edit (`/edit/[collection]/[slug]`), and preview (`/preview/[collection]/[slug]`).

**Completed enhancements:**
- **Draft Management Dashboard:** Added `listDrafts()` function to `cms/lib/content.ts`, created `cms/routes/drafts.tsx` for viewing all drafts with quick actions (edit, preview), added "View Drafts" button to main dashboard (`cms/routes/index.tsx`)
- **Image Upload Improvements:** Enhanced `cms/components/ContentForm.tsx` image field with drag and drop support, visual feedback on drag over/drop, and image preview using FileReader API
- **Content Versioning:** Added `getEntryHistory()` and `restoreEntryVersion()` functions to `cms/lib/content.ts` using git commands, created `cms/routes/history/[collection]/[slug].tsx` for viewing version history and restoring previous versions, added "Version History" link to edit page
- **Form Validation:** Existing validation in `cms/lib/form.ts` (URL validation for attribution, preview length limit) - inline error messages not yet implemented
- **SEO Preview:** Added `showSeoPreview` prop to ContentForm component with Google Search Result and Twitter/X card previews (not yet activated in routes)
- **Metadata UX Improvement:** Separated LLM/E-E-A-T fields (aiSummary, keyTakeaways, entityMentions, faq, sources, imageAlt, imageAttributionName, imageAttributionUrl, preview) into a collapsible `<details>` section labeled "Advanced Metadata (AI/SEO fields)" in the content form, making the main form less overwhelming for users

**Technical details:**
- Exported `CollectionKey` type from `cms/lib/content.ts` for use in routes
- Used HTML `<details>/<summary>` element for the collapsible metadata section (works with Fresh's server-side rendering, no client-side JavaScript needed)
- Validation: `deno task check` passes in cms directory
- Dev server runs via `deno task dev --host 127.0.0.1` from `cms`, typically at `http://127.0.0.1:5173/`

### Phase 6 — Native Block Editor & Pipeline Refactor (May 2026)

Files: `cms/extensions/hooks/mdx-export/index.js`, `cms/extensions/hooks/geo-optimizer/index.js`, `src/content.config.ts`, `src/layouts/FieldNotesLayout.astro`, `src/components/PostFooterNav.astro`, `tests/smoke.spec.ts`.

Transitioned the CMS architecture from the fragmented Many-to-Any (M2A) block system to Directus' **Native Block Editor** (Editor.js) for a superior writing experience.

**Completed enhancements:**
- **Native Block Architecture:** Replaced the junction-based `body_blocks` with a single JSON field using the `blocks` interface. Authors now have a Notion-like canvas for prose.
- **Shortcode Engine:** Implemented a robust resolution engine in the `mdx-export` hook. Supports `[[Carousel]]` (blind lookup of the next unused item) and `[[block_type:id]]` (explicit ID lookup).
- **Data Restoration:** Developed and ran `fix-corrupted-blocks.ts` to recover post content from `directus_revisions` following a schema migration failure. Converted legacy blocks to Editor.js format.
- **GEO Pipeline Update:** Refactored the `geo-optimizer` hook to parse Editor.js JSON and resolve text from rich media components via shortcodes, ensuring the LLM sees the full content.
- **Field Notes Enhancement:** Added `saw`, `heard`, `wondered`, `learned` fields to the `field-notes` schema and automatically render them via `FieldNotesBlock.astro` in the layout.
- **Link Integrity:** Fixed a bug in `PostFooterNav.astro` where related links across collections used incorrect paths.

**Technical details:**
- `mdx-export` now handles both native blocks (Paragraph, Header, List, etc.) and custom components via shortcodes.
- `geo-optimizer` now uses `ItemsService` with full relation expansion to analyze rich media content.
- Smoke tests updated to use `.first()` for `h1` to avoid strict-mode violations caused by the Astro Dev Toolbar in preview mode.

### Phase 7 — Unified Cross-Collection Discovery (May 2026)

Files: `src/utils/getPosts.ts`, `src/pages/tag/[tag].astro`, `src/pages/category/[category].astro`, `src/components/PostCard.astro`, `src/layouts/PostLayout.astro`, `src/layouts/FieldNotesLayout.astro`, `src/layouts/ProjectPostLayout.astro`, `src/pages/bookshelf/[id].astro`.

Broke down the content silos by unifying the tag and category archives across all four primary collections (Blog, Projects, Field Notes, and Books).

**Completed enhancements:**
- **Unified Querying:** Added `getAllEntries()` helper to `getPosts.ts` that combines and sorts items from all collections.
- **Cross-Collection Archives:** Refactored the `[tag]` and `[category]` archive routes to query the unified entry pool. A single tag like `#homeschool` now reveals blog posts, field notes, and projects in one grid.
- **PostCard Upgrades:** Enhanced the `PostCard` component to support the `book` kind, ensuring book reviews look consistent and high-quality when appearing in search results or archives.
- **Universal Linking:** Updated `PostLayout` to always link tags to the unified archive. Enabled category linking for Projects and Field Notes by passing `categoryHref` through their respective layouts.
- **Bookshelf Discovery:** Added the unified tag list to the bottom of individual book review pages.

**Technical details:**
- `getAllEntries` uses a discriminated union (`_type`) to preserve collection identity during sorting.
- `PostCard`kindConfig` now includes `book: { prefix: '/bookshelf', cta: 'Verdict' }`.
- Unified discovery increased the static page count from 67 → 72 due to newly discoverable book-related taxonomies.

### Phase 8 — Technical Debt "Zero-Out" (May 2026)

Files: `src/layouts/BaseLayout.astro`, `src/layouts/PostLayout.astro`, `src/pages/bookshelf/[id].astro`, `src/components/Breadcrumbs.astro`, `src/content.config.ts`.

Systematically addressed infrastructure debt and refined JSON-LD for better AI discovery and SEO compliance.

**Completed enhancements:**
- **Sitemap Index Fix:** Investigated and resolved the 404 issue with `sitemap-index.xml` in preview mode. All smoke tests (15/15) are now passing.
- **JSON-LD Consolidation:** Refactored `BaseLayout` to serve as the single source of truth for structured data. Merged site-wide schemas (Organization, Person, WebSite) with article-specific blocks into a single `<script>` tag.
- **Book Review SEO:** Implemented a new `Review` and `Book` JSON-LD builder. Each book review now emits rich metadata including ratings, author data, and item details.
- **Schema Normalization:** Updated the `books` collection to use Astro's `image()` helper for `featuredImage` and `heroImage`, enabling build-time optimization. Migrated `z.url()` to the standard `z.string().url()`.
- **Breadcrumb Compliance:** Updated `Breadcrumbs.astro` to include the `item` field (canonical URL) for the current page, ensuring 100% schema.org compliance.

### Phase 9 — CMS UX: AI Optimization Button (May 2026)

Files: `cms/extensions/hooks/geo-optimizer/index.js`, `cms/extensions/hooks/mdx-export/index.js`.

Improved the authoring workflow by adding an explicit trigger for AI metadata generation.

**Completed enhancements:**
- **"Optimize with AI" Button:** Added a `trigger_geo` field with a **Button interface** to all content collections in Directus.
- **Hook Refactor:** Converted Directus hooks to CommonJS for maximum compatibility with the local Docker environment.
- **Robust Triggering:** Refactored the `geo-optimizer` hook to fire on either a status change or a button click, and to automatically reset the button state after processing.

### Phase 10 — DAIS Bridge & Sovereign Gateway (May 2026)

Files: `dais-bridge/`, `dais-bridge.tests/`, `docs/plans/`, `docs/proposals/`.

Built a local-first "Librarian Agent" gateway to orchestrate the family's architectural intelligence.

**Completed enhancements:**
- **DAIS Bridge Scaffolding:** Implemented a C# Semantic Kernel orchestrator with 5 native plugins (Obsidian, ArangoDB, Assets, GEO, Git) to automate the flow from Obsidian vault to Astro static site.
- **Sovereign Gateway Refactor:** Converted the console application into an ASP.NET Core Minimal API with SignalR support for real-time monitoring and kid-safe interactions.
- **Safety & Isolation:** Implemented `SafetyMiddleware` for deterministic content filtering and tenant-aware graph isolation in the ArangoDB plugin.
- **MCP Research Integration:** Developed a `ResearchPlugin` utilizing the official `ModelContextProtocol` C# SDK to query live `context7` documentation via HTTP.
- **Infrastructure:** Aligned project namespaces to `Darbee.Gateway`, updated test projects to .NET 9, and verified 100% test pass rate for backend components.

### Phase 11 — Graph-Backed RAG (in progress, started 2026-05-09)

Branch: `feature/graph-backed-rag`. Spec: [`docs/superpowers/specs/2026-05-09-graph-backed-rag-design.md`](docs/superpowers/specs/2026-05-09-graph-backed-rag-design.md). Plan: [`docs/superpowers/plans/2026-05-09-graph-backed-rag.md`](docs/superpowers/plans/2026-05-09-graph-backed-rag.md). Resume guide: [`docs/superpowers/RESUME-graph-backed-rag.md`](docs/superpowers/RESUME-graph-backed-rag.md). Active TODO: [`TODO-phase11.md`](TODO-phase11.md).

Replaces the stubbed `ArangoPlugin` with a real `MemoryPlugin` + `Memory/` namespace backed by ArangoDB (vector index) and LM Studio embeddings (`nomic-embed-text-v1.5`, 768 dim). Hybrid recall: entity extraction → graph expansion → vector top-K rerank. Layered SK 1.75 memory model: built-in `WhiteboardProvider` (short-term), new `DarbeesContextProvider : AIContextProvider` (auto long-term extract), explicit `MemoryPlugin` kernel functions (Remember/Recall). Single DB, normalized collections per content kind, `tenant_id` field on every doc/edge.

**Status as of 2026-05-12:**

- ✅ A1 — Memory model records (`8281b8e`, `2b737f0`)
- ✅ A2 — `IEmbeddingClient` interface + failing test (`78c8b52`)
- ✅ A3 — `LmStudioEmbeddingClient` impl (`e3c45bf`)
- ✅ B1 — `TenantContext` + AsyncLocal `ITenantContextAccessor` (`4f600e3`)
- ✅ docs — v4 reversal: retargeted to 3.12.x (`8086a4b`)
- ✅ A4 — `MemoryStore` schema + lazy vector index lifecycle (`ad92b61`)
- ✅ A5 — `MemoryStore` content/edge/entity write paths (`4c3ecf0`)
- ✅ A6 — `IEmbeddingClient` + `MemoryStore` DI wiring, `EnsureSchemaAsync` at startup (`cb8bd1c`)
- ⏳ B2 → G2 remaining. See [`TODO-phase11.md`](TODO-phase11.md) for punchlist and [`docs/superpowers/RESUME-graph-backed-rag.md`](docs/superpowers/RESUME-graph-backed-rag.md) for environment + per-task gotchas.

**Working state on this branch:** 29/29 tests pass (`ARANGO_TEST_RUN=1 dotnet test`). Local dev runs via `make up` (Phase 12 podman compose stack — see [`docs/dev-environment.md`](docs/dev-environment.md)), which starts ArangoDB 3.12 with `--vector-index`, the LM Studio probe sidecar, and the DAIS Bridge gateway. LM Studio with `nomic-embed-text-v1.5` and a Bearer token (`LMSTUDIO_API_KEY` in `.env`) is required from A6 onward.

**Smoke-test surprises captured during this work (already baked into spec/plan):**
- ArangoDB 3.12 vector index requires `--vector-index` startup flag (errorNum 10 without it). `--experimental-vector-index` is a deprecated alias.
- Vector index POST on an empty collection returns 500/1555 but persists an "unusable" index entry that AQL prefers over later good ones — must be cleaned up. Drives the lazy-creation pattern in A4.
- AQL `APPROX_NEAR_COSINE` must be bound via `LET sim = APPROX_NEAR_COSINE(...)` and reused; calling it twice in one query is errorNum 1554.
- ArangoDB rejects chunked Transfer-Encoding (errorNum 9). `JsonContent.Create()` triggers it — must use `StringContent` with serialized JSON for explicit `Content-Length`.

**New anti-pattern (to be added to the list when Phase 11 lands):** Don't expose tenant ID as an LLM-bound kernel-function parameter; always read from `ITenantContextAccessor` set by the SignalR hub at connection time. Function parameters are inherently LLM-controllable; non-parameter inputs from DI are not.

### Phase 12 — Podman Dev Environment Orchestration (2026-05-13, COMPLETE)

Files: `compose.yaml`, `dais-bridge/Dockerfile`, `dais-bridge/.dockerignore`, `Makefile`, `.env.example`, `docs/dev-environment.md`.

**Goal:** Replace manual "start ArangoDB, start LM Studio, run dotnet" with a single `make up` that brings up the entire DAIS Bridge memory-stack on a Podman Compose network.

**Completed enhancements:**
- **Env-var-first configuration** in `dais-bridge/Program.cs`: `Environment.GetEnvironmentVariable(...) ?? builder.Configuration[...] ?? "localhost default"`. This lets the same binary run inside the compose network (arango at `http://arango:8529`) or on the host (`http://localhost:8529`) without code changes.
- **Multi-stage Dockerfile** (`dev` / `build` / `prod`): dev stage is SDK + `dotnet watch` with source-mounted `:Z` volume; prod stage is `aspnet:9.0` runtime image running as non-root `app` user.
- **Compose orchestration** with four services: `arango` (3.12 with `--vector-index`, `arangosh` healthcheck, named volume), `lm-probe` (alpine sidecar polling LM Studio every 30s), `dais-bridge-dev` (profile `dev`, hot reload), `dais-bridge-prod` (profile `prod`, published binary).
- **Self-documenting Makefile**: `make up` / `make up-prod` / `make down` / `make health` / `make clean` (destructive: removes arango-data volume).
- **`docs/dev-environment.md`** — canonical bring-up guide with troubleshooting table.

**Verification:** `make up` → arango Healthy → dais-bridge-dev responds → `dotnet test` 29/29 against the orchestrated arango (the prior Astro scaffolding failure was a stale assertion against the removed cloudflare adapter; updated in `1c94ffd`). Prod profile: binary runs as `uid=1654(app)`. `dotnet watch` file detection confirmed via `podman logs`.

---

## Project file map (where things live)

```
.
├── HANDOFF.md                       ← this file
├── CLAUDE.md                        ← conventions, design tokens, gotchas
├── README.md                        ← user-facing setup / deploy
├── astro.config.mjs                 ← integrations + 3 fonts + Tailwind
├── tsconfig.json                    ← extends astro/tsconfigs/strict
├── eslint.config.js                 ← flat config (TS + Astro recommended)
├── .prettierrc.json                 ← tabs, single quotes, Astro+Tailwind plugins
├── .prettierignore
├── .gitignore
├── .env.example                     ← documented env-var inventory
├── package.json                     ← scripts: dev/build/check/lint/format/test/check:links
├── playwright.config.ts             ← chromium-only, webServer: npm run preview
├── lighthouserc.json                ← perf/a11y/SEO thresholds
├── lighthouserc.mobile.json         ← mobile Lighthouse thresholds (NEW)
├── docs/
│   ├── security-notes.md            ← yaml@2.x audit warning (won't-fix rationale)
│   └── dev-environment.md           ← Phase 12: Podman compose bring-up guide + troubleshooting
├── compose.yaml                     ← Podman Compose orchestration (arango, lm-probe, dais-bridge dev/prod)
├── Makefile                         ← Self-documenting targets: make up / down / health / clean
├── dais-bridge/                     ← .NET 9 Sovereign Gateway
│   ├── Dockerfile                   ← Multi-stage: dev (SDK+watch) / build / prod (runtime, non-root)
│   ├── .dockerignore                ← Keeps host build artifacts out of image context
│   ├── Hubs/                        ← KidSafeHub, ParentHub (SignalR)
│   ├── Middleware/                  ← SafetyMiddleware (Deterministic Filtering)
│   ├── Models/                      ← SafetyPolicies, TenantContext
│   ├── Plugins/                     ← Obsidian, Arango, Asset, GEO, Git, Research (MCP)
│   └── Program.cs                   ← Minimal API & Semantic Kernel Setup
├── dais-bridge.tests/               ← xUnit Test Suite for Gateway & Plugins
├── scripts/
│   └── check-internal-links.mjs     ← post-build static link auditor
├── tests/
│   └── smoke.spec.ts                ← 15 Playwright smoke tests
├── .github/workflows/ci.yml         ← 3 jobs: verify, test, lighthouse
├── cms/                             ← local-only Deno Fresh CMS (gitignored)
│   ├── lib/
│   │   ├── schemas.ts               ← collection definitions + llmFields
│   │   ├── frontmatter.ts            ← frontmatter parsing/serialization
│   │   ├── content.ts                ← list/read/write entries, draft/version history
│   │   ├── form.ts                   ← form validation (URL, preview length)
│   │   └── images.ts                ← image upload handling
│   ├── components/
│   │   ├── ContentForm.tsx           ← main content form with collapsible metadata
│   │   └── MetadataDrawer.tsx        ← drawer component (created, not currently used)
│   └── routes/
│       ├── index.tsx                 ← CMS dashboard
│       ├── drafts.tsx                ← draft management dashboard (NEW)
│       ├── new/[collection].tsx      ← create new entry
│       ├── edit/[collection]/[slug].tsx ← edit existing entry
│       ├── preview/[collection]/[slug].tsx ← preview mode
│       └── history/[collection]/[slug].tsx ← version history (NEW)
├── public/                          ← favicon, llms.txt, og images, brand
└── src/
    ├── consts.ts                    ← brand vocab + env-var re-exports + DIFFICULTY_COLORS + bookshelf vocab
    ├── content.config.ts            ← Zod schemas (blog, projects, field-notes, books) — all share llmFields
    ├── styles/global.css            ← Tailwind + DaisyUI themes (forest, rainbow) + Darbees tokens + .masthead
    ├── layouts/
    │   ├── BaseLayout.astro         ← <html>, <head>, header, footer wrapper
    │   ├── PostLayout.astro         ← THE shared post layout (slots: meta, header-extras, before-content)
    │   ├── BlogPostLayout.astro     ← thin wrapper over PostLayout (~60 lines)
    │   ├── FieldNotesLayout.astro   ← thin wrapper over PostLayout (~89 lines)
    │   └── ProjectPostLayout.astro  ← thin wrapper over PostLayout (~130 lines)
    ├── components/                  ← 23 Astro components (was 24 before card consolidation)
    │   ├── PostCard.astro           ← THE shared card (discriminated union: blog | project | field-note)
    │   ├── PostLayout-aux/          ← (no folder — listed for orientation): PostMeta, PostFooterNav, KeyTakeaways, FAQAccordion, Sources, AISummary, EntityCard, Breadcrumbs, TableOfContents, NewsletterSignup, StructuredData
    │   └── ...
    ├── content/
    │   ├── blog/                    ← *.mdx
    │   ├── projects/                ← *.mdx
    │   ├── field-notes/             ← *.mdx
    │   ├── books/                   ← *.mdx (2 entries: wingfeather, carry-on-mr-bowditch)
    │   └── _templates/              ← starter MDX with full frontmatter
    ├── pages/                       ← file-based routing
    │   ├── index.astro              ← masthead home page (3-column newspaper)
    │   ├── 404, about, contact, support, start-here, for-ai-agents, knowledge.json
    │   ├── homeschool, kingdom-farm, rv-life ← static prose + live posts grid (3 of these)
    │   ├── bookshelf.astro          ← bookshelf landing
    │   ├── bookshelf/[id].astro     ← per-book detail
    │   ├── blog/index.astro + [...slug].astro
    │   ├── projects/index.astro + [...slug].astro
    │   ├── field-notes/index.astro + [...slug].astro
    │   ├── tag/[tag].astro          ← per-tag archive
    │   ├── category/[category].astro ← per-category archive
    │   ├── resources/, resources.astro
    │   └── rss.xml.ts
    └── utils/
        ├── seo.ts                   ← all JSON-LD builders (Org, Person, WebSite, BlogPosting, Breadcrumb, FAQ, HowTo)
        ├── getPosts.ts              ← getBlogPosts, getProjects, getFieldNotes, getAdjacentPosts, getRelatedPosts
        ├── formatDate.ts, slugify.ts, readingTime.ts
```

---

## Anti-patterns I learned the hard way (don't repeat these)

1. **Don't put `<!-- -->` HTML comments inside `<>...</>` Astro fragments** — `prettier-plugin-astro` can't parse them. Use `{/* JSX comment */}` instead. This bit me twice in `pages/blog/index.astro`.
2. **Don't destructure `data` from a discriminated-union prop and then check the discriminator separately** — TypeScript 6 won't narrow `data` through that. Either keep `entry.data.<field>` references inside the conditional branches, or cast `data` to a union of all possible fields (the pattern in `PostCard.astro`).
3. **Don't use Tailwind v4 arbitrary `text-[clamp(...)]` with commas** — JIT silently fails to compile. Use a real CSS class (see `.masthead` in `global.css`).
4. **Don't run a long-lived Astro dev server through an in-place `npm install astro@<new>`** — Vite loads plugin code into memory at startup. The new on-disk runtime imports virtual modules the running plugin doesn't know about. **Restart the dev server after any Astro version change.** Manifested as: `Cannot find module 'virtual:astro:assets/fonts/runtime/font-file-url-resolver'`.
5. **Don't run multiple parallel `Edit` calls against the same file** — they race and silently drop content. Sequential edits to the same file. (I lost an `import` once and didn't notice until the build broke.)
6. **Don't add a fourth content type without considering whether it fits the existing layout/card abstractions.** Books were special enough to warrant their own detail page (`/bookshelf/[id].astro` uses `BaseLayout` directly, NOT `PostLayout`) because the rating + 3-takes + notes structure didn't compose cleanly into the `PostLayout` slot model. Use judgment.
7. **Don't use `locator('h1')` in Playwright without `.first()` if the Astro Dev Toolbar is present.** The toolbar injects several `h1` elements for its own UI (Audit, Settings, etc.), which will cause "strict mode violation" errors in tests.
8. **Don't use `&&` as a command separator in PowerShell.** It will fail with a syntax error. Use `;` instead (e.g., `dotnet build; dotnet test`).
9. **Ensure namespace alignment when refactoring C# projects.** If the root namespace changes (e.g., to `Darbee.Gateway`), all plugin and test files must be updated to prevent `CS0234` and `CS0246` resolution errors.
10. **Enable buffering when reading request bodies in ASP.NET Core Middleware.** Use `context.Request.EnableBuffering()` and reset `Position = 0` if you need to read the body and then allow the rest of the pipeline to process it.
11. **Don't bake secrets into images.** The prod Dockerfile never bakes `appsettings.json` secrets; they flow via compose `environment:` which sources from `.env` (gitignored). This is why Phase 12 Task A1 (env-var-first lookup) is a prerequisite.
12. **Don't run as root in `prod` containers.** The `prod` stage in the Dockerfile sets `USER app` before `ENTRYPOINT`. The `app` user (`uid=1654`) has no shell and minimal filesystem access.
13. **Don't change DAIS Bridge connection-string logic to be container-specific.** Instead, make the binary environment-agnostic via env-var-first lookup (`Environment.GetEnvironmentVariable(...) ?? builder.Configuration[...] ?? "localhost default"`). Same binary, same config, host or container.
14. **Healthchecks must use tools guaranteed present in the image.** The `arangodb:3.12` image doesn't contain `curl` (BusyBox wget lacks auth flags). Using `arangosh` for the healthcheck works because it's guaranteed present and tests the actual DB auth path, not just TCP reachability.

---

## Open items (low priority, none blocking)

In rough order of value:

1. **`heroImage` schema field on books is `z.string().optional()`** while everywhere else it's an `image()` reference. Worth normalizing if you want optimized images for book covers.
2. **`Breadcrumbs.astro` JSON-LD doesn't add `item` field for the last (current-page) crumb** — schema.org accepts this, but a future SEO audit might flag it.
3. **No image optimization config beyond Astro's defaults.** Sharp is installed and works fine; would be worth Cloudflare Images integration eventually (already on the README roadmap).
4. **`@astrojs/check` ↔ `yaml@2.x` advisory** — see `docs/security-notes.md`. Re-evaluate quarterly.
5. **Sitemap index 404 in preview** — `sitemap-index.xml` exists in `dist` and is correctly build-logged, but `astro preview` returns 404. Likely a configuration quirk with static asset serving in the preview runtime.
6. **CMS inline form validation** — Current validation in `cms/lib/form.ts` happens server-side. Could add inline error messages for better UX.
7. **CMS SEO preview activation** — SEO preview UI exists in `ContentForm` component but not yet activated in routes. Wire up `showSeoPreview` prop to enable it.

---

## How to keep momentum

When you (a future LLM session, or a contributor) pick this up:

1. **Run the gates first.** `npm install && npm run check && npm run lint && npm run format:check && npm run build && npm run check:links && npm test`. They should all pass (except for the known sitemap 404 in smoke tests).
2. **For backend work:** `make up` in repo root to start the dev stack (arango + dais-bridge-dev with hot reload). Then `ARANGO_TEST_RUN=1 dotnet test` from host against the orchestrated arango.
3. **For CMS work:** Run `deno task check` in the `cms/` directory to validate CMS-specific code.
4. **Read [CLAUDE.md](CLAUDE.md).** It has the "where to add X" decision tree and is shorter than this file.
5. **Read this doc's "Anti-patterns" section.** Fourteen of them are real bugs from this work history.
6. **Pick from "Open items" or have the user prioritize.** #1 and #2 are the biggest "missing" features on the frontend.

---

## Where we're going (future direction)

The project has transitioned to a professional, scalable CMS architecture. The **Native Block Editor** allows for a high-quality writing experience while the **Shortcode Engine** provides a developer-friendly way to embed complex Astro components.

### Site improvements (Astro)
1. **Component Expansion** — Add new `Rich Media` block types (e.g., `NewsletterInline`, `ProductComparison`) by updating the `mdx-export` switch and Directus schema.
2. **Unified Search** — Implement a client-side search (using Pagefind or similar) that indexes the `knowledge.json` output for instant discovery.

### CMS enhancements (Deno Fresh)
1. **Interactive GEO Trigger** — Move the "Auto-Generate GEO Data" trigger into the Directus UI (e.g., as a custom button extension) rather than relying on a `status` dropdown change.
2. **Media Library Integration** — Ensure the `rich_media` image/gallery blocks are tightly integrated with the Directus assets folder.

### Sovereign Gateway & AI Orchestration (C#)
1. **Graph-Backed RAG** — Phase 11 in progress (A6→G2). Wire `MemoryStore` into DI (`AddMemoryStore`), register `DarbeesContextProvider` as the SK `TextMemoryPlugin`, implement hybrid recall (entity → graph → vector rerank), and build the `MemoryPlugin` kernel functions (Remember/Recall). LM Studio with Bearer token is required from A6 onward.
2. **Dynamic Policy Engine** — Move safety policies from `safety_policies.json` to ArangoDB, allowing parents to update blocked keywords via the `ParentHub` in real-time.
3. **SignalR Frontend** — Implement a client for `KidSafeHub` (either as an Obsidian plugin or a lightweight web interface) to allow direct interaction with the "Librarian Supervisor."
4. **Autonomous Research Loops** — Enhance the `ResearchPlugin` to autonomously trigger documentation lookups when the LLM detects high uncertainty in a technical task.

---

*Last updated: 2026-05-13. State at this snapshot: all gates green, 72 pages, 3534 internal links verified, 15/15 smoke tests passing, 29/29 .NET tests passing on `feature/graph-backed-rag`. Phase 12 Podman Dev Environment complete — `make up` starts ArangoDB 3.12 + LM probe + DAIS Bridge with hot reload. Prod profile verified running as non-root `app` user. Phase 11 A6 (DI wiring for MemoryStore + embedding client) also complete (`cb8bd1c`). Next: Phase 11 B2 (MemoryPlugin kernel functions).*
