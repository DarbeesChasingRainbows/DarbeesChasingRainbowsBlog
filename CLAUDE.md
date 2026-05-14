# CLAUDE.md

Context for future Claude Code sessions. Read this first.

## What this is

A static blog at `darbeeschasingrainbows.com` — family / faith / RV life /
homeschool / Kingdom Farm. **Astro 6**, **Tailwind CSS v4**, **DaisyUI v5**,
**Cloudflare Pages**. 

- **Frontend**: Plain HTML output, no client JS framework.
- **Authoring**: **Obsidian** — open the repo folder as a vault, Templater scaffolds `.mdx` posts directly into `src/content/{blog,books,projects,field-notes}/`. See [OBSIDIAN-CONTENT-WORKFLOW.md](OBSIDIAN-CONTENT-WORKFLOW.md).
- **`cms/` directory**: historical only — earlier exploration of a Directus / Deno Fresh admin. **Not wired into the current authoring loop.** Don't add features there.

## Commands (Astro)

| Task                     | Command                | When             |
| ------------------------ | ---------------------- | ---------------- |
| Local dev (HMR)          | `npm run dev`          | Port 4321        |
| Production build         | `npm run build`        | Output → `dist/` |
| Preview production build | `npm run preview`      | After `build`    |
| Type check               | `npm run check`        | CI runs this     |
| Lint                     | `npm run lint`         | CI runs this     |
| Format check             | `npm run format:check` | CI runs this     |
| Broken link check        | `npm run check:links`  | CI runs this     |
| Playwright tests         | `npm test`             | CI runs this     |

CI workflow: [.github/workflows/ci.yml](.github/workflows/ci.yml). 

## Commands (Authoring scripts — Phase 13)

| Task                      | Command                          | When                  |
| ------------------------- | -------------------------------- | --------------------- |
| Fill GEO frontmatter      | `npm run geo:fill -- <post.mdx>` | Before publishing     |
| Fill GEO across the site  | `npm run geo:fill:all`           | Bulk backfill         |
| Rebuild related posts     | `npm run related:rebuild`        | After adding/editing posts |
| Watch image inbox         | `npm run image:watch`            | While adding photos   |
| Script unit tests         | `npm run test:scripts`           | CI runs this          |

Full guide: [scripts/README.md](scripts/README.md). All three call local LM Studio.

## Commands (DAIS Bridge / Phase 11 services)

| Task                     | Command                | When             |
| ------------------------ | ---------------------- | ---------------- |
| Bring up dev stack       | `make up`              | Start of session |
| Bring up prod-mode stack | `make up-prod`         | Pre-merge smoke  |
| Tear down                | `make down`            | End of session   |
| Health check             | `make health`          | After `make up`  |
| Tail bridge logs         | `make logs-bridge`     | Debugging        |
| Run host-side tests      | `ARANGO_TEST_RUN=1 dotnet test dais-bridge.tests/dais-bridge.tests.csproj` | CI runs this |

Full guide: [docs/dev-environment.md](docs/dev-environment.md).

## Directory map

```
cms/                 HISTORICAL — Directus + Deno Fresh exploration; not used. Don't extend.
src/
  components/        23 .astro components (centralized Card/Layout model)
  content/
    blog/            general blog posts (.mdx)
    projects/        DIY/build posts with parts list (.mdx)
    field-notes/     hikes, history, nature studies (.mdx)
    books/           bookshelf reviews (with llmFields)
    _templates/      starter MDX with all frontmatter fields filled in
  layouts/           BaseLayout + shared PostLayout (slots: meta, header-extras, before-content)
  pages/             file-based routing with static Tag/Category archives
  styles/global.css  Tailwind directives + DaisyUI themes + Darbees tokens
  utils/             formatDate, slugify, readingTime, getPosts, seo (JSON-LD)
  consts.ts          SITE_*, NAV_LINKS, BRAND_*, env-var re-exports
  content.config.ts  Zod schemas for all collections (sharing llmFields)
public/              static assets (favicon, llms.txt, og images, logo.svg)
```

## Where to put new code

| What you want to add                   | Where it goes                                                                                                                                                                                           |
| -------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A blog post                            | `src/content/blog/<slug>.mdx` — copy from `_templates/blog-post-template.mdx`                                                                                                                           |
| A project (DIY/build)                  | `src/content/projects/<slug>.mdx`                                                                                                                                                                       |
| A field note                           | `src/content/field-notes/<slug>.mdx`                                                                                                                                                                    |
| Reusable Astro UI element              | `src/components/<Name>.astro`                                                                                                                                                                           |
| Site-wide constant                     | `src/consts.ts`                                                                                                                                                                                         |

## Content collection schemas (summary)

All collections share an `llmFields` block for **GEO (Generative Engine Optimization)**: `aiSummary`, `keyTakeaways`, `entityMentions`, `faq`, `sources`, `imageAlt`.

- `imageAlt`: Accessible alt text for hero image. Defaults to post title.
- `books`:rating (green|yellow|parent-read|red), takes (dad, mom, kids).

Source of truth: [src/content.config.ts](src/content.config.ts). 

## Design tokens

- **Fonts**: Fraunces (Serif), Nunito (Display), Inter (Body).
- **Colors**: `primary` (Forest Green), `secondary` (Gold), `accent` (Terracotta), `base-100` (Cream).

## Routing

- Dynamic: `pages/blog/[...slug].astro`, `pages/projects/[...slug].astro`, `pages/field-notes/[...slug].astro`.
- Archives: `pages/tag/[tag].astro`, `pages/category/[category].astro`.
- Bookshelf: index + per-book detail route `pages/bookshelf/[id].astro`.

## Component conventions

- `.astro` files: tabs, single quotes, trailing commas.
- Comments: JSX fragments (`<>...</>`) **must** use `{/* */}`.
- Layouts: Use the centralized `PostLayout.astro` for all posts.
- Cards: Use the centralized `PostCard.astro` (discriminated union prop).

## Tech debt (prioritized)

1. **Cross-collection tag archives** — current archives only index blog posts. (Note: HANDOFF Phase 7 claimed this was fixed; verify.)
2. **Project Category archives** — no landing pages for project-specific categories.
3. **Lint cleanup** — ~55 errors remaining post-2026-05-13 cleanup (unused vars, legacy `require()` calls in scripts/, `this` aliases). Pre-existing, not session-introduced.
4. **Format check** — ~123 files flagged by `prettier --check` (mix of CRLF leftovers and real drift). Mostly cosmetic.

## Things to be careful about

- **LM Studio**: Used by the Phase 11 memory layer (`dais-bridge/Memory/`) for embeddings at `http://localhost:1234/v1`. Requires a Bearer token in `.env` as `LMSTUDIO_API_KEY`. The `lm-probe` sidecar in `compose.yaml` polls it every 30s and logs UP/DOWN — `make logs-lm` to watch. Inside containers, LM Studio is reached at `http://host.containers.internal:1234`.
- **Prettier Parse Errors**: HTML comments in JSX fragments break build.
- **Tailwind v4 JIT**: Avoid commas in arbitrary values in HTML; use CSS classes.
- **Typescript 6**: No narrowing across destructuring — cast `data` or use `entry.data.field`.

## When in doubt
Read [HANDOFF.md](HANDOFF.md) for a full history of remediation phases.
